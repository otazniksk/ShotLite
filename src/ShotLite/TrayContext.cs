using System.Diagnostics;

namespace ShotLite;

/// <summary>Application root: tray icon, menu, global hotkeys. There is no main window.</summary>
internal sealed class TrayContext : ApplicationContext
{
    private const int CaptureHotkeyId = 1;
    private const int SaveFullHotkeyId = 2;
    private const int CopyFullHotkeyId = 3;

    private readonly AppSettings _settings;
    private readonly Icon _icon;
    private readonly NotifyIcon _tray;
    private readonly Notifier _notifier;
    private readonly ImageActions _actions;
    private readonly HotkeyManager _hotkeys = new();
    private readonly ToolStripMenuItem _captureItem;
    private readonly ToolStripMenuItem _saveFullItem;
    private readonly ToolStripMenuItem _copyFullItem;
    private readonly ToolStripMenuItem _openFolderItem;
    private readonly ToolStripMenuItem _settingsItem;
    private readonly ToolStripMenuItem _aboutItem;
    private readonly ToolStripMenuItem _exitItem;
    private readonly ToolStripMenuItem _updateItem;
    private readonly ToolStripSeparator _updateSeparator = new() { Visible = false };
    private readonly System.Windows.Forms.Timer _updateTimer = new();
    private UpdateInfo? _update;
    private CaptureForm? _overlay;
    private bool _settingsOpen;
    private AboutForm? _about;

    public TrayContext()
    {
        _settings = AppSettings.Load();
        Localization.Apply(_settings.Language);
        _icon = AppIcon.LoadSmall();

        _captureItem = new ToolStripMenuItem(null, null, (_, _) => Deferred(StartCapture));
        _captureItem.Font = new Font(_captureItem.Font, FontStyle.Bold);
        _saveFullItem = new ToolStripMenuItem(null, null, (_, _) => Deferred(SaveFullScreen));
        _copyFullItem = new ToolStripMenuItem(null, null, (_, _) => Deferred(CopyFullScreen));
        _openFolderItem = new ToolStripMenuItem(null, null, (_, _) => OpenSaveFolder());
        _settingsItem = new ToolStripMenuItem(null, null, (_, _) => ShowSettings());
        _aboutItem = new ToolStripMenuItem(null, null, (_, _) => ShowAbout());
        _exitItem = new ToolStripMenuItem(null, null, (_, _) => ExitThread());
        _updateItem = new ToolStripMenuItem(null, null, (_, _) => OpenUpdatePage())
        {
            Visible = false,
            ForeColor = Color.FromArgb(220, 38, 38),
        };
        _updateItem.Font = new Font(_updateItem.Font, FontStyle.Bold);
        ApplyTexts();

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(new ToolStripItem[]
        {
            _updateItem,
            _updateSeparator,
            _captureItem,
            _saveFullItem,
            _copyFullItem,
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
            if (e.Button == MouseButtons.Left)
                Deferred(StartCapture);
        };

        _notifier = new Notifier(_tray, () => _settings.ShowNotifications);
        _actions = new ImageActions(_settings, _notifier);
        _hotkeys.Pressed += OnHotkey;

        // Follow the registry (the installer may have enabled autostart) and refresh the path in case the exe moved.
        _settings.RunAtStartup = Autostart.IsEnabled();
        if (_settings.RunAtStartup)
            Autostart.Set(true);

        // First check shortly after start (not to slow down login), then once a day.
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
        if (!_settings.CheckForUpdates)
            return;
        var update = await UpdateChecker.CheckAsync(); // continues on the UI thread
        if (update == null || !_settings.CheckForUpdates)
            return;

        _update = update;
        _updateItem.Visible = _updateSeparator.Visible = true;
        ApplyTexts();

        var version = update.Version.ToString(3);
        if (_settings.LastNotifiedUpdate != version)
        {
            _settings.LastNotifiedUpdate = version;
            _settings.Save();
            _notifier.Show(string.Format(Strings.UpdateNotification, version), OpenUpdatePage);
        }
    }

    private void OpenUpdatePage() =>
        Process.Start(new ProcessStartInfo(_update?.Url ?? UpdateChecker.ReleasesUrl) { UseShellExecute = true });

    /// <summary>Menu texts in the current language; called at start and after a language change.</summary>
    private void ApplyTexts()
    {
        _captureItem.Text = Strings.MenuCapture;
        _saveFullItem.Text = Strings.MenuSaveFull;
        _copyFullItem.Text = Strings.MenuCopyFull;
        _openFolderItem.Text = Strings.MenuOpenFolder;
        _settingsItem.Text = Strings.MenuSettings;
        _aboutItem.Text = Strings.MenuAbout;
        _exitItem.Text = Strings.MenuExit;
        if (_update != null)
            _updateItem.Text = string.Format(Strings.MenuUpdateAvailable, _update.Version.ToString(3));
    }

    private void OnHotkey(int id)
    {
        if (_settingsOpen)
            return;
        switch (id)
        {
            case CaptureHotkeyId: StartCapture(); break;
            case SaveFullHotkeyId: SaveFullScreen(); break;
            case CopyFullHotkeyId: CopyFullScreen(); break;
        }
    }

    private void StartCapture()
    {
        if (_overlay is { IsDisposed: false })
        {
            _overlay.Activate();
            return;
        }

        using var capture = ScreenCapture.CaptureVirtualScreen(out var bounds);
        _overlay = new CaptureForm(capture, bounds, _settings, _actions);
        _overlay.FormClosed += (_, _) =>
        {
            _overlay = null;
            _settings.Save();
        };
        _overlay.Show();
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
        // Release the hotkeys so the user can record the same combinations again.
        _hotkeys.UnregisterAll();
        try
        {
            var working = _settings.Clone();
            using var windowIcon = AppIcon.Load(SystemInformation.IconSize);
            using var form = new SettingsForm(working, windowIcon);
            if (form.ShowDialog() == DialogResult.OK)
            {
                _settings.CopyFrom(working);
                _settings.Save();
                Autostart.Set(_settings.RunAtStartup);
                Localization.Apply(_settings.Language);
                ApplyTexts();
                if (!_settings.CheckForUpdates)
                    _updateItem.Visible = _updateSeparator.Visible = false;
                else if (_update == null)
                    CheckForUpdates();
            }
        }
        finally
        {
            _settingsOpen = false;
            RegisterHotkeys();
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
    private bool RegisterHotkeys()
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

        if (failed.Count > 0)
        {
            _notifier.Error(string.Format(Strings.HotkeyFailed, string.Join(", ", failed)));
            return false;
        }

        bool usesPrintScreen = (_settings.HotkeyCapture.Keys & Keys.KeyCode) == Keys.PrintScreen;
        if (usesPrintScreen && Autostart.PrintScreenOpensSnippingTool())
        {
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
        _hotkeys.Dispose();
        _updateTimer.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _icon.Dispose();
        _settings.Save();
        base.ExitThreadCore();
    }
}
