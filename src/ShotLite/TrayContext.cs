using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;

namespace ShotLite;

/// <summary>Application root: tray icon, menu, global hotkeys. There is no main window.</summary>
internal sealed class TrayContext : ApplicationContext
{
    private const int CaptureHotkeyId = 1;
    private const int SaveFullHotkeyId = 2;
    private const int CopyFullHotkeyId = 3;
    private const int RecordHotkeyId = 4;

    private readonly AppSettings _settings;
    private readonly Icon _icon;
    private readonly NotifyIcon _tray;
    private readonly Notifier _notifier;
    private readonly ImageActions _actions;
    private readonly HotkeyManager _hotkeys = new();
    private readonly ToolStripMenuItem _captureItem;
    private readonly ToolStripMenuItem _recordItem;
    private readonly ToolStripMenuItem _saveFullItem;
    private readonly ToolStripMenuItem _copyFullItem;
    private readonly ToolStripMenuItem _openFolderItem;
    private readonly ToolStripMenuItem _settingsItem;
    private readonly ToolStripMenuItem _aboutItem;
    private readonly ToolStripMenuItem _exitItem;
    private readonly ToolStripMenuItem _updateItem;
    /// <summary>A real button in the menu once the update is downloaded; it replaces <see cref="_updateItem"/>.</summary>
    private readonly Button _restartButton = new()
    {
        AutoSize = true,
        UseVisualStyleBackColor = false,
        BackColor = Color.FromArgb(220, 252, 231), // light green: a system button that still stands out
        Cursor = Cursors.Hand,
    };
    private readonly ToolStripControlHost _restartHost;
    private readonly ToolStripSeparator _updateSeparator = new() { Visible = false };
    private readonly System.Windows.Forms.Timer _updateTimer = new();
    private UpdateInfo? _update;
    private bool _updateDownloading;
    /// <summary>Downloaded percent while <see cref="_updateDownloading"/>; -1 before the size is known.</summary>
    private int _updateProgress = -1;
    private bool _updateFailed;
    /// <summary>The update item was clicked: the menu stays open to show the progress.</summary>
    private bool _keepMenuOpen;
    /// <summary>The user confirmed the restart (or updates install automatically); only then may the setup run.</summary>
    private bool _updateApproved;
    private static readonly Color UpdateRed = Color.FromArgb(220, 38, 38);
    private static readonly Color UpdateGreen = Color.FromArgb(22, 163, 74);
    private string? _updateInstaller;
    private CaptureForm? _overlay;
    private bool _settingsOpen;
    private AboutForm? _about;
    private RecordingSession? _recording;

