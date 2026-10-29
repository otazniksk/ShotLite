namespace ShotLite;

internal sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly TableLayoutPanel _grid;
    private int _row;

    private readonly CheckBox _captureEnabled = new();
    private readonly CheckBox _saveFullEnabled = new();
    private readonly CheckBox _copyFullEnabled = new();
    private readonly HotkeyBox _captureKeys = new();
    private readonly HotkeyBox _saveFullKeys = new();
    private readonly HotkeyBox _copyFullKeys = new();
    private readonly ComboBox _format = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    private readonly NumericUpDown _quality = new() { Minimum = 10, Maximum = 100, Width = 70 };
    private readonly TextBox _folder = new() { Width = 300 };
    private readonly CheckBox _autoSave = new();
    private readonly CheckBox _notifications = new();
    private readonly CheckBox _startup = new();
    private readonly CheckBox _checkUpdates = new();
    private readonly ComboBox _language = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
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
        Padding = new Padding(12);

        _grid = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
        };
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        Header(Strings.SectionHotkeys, first: true);
        HotkeyRow(_captureEnabled, _captureKeys, Strings.HotkeyCapture, settings.HotkeyCapture);
        HotkeyRow(_saveFullEnabled, _saveFullKeys, Strings.HotkeySaveFull, settings.HotkeySaveFull);
        HotkeyRow(_copyFullEnabled, _copyFullKeys, Strings.HotkeyCopyFull, settings.HotkeyCopyFull);
        Note(Strings.HotkeyNote);

        Header(Strings.SectionSaving);
        _format.Items.AddRange(new object[] { "PNG", "JPEG" });
        _format.SelectedIndex = settings.FileFormat == "jpg" ? 1 : 0;
        Row(Strings.Format, _format);
        _quality.Value = Math.Clamp(settings.JpegQuality, 10, 100);
        Row(Strings.JpegQuality, _quality);
        // Show the real path; leaving it unchanged (or empty) keeps the portable default.
        _folder.Text = settings.ResolvedSaveFolder;
        var browse = new Button { Text = Strings.Browse, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        browse.Click += (_, _) => BrowseFolder();
        Row(Strings.Folder, Flow(_folder, browse));
        Check(_autoSave, Strings.AutoSave, settings.AutoSave);

        Header(Strings.SectionGeneral);
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

        Header(Strings.SectionUpload);
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
            Margin = new Padding(0, 14, 0, 0),
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        Span(buttons);

        AcceptButton = ok;
        CancelButton = cancel;
        Controls.Add(_grid);
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
            MessageBox.Show(this, Strings.UrlInvalid, Strings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _settings.HotkeyCapture = new HotkeySetting(_captureKeys.Hotkey) { Enabled = _captureEnabled.Checked };
        _settings.HotkeySaveFull = new HotkeySetting(_saveFullKeys.Hotkey) { Enabled = _saveFullEnabled.Checked };
        _settings.HotkeyCopyFull = new HotkeySetting(_copyFullKeys.Hotkey) { Enabled = _copyFullEnabled.Checked };
        _settings.FileFormat = _format.SelectedIndex == 1 ? "jpg" : "png";
        _settings.JpegQuality = (int)_quality.Value;
        _settings.SaveFolder = AppSettings.NormalizeSaveFolder(_folder.Text);
        _settings.AutoSave = _autoSave.Checked;
        _settings.ShowNotifications = _notifications.Checked;
        _settings.RunAtStartup = _startup.Checked;
        _settings.CheckForUpdates = _checkUpdates.Checked;
        if (_language.SelectedItem is LanguageInfo language)
            _settings.Language = language.Code.Length == 0 ? null : language.Code;
        _settings.Upload = upload;

        DialogResult = DialogResult.OK;
    }

    private void BrowseFolder()
    {
        using var dialog = new FolderBrowserDialog { SelectedPath = Environment.ExpandEnvironmentVariables(_folder.Text), UseDescriptionForTitle = true, Description = Strings.Folder };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _folder.Text = dialog.SelectedPath;
    }

    // ---------------------------------------------------------------- layout helpers

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
