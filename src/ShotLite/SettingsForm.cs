namespace ShotLite;

internal sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly TabControl _tabs = new() { Margin = new Padding(12, 12, 12, 0) };
    private readonly Bitmap _logo;
    private TableLayoutPanel _grid = null!;
    private int _row;

    private readonly CheckBox _captureEnabled = new();
    private readonly CheckBox _saveFullEnabled = new();
    private readonly CheckBox _copyFullEnabled = new();
    private readonly CheckBox _recordEnabled = new();
    private readonly HotkeyBox _captureKeys = new();
    private readonly HotkeyBox _saveFullKeys = new();
    private readonly HotkeyBox _copyFullKeys = new();
    private readonly HotkeyBox _recordKeys = new();
    private readonly TextBox _aspects = new() { Width = 300 };
    private readonly ComboBox _aspectDefault = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    private readonly ComboBox _format = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    private readonly NumericUpDown _quality = new() { Minimum = 10, Maximum = 100, Width = 70 };
    private readonly TextBox _folder = new() { Width = 300 };
    private readonly CheckBox _autoSave = new();
    private readonly ComboBox _recordFormat = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    private readonly TextBox _recordFolder = new() { Width = 300 };
    private readonly CheckBox _openAfterRecording = new();
    private readonly CheckBox _notifications = new();
    private readonly CheckBox _startup = new();
    private readonly CheckBox _checkUpdates = new();
    private readonly CheckBox _autoInstallUpdates = new();
    private readonly ComboBox _language = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly CheckBox _uploadEnabled = new();
    private readonly TextBox _uploadUrl = new() { Width = 340 };
    private readonly TextBox _uploadField = new() { Width = 150 };
    private readonly TextBox _uploadAuth = new() { Width = 340, UseSystemPasswordChar = true };
    private readonly TextBox _uploadJsonPath = new() { Width = 150 };

    /// <param name="settings">A working copy; it is modified only when the user confirms.</param>
    public SettingsForm(AppSettings settings, Icon icon)
    {
        _settings = settings;

        Text = Strings.SettingsTitle;
        Icon = icon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        // The 256 px frame scaled down stays sharp at any DPI.
        using (var logo = AppIcon.Load(new Size(256, 256)))
            _logo = logo.ToBitmap();

        BeginTab(Strings.SectionGeneral);
        // An empty code means "follow the Windows display language" (AppSettings.Language = null).
        var automatic = new LanguageInfo("", Strings.LanguageAuto);
        _language.Items.Add(automatic);
        _language.SelectedItem = automatic;
        foreach (var language in Localization.Available())
        {
            _language.Items.Add(language);
            if (language.Code.Equals(settings.Language, StringComparison.OrdinalIgnoreCase))
                _language.SelectedItem = language;
        }
        Row(Strings.Language, _language);
        Note(Strings.LanguageNote);
        Check(_notifications, Strings.Notifications, settings.ShowNotifications);
        Check(_startup, Strings.RunAtStartup, settings.RunAtStartup);
        Check(_checkUpdates, Strings.CheckForUpdates, settings.CheckForUpdates);
        if (UpdateInstaller.CanInstall)
            Check(_autoInstallUpdates, Strings.AutoInstallUpdates, settings.AutoInstallUpdates);

        // The aspect ratio shapes screenshots and recordings alike, so it sits with neither of them.
        Header(Strings.SectionSelection);
        _aspects.Text = string.Join(", ", settings.AspectRatios);
        Row(Strings.AspectRatios, _aspects);
        Note(Strings.AspectNote);
        // Filled from the saved list: a newly typed ratio can be chosen after saving.
        _aspectDefault.Items.Add(Strings.AspectFree);
        _aspectDefault.Items.AddRange(settings.AspectRatios.ToArray<object>());
        _aspectDefault.SelectedIndex = Math.Max(0, _aspectDefault.Items.IndexOf(settings.DefaultAspectRatio ?? ""));
        Row(Strings.AspectDefault, _aspectDefault);

        BeginTab(Strings.SectionHotkeys);
        HotkeyRow(_captureEnabled, _captureKeys, Strings.HotkeyCapture, settings.HotkeyCapture);
        HotkeyRow(_saveFullEnabled, _saveFullKeys, Strings.HotkeySaveFull, settings.HotkeySaveFull);
        HotkeyRow(_copyFullEnabled, _copyFullKeys, Strings.HotkeyCopyFull, settings.HotkeyCopyFull);
        if (ScreenRecorder.IsSupported)
            HotkeyRow(_recordEnabled, _recordKeys, Strings.HotkeyRecord, settings.HotkeyRecord);
        Note(Strings.HotkeyNote);

        BeginTab(Strings.TabScreenshots);
        _format.Items.AddRange(new object[] { "PNG", "JPEG" });
        _format.SelectedIndex = settings.FileFormat == "jpg" ? 1 : 0;
        Row(Strings.Format, _format);
        _quality.Value = Math.Clamp(settings.JpegQuality, 10, 100);
        Row(Strings.JpegQuality, _quality);
        // Show the real path; leaving it unchanged (or empty) keeps the portable default.
        _folder.Text = settings.ResolvedSaveFolder;
        Row(Strings.Folder, FolderPicker(_folder));
        Check(_autoSave, Strings.AutoSave, settings.AutoSave);

        if (ScreenRecorder.IsSupported)
        {
            BeginTab(Strings.TabVideo);
            _recordFormat.Items.AddRange(new object[] { "MP4", "GIF" });
            _recordFormat.SelectedIndex = settings.RecordFormat == RecordFormat.Gif ? 1 : 0;
            Row(Strings.Format, _recordFormat);
            _recordFolder.Text = settings.ResolvedRecordFolder;
            Row(Strings.Folder, FolderPicker(_recordFolder));
            Check(_openAfterRecording, Strings.OpenFolderAfterRecording, settings.OpenFolderAfterRecording);
        }

        BeginTab(Strings.TabServer);
        Header(Strings.SectionUpload, first: true);
        Check(_uploadEnabled, Strings.UploadEnabled, settings.Upload.Enabled);
        _uploadUrl.Text = settings.Upload.Url;
        Row(Strings.UploadUrl, _uploadUrl);
        _uploadField.Text = settings.Upload.FileField;
        Row(Strings.UploadField, _uploadField);
        _uploadAuth.Text = settings.Upload.AuthorizationHeader;
        Row(Strings.UploadAuth, _uploadAuth);
        _uploadJsonPath.Text = settings.Upload.ResponseJsonPath;
        Row(Strings.UploadJsonPath, _uploadJsonPath);
        Note(Strings.UploadNote);

        var ok = new Button { Text = "OK", AutoSize = true, MinimumSize = new Size(80, 0) };
        ok.Click += (_, _) => Apply();
        var cancel = new Button { Text = Strings.Cancel, AutoSize = true, MinimumSize = new Size(80, 0), DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(12, 10, 12, 12),
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        var header = new BrandHeader(_logo, string.Format(Strings.AboutVersion, AboutForm.VersionText()))
        {
            Height = 68,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
        };
        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
        };
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(_tabs, 0, 1);
        layout.Controls.Add(buttons, 0, 2);

        AcceptButton = ok;
        CancelButton = cancel;
        Controls.Add(layout);
    }

    /// <summary>A TabControl doesn't size itself, so it takes the size of the largest page once scaling is done.</summary>
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var content = Size.Empty;
        foreach (TabPage page in _tabs.TabPages)
        {
            var grid = page.Controls[0];
            var size = grid.GetPreferredSize(Size.Empty);
            content = new Size(Math.Max(content.Width, grid.Left + size.Width), Math.Max(content.Height, grid.Top + size.Height));
        }
        var frame = _tabs.Size - _tabs.DisplayRectangle.Size;
        _tabs.Size = content + frame + new Size(_tabs.TabPages[0].Padding.Right, _tabs.TabPages[0].Padding.Bottom);
    }

    private void Apply()
    {
        var upload = new UploadSettings
        {
            Enabled = _uploadEnabled.Checked,
            Url = _uploadUrl.Text.Trim(),
            FileField = string.IsNullOrWhiteSpace(_uploadField.Text) ? "file" : _uploadField.Text.Trim(),
            AuthorizationHeader = _uploadAuth.Text.Trim(),
            ResponseJsonPath = _uploadJsonPath.Text.Trim(),
        };
        if (upload.Enabled && !upload.IsConfigured)
        {
            ShowTabOf(_uploadUrl);
            MessageBox.Show(this, Strings.UrlInvalid, Strings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!AspectRatio.TryParseList(_aspects.Text, out var aspects, out var invalid))
        {
            ShowTabOf(_aspects);
            MessageBox.Show(this, string.Format(Strings.AspectInvalid, invalid), Strings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _aspects.Focus();
            return;
        }

        _settings.AspectRatios = aspects.Select(ratio => ratio.ToString()).ToList();
        // A default that was removed from the list goes back to a free selection.
        var aspectDefault = _aspectDefault.SelectedIndex > 0 ? (string)_aspectDefault.SelectedItem! : null;
        _settings.DefaultAspectRatio = _settings.AspectRatios.Contains(aspectDefault ?? "") ? aspectDefault : null;
        _settings.HotkeyCapture = new HotkeySetting(_captureKeys.Hotkey) { Enabled = _captureEnabled.Checked };
        _settings.HotkeySaveFull = new HotkeySetting(_saveFullKeys.Hotkey) { Enabled = _saveFullEnabled.Checked };
        _settings.HotkeyCopyFull = new HotkeySetting(_copyFullKeys.Hotkey) { Enabled = _copyFullEnabled.Checked };
        if (ScreenRecorder.IsSupported)
            _settings.HotkeyRecord = new HotkeySetting(_recordKeys.Hotkey) { Enabled = _recordEnabled.Checked };
        _settings.FileFormat = _format.SelectedIndex == 1 ? "jpg" : "png";
        _settings.JpegQuality = (int)_quality.Value;
        _settings.SaveFolder = AppSettings.NormalizeSaveFolder(_folder.Text);
        _settings.AutoSave = _autoSave.Checked;
        if (ScreenRecorder.IsSupported)
        {
            _settings.RecordFormat = _recordFormat.SelectedIndex == 1 ? RecordFormat.Gif : RecordFormat.Mp4;
            _settings.RecordFolder = AppSettings.NormalizeRecordFolder(_recordFolder.Text);
            _settings.OpenFolderAfterRecording = _openAfterRecording.Checked;
        }
        _settings.ShowNotifications = _notifications.Checked;
        _settings.RunAtStartup = _startup.Checked;
        _settings.CheckForUpdates = _checkUpdates.Checked;
        if (UpdateInstaller.CanInstall)
            _settings.AutoInstallUpdates = _autoInstallUpdates.Checked;
        if (_language.SelectedItem is LanguageInfo language)
            _settings.Language = language.Code.Length == 0 ? null : language.Code;
        _settings.Upload = upload;

        DialogResult = DialogResult.OK;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _logo.Dispose();
        base.Dispose(disposing);
    }

    private void BrowseFolder(TextBox folder)
    {
        using var dialog = new FolderBrowserDialog { SelectedPath = Environment.ExpandEnvironmentVariables(folder.Text), UseDescriptionForTitle = true, Description = Strings.Folder };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            folder.Text = dialog.SelectedPath;
    }

    private FlowLayoutPanel FolderPicker(TextBox folder)
    {
        var browse = new Button { Text = Strings.Browse, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        browse.Click += (_, _) => BrowseFolder(folder);
        return Flow(folder, browse);
    }

    // ---------------------------------------------------------------- layout helpers

    private void BeginTab(string title)
    {
        var page = new TabPage(title) { Padding = new Padding(12), UseVisualStyleBackColor = true };
        _grid = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Location = new Point(12, 12),
        };
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        page.Controls.Add(_grid);
        _tabs.TabPages.Add(page);
        _row = 0;
    }

    /// <summary>A rejected value may be on another tab than the one in view.</summary>
    private void ShowTabOf(Control control)
    {
        for (var parent = control.Parent; parent != null; parent = parent.Parent)
        {
            if (parent is TabPage page)
                _tabs.SelectedTab = page;
        }
    }

    private void Header(string text, bool first = false)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, first ? 0 : 14, 0, 4),
        };
        Span(label);
    }

    private void Note(string text)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(480, 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(3, 2, 3, 2),
        };
        Span(label);
    }

    private void HotkeyRow(CheckBox enabled, HotkeyBox box, string text, HotkeySetting setting)
    {
        enabled.Text = text;
        enabled.AutoSize = true;
        enabled.Checked = setting.Enabled;
        enabled.Anchor = AnchorStyles.Left;
        box.Hotkey = setting.Keys;
        _grid.Controls.Add(enabled, 0, _row);
        _grid.Controls.Add(box, 1, _row);
        _row++;
    }

    private void Row(string text, Control control)
    {
        var label = new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 12, 3) };
        _grid.Controls.Add(label, 0, _row);
        _grid.Controls.Add(control, 1, _row);
        _row++;
    }

    private void Check(CheckBox box, string text, bool value)
    {
        box.Text = text;
        box.AutoSize = true;
        box.Checked = value;
        Span(box);
    }

    private void Span(Control control)
    {
        _grid.Controls.Add(control, 0, _row);
        _grid.SetColumnSpan(control, 2);
        _row++;
    }

    private static FlowLayoutPanel Flow(params Control[] controls)
    {
        var panel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        panel.Controls.AddRange(controls);
        return panel;
    }
}