    public TrayContext()
    {
        _settings = AppSettings.Load();
        Localization.Apply(_settings.Language);
        _icon = AppIcon.LoadSmall();

        _captureItem = new ToolStripMenuItem(null, null, (_, _) => Deferred(StartCapture));
        _captureItem.Font = new Font(_captureItem.Font, FontStyle.Bold);
        _recordItem = new ToolStripMenuItem(null, null, (_, _) => Deferred(ToggleRecording)) { Visible = ScreenRecorder.IsSupported };
        _saveFullItem = new ToolStripMenuItem(null, null, (_, _) => Deferred(SaveFullScreen));
        _copyFullItem = new ToolStripMenuItem(null, null, (_, _) => Deferred(CopyFullScreen));
        _openFolderItem = new ToolStripMenuItem(null, null, (_, _) => OpenSaveFolder());
        _settingsItem = new ToolStripMenuItem(null, null, (_, _) => ShowSettings());
        _aboutItem = new ToolStripMenuItem(null, null, (_, _) => ShowAbout());
        _exitItem = new ToolStripMenuItem(null, null, (_, _) => ExitThread());
        _updateItem = new ToolStripMenuItem(null, null, (_, _) => OnUpdateClicked())
        {
            Visible = false,
            ForeColor = UpdateRed,
        };
        _updateItem.Font = new Font(_updateItem.Font, FontStyle.Bold);

        // Left-aligned with the texts of the other items (the host itself is as wide as the menu).
        int gap = _restartButton.LogicalToDeviceUnits(4);
        _restartHost = new ToolStripControlHost(_restartButton)
        {
            Visible = false,
            ControlAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(gap, gap, gap, gap),
        };
        _restartButton.Click += (_, _) =>
        {
            (_restartHost.GetCurrentParent() as ToolStripDropDown)?.Close();
            ApproveUpdate();
        };
        ApplyTexts();

        var menu = new ContextMenuStrip();
        // Downloading keeps the menu open to show the progress; the restart click doesn't need it.
        menu.ItemClicked += (_, e) => _keepMenuOpen = e.ClickedItem == _updateItem && UpdateInstaller.CanInstall && _updateInstaller == null;
        menu.Closing += (_, e) =>
        {
            if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked && _keepMenuOpen)
                e.Cancel = true;
            _keepMenuOpen = false;
        };
        menu.Items.AddRange(new ToolStripItem[]
        {
            _restartHost,
            _updateItem,
            _updateSeparator,
            _captureItem,
            new ToolStripSeparator(),
            _recordItem,
            _saveFullItem,
            _copyFullItem,
            new ToolStripSeparator(),
            _openFolderItem,
            new ToolStripSeparator(),
            _settingsItem,
            _aboutItem,
            new ToolStripSeparator(),
            _exitItem,
        });

        _tray = new NotifyIcon { Icon = _icon, Text = Strings.AppName, ContextMenuStrip = menu, Visible = true };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
                return;
            if (_recording != null)
                _recording.Stop();
            else
                Deferred(StartCapture);
        };

        _notifier = new Notifier(_tray, () => _settings.ShowNotifications);
        _actions = new ImageActions(_settings, _notifier);
        _actions.WorkFinished += LaunchUpdateWhenIdle;
        _hotkeys.Pressed += OnHotkey;

        // Follow the registry (the installer may have enabled autostart) and refresh the path in case the exe moved.
        _settings.RunAtStartup = Autostart.IsEnabled();
        if (_settings.RunAtStartup)
            Autostart.Set(true);

        // First check shortly after start (not to slow down login), then once a day.
        UpdateInstaller.DeleteDownloads();
        _updateTimer.Interval = 10_000;
        _updateTimer.Tick += (_, _) =>
        {
            _updateTimer.Interval = (int)TimeSpan.FromHours(24).TotalMilliseconds;
            CheckForUpdates();
        };
        _updateTimer.Start();

        bool hotkeysOk = RegisterHotkeys();
        if (hotkeysOk && !_settings.FirstRunDone)
        {
            _settings.FirstRunDone = true;
            _settings.Save();
            _notifier.Show(string.Format(Strings.FirstRun, HotkeyText.Format(_settings.HotkeyCapture.Keys)), force: true);
        }
    }

    private async void CheckForUpdates()
    {
        // An update being downloaded or waiting for the restart click keeps its version; a newer one comes after the restart.
        if (!_settings.CheckForUpdates || _updateDownloading || _updateInstaller != null)
            return;
        var update = await UpdateChecker.CheckAsync(); // continues on the UI thread
        if (update == null || !_settings.CheckForUpdates || _updateDownloading || _updateInstaller != null)
            return;

        _update = update;
        ApplyTexts();

        bool installable = UpdateInstaller.CanInstall;
        if (installable && _settings.AutoInstallUpdates && await InstallUpdate(quiet: true))
            return;

        var version = update.Version.ToString(3);
        if (_settings.LastNotifiedUpdate != version)
        {
            _settings.LastNotifiedUpdate = version;
            _settings.Save();
            _notifier.Show(string.Format(installable ? Strings.UpdateNotificationInstall : Strings.UpdateNotification, version), OnUpdateClicked);
        }
    }

    private async void OnUpdateClicked()
    {
        if (_updateInstaller != null)
            ApproveUpdate();
        else if (UpdateInstaller.CanInstall)
            await InstallUpdate(quiet: false);
        else
            OpenUpdatePage();
    }

    private void ApproveUpdate()
    {
        _updateApproved = true;
        ApplyTexts();
        LaunchUpdateWhenIdle();
    }

    /// <param name="quiet">Automatic update: a failure stays silent and falls back to the normal notification.</param>
    /// <returns>True when the verified installer is ready. A manual update then waits for the restart click.</returns>
    private async Task<bool> InstallUpdate(bool quiet)
    {
        if (_update == null || _updateDownloading || _updateInstaller != null)
            return true;

        _updateDownloading = true;
        _updateFailed = false;
        _updateProgress = -1;
        ApplyTexts();
        // Created on the UI thread, so the reports arrive there and can update the open menu.
        var progress = new Progress<int>(percent =>
        {
            _updateProgress = percent;
            ApplyTexts();
        });
        if (!quiet)
            _notifier.Show(Strings.UpdateDownloading, force: true);

        string? failure = null;
        try
        {
            _updateInstaller = await UpdateInstaller.DownloadAsync(_update.Version, progress); // continues on the UI thread
        }
        catch (Exception ex) when (ex is InvalidDataException or CryptographicException)
        {
            failure = Strings.UpdateBadSignature;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException or UnauthorizedAccessException)
        {
            failure = ex.Message;
        }
        _updateDownloading = false;
        // A failed automatic update keeps the normal label; it falls back to the notification.
        _updateFailed = failure != null && !quiet;
        ApplyTexts();

        if (failure != null)
        {
            if (!quiet)
                _notifier.Error(string.Format(Strings.UpdateFailed, failure));
            return false;
        }
        if (quiet)
        {
            ApproveUpdate();
        }
        else
        {
            ApplyTexts();
            // For when the menu was closed meanwhile or the download started from the notification.
            _notifier.Show(string.Format(Strings.UpdateReady, _update.Version.ToString(3)), ApproveUpdate, force: true);
        }
        return true;
    }

    /// <summary>Never interrupts work: with a recording, the overlay, settings, an upload or text recognition it waits until they finish.</summary>
    private void LaunchUpdateWhenIdle()
    {
        if (_updateInstaller is not { } installer || _update == null || !_updateApproved
            || _recording != null || _overlay != null || _settingsOpen || _actions.IsBusy)
            return;

        _updateInstaller = null;
        try
        {
            UpdateInstaller.Launch(installer, _update.Version);
            ExitThread();
        }
        catch (InvalidDataException)
        {
            ShowUpdateFailed(Strings.UpdateBadSignature);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
        {
            ShowUpdateFailed(ex.Message);
        }
    }

    private void ShowUpdateFailed(string reason)
    {
        _updateFailed = true;
        _updateApproved = false;
        ApplyTexts();
        _notifier.Error(string.Format(Strings.UpdateFailed, reason));
    }

    private void OpenUpdatePage() =>
        Process.Start(new ProcessStartInfo(_update?.Url ?? UpdateChecker.ReleasesUrl) { UseShellExecute = true });

    /// <summary>Menu texts in the current language; called at start and after a language change.</summary>
    private void ApplyTexts()
    {
        _captureItem.Text = Strings.MenuCapture;
        _recordItem.Text = _recording != null ? Strings.MenuStopRecording : Strings.MenuRecord;
        _saveFullItem.Text = Strings.MenuSaveFull;
        _copyFullItem.Text = Strings.MenuCopyFull;
        _openFolderItem.Text = Strings.MenuOpenFolder;
        _settingsItem.Text = Strings.MenuSettings;
        _aboutItem.Text = Strings.MenuAbout;
        _exitItem.Text = Strings.MenuExit;
        if (_update != null)
        {
            var label = UpdateInstaller.CanInstall ? Strings.MenuInstallUpdate : Strings.MenuUpdateAvailable;
            _updateItem.Text = true switch
            {
                _ when _updateDownloading && _updateProgress >= 0 => string.Format(Strings.UpdateDownloadingProgress, _updateProgress),
                _ when _updateDownloading => Strings.UpdateDownloading,
                _ when _updateInstaller != null && _updateApproved => Strings.UpdateInstalling,
                _ when _updateInstaller != null => string.Format(Strings.MenuUpdateRestart, _update.Version.ToString(3)),
                _ when _updateFailed => Strings.MenuUpdateRetry,
                _ => string.Format(label, _update.Version.ToString(3)),
            };
            // Red asks for attention; green while the approved update installs.
            _updateItem.ForeColor = _updateInstaller != null ? UpdateGreen : UpdateRed;
            _restartButton.Text = string.Format(Strings.MenuUpdateRestart, _update.Version.ToString(3));
        }

        // Derived from the state every time, so turning update checks off and on again restores the items.
        bool offered = _update != null && _settings.CheckForUpdates;
        bool ready = offered && _updateInstaller != null && !_updateApproved;
        _updateSeparator.Available = offered;
        _restartHost.Available = ready;
        _updateItem.Available = offered && !ready;
    }

    private void OnHotkey(int id)
    {
        if (id == RecordHotkeyId)
        {
            ToggleRecording();
            return;
        }
        if (_recording != null)
            return;
        switch (id)
        {
            case CaptureHotkeyId: StartCapture(); break;
            case SaveFullHotkeyId: SaveFullScreen(); break;
            case CopyFullHotkeyId: CopyFullScreen(); break;
        }
    }

    private void StartCapture() => OpenOverlay(recordMode: false);

    private void ToggleRecording()
    {
        if (_recording != null)
            _recording.Stop();
        else if (ScreenRecorder.IsSupported)
            OpenOverlay(recordMode: true);
    }

    private void OpenOverlay(bool recordMode)
    {
        if (_recording != null)
            return;
        if (_overlay is { IsDisposed: false })
        {
            _overlay.Activate();
            return;
        }

        using var capture = ScreenCapture.CaptureVirtualScreen(out var bounds);
        _overlay = new CaptureForm(capture, bounds, _settings, _actions, ScreenRecorder.IsSupported ? StartRecording : null, recordMode);
        _overlay.FormClosed += (_, _) =>
        {
            _overlay = null;
            _settings.Save();
            // Later: the overlay starts its recording or upload only after it has closed.
            Deferred(LaunchUpdateWhenIdle);
        };
        _overlay.Show();
    }

    private void StartRecording(Rectangle area)
    {
        var session = new RecordingSession(area, _settings, _actions);
        session.Finished += () =>
        {
            _recording = null;
            SetRecordingMenu();
            LaunchUpdateWhenIdle();
        };
        _recording = session;
        SetRecordingMenu();
        session.Start();
    }

    /// <summary>While recording, the menu offers only stopping: a screenshot would freeze the screen under the clip.</summary>
    private void SetRecordingMenu()
    {
        bool idle = _recording == null;
        _captureItem.Enabled = _saveFullItem.Enabled = _copyFullItem.Enabled = idle;
        ApplyTexts();
    }

    private void SaveFullScreen()
    {
        using var capture = ScreenCapture.CaptureVirtualScreen(out _);
        _actions.SaveToFolder(capture);
    }

    private void CopyFullScreen()
    {
        using var capture = ScreenCapture.CaptureVirtualScreen(out _);
        _actions.CopyToClipboard(capture);
    }

    private void OpenSaveFolder()
    {
        var folder = _settings.ResolvedSaveFolder;
        Directory.CreateDirectory(folder);
        Process.Start("explorer.exe", $"\"{folder}\"");
    }

    private void ShowSettings()
    {
        if (_settingsOpen)
            return;
        _settingsOpen = true;
        try
        {
            var before = _settings.Clone();
            var working = _settings.Clone();
            using var windowIcon = AppIcon.Load(SystemInformation.IconSize);
            using var form = new SettingsForm(working, windowIcon);
            // Only while a shortcut box records keys: the user can enter the same combinations again,
            // and the rest of the time the hotkeys work, e.g. to take a screenshot of the settings.
            form.HotkeyEditingChanged += editing =>
            {
                if (editing)
                    _hotkeys.UnregisterAll();
                else
                    RegisterHotkeys(quiet: true);
            };
            if (form.ShowDialog() == DialogResult.OK)
            {
                _settings.CopyChanges(before, working);
                _settings.Save();
                Autostart.Set(_settings.RunAtStartup);
                Localization.Apply(_settings.Language);
                ApplyTexts();
                if (_settings.CheckForUpdates && _update == null)
                    CheckForUpdates();
            }
        }
        finally
        {
            _settingsOpen = false;
            RegisterHotkeys();
            LaunchUpdateWhenIdle();
        }
    }

    private void ShowAbout()
    {
        if (_about is { IsDisposed: false })
        {
            _about.Activate();
            return;
        }
        var windowIcon = AppIcon.Load(SystemInformation.IconSize);
        _about = new AboutForm(windowIcon);
        _about.FormClosed += (_, _) =>
        {
            _about.Dispose();
            windowIcon.Dispose();
            _about = null;
        };
        _about.Show();
        _about.Activate();
    }

    /// <returns>True when every enabled hotkey was registered.</returns>
    /// <param name="quiet">No warnings; for re-registering after a shortcut box, which would repeat them on every focus change.</param>
    private bool RegisterHotkeys(bool quiet = false)
    {
        _hotkeys.UnregisterAll();
        var failed = new List<string>();

        void Register(int id, HotkeySetting hotkey, string name, ToolStripMenuItem item)
        {
            bool active = hotkey.Enabled && (hotkey.Keys & Keys.KeyCode) != Keys.None;
            item.ShortcutKeyDisplayString = active ? HotkeyText.Format(hotkey.Keys) : null;
            if (active && !_hotkeys.Register(id, hotkey.Keys))
                failed.Add($"{name} ({HotkeyText.Format(hotkey.Keys)})");
        }

        Register(CaptureHotkeyId, _settings.HotkeyCapture, Strings.HotkeyCapture, _captureItem);
        Register(SaveFullHotkeyId, _settings.HotkeySaveFull, Strings.HotkeySaveFull, _saveFullItem);
        Register(CopyFullHotkeyId, _settings.HotkeyCopyFull, Strings.HotkeyCopyFull, _copyFullItem);
        if (ScreenRecorder.IsSupported)
            Register(RecordHotkeyId, _settings.HotkeyRecord, Strings.HotkeyRecord, _recordItem);

        if (failed.Count > 0)
        {
            if (!quiet)
                _notifier.Error(string.Format(Strings.HotkeyFailed, string.Join(", ", failed)));
            return false;
        }

        bool usesPrintScreen = (_settings.HotkeyCapture.Keys & Keys.KeyCode) == Keys.PrintScreen;
        if (usesPrintScreen && Autostart.PrintScreenOpensSnippingTool())
        {
            if (!quiet)
                _notifier.Error(Strings.SnippingWarning);
            return false;
        }
        return true;
    }

    /// <summary>Runs the action after the tray menu has closed, so the menu isn't captured.</summary>
    private static void Deferred(Action action)
    {
        var timer = new System.Windows.Forms.Timer { Interval = 200 };
        timer.Tick += (_, _) =>
        {
            timer.Dispose();
            action();
        };
        timer.Start();
    }

    protected override void ExitThreadCore()
    {
        _overlay?.Close();
        _recording?.StopAndWait();
        _hotkeys.Dispose();
        _updateTimer.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _icon.Dispose();
        _settings.Save();
        base.ExitThreadCore();
    }
}
