using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShotLite;

public sealed class HotkeySetting
{
    public HotkeySetting()
    {
    }

    public HotkeySetting(Keys keys) => Keys = keys;

    public Keys Keys { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed class UploadSettings
{
    public bool Enabled { get; set; }
    public string Url { get; set; } = "";
    public string FileField { get; set; } = "file";
    public string ResponseJsonPath { get; set; } = "";

    /// <summary>Plain value in memory only; on disk it is <see cref="AuthorizationHeaderProtected"/>.</summary>
    [JsonIgnore]
    public string AuthorizationHeader { get; set; } = "";

    /// <summary>The header encrypted with Windows DPAPI for the current user (base64), so other accounts can't read it.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AuthorizationHeaderProtected
    {
        get => SecretProtector.Protect(AuthorizationHeader);
        set => AuthorizationHeader = SecretProtector.Unprotect(value);
    }

    /// <summary>Plain-text value written by versions before 1.0.0: read once, never written again.</summary>
    [JsonPropertyName("AuthorizationHeader")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyAuthorizationHeader
    {
        get => null;
        set
        {
            if (!string.IsNullOrEmpty(value))
                AuthorizationHeader = value;
        }
    }

    [JsonIgnore]
    public bool IsConfigured =>
        Enabled
        && Uri.TryCreate(Url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}

/// <summary>Settings persisted to %APPDATA%\ShotLite\settings.json (replaces Lightshot's registry keys).</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string SettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Strings.AppName);

    private static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public HotkeySetting HotkeyCapture { get; set; } = new(Keys.PrintScreen);
    public HotkeySetting HotkeySaveFull { get; set; } = new(Keys.Shift | Keys.PrintScreen);
    public HotkeySetting HotkeyCopyFull { get; set; } = new(Keys.Control | Keys.PrintScreen);
    public HotkeySetting HotkeyRecord { get; set; } = new(Keys.Control | Keys.Shift | Keys.PrintScreen);

    /// <summary>"png" or "jpg".</summary>
    public string FileFormat { get; set; } = "png";
    public int JpegQuality { get; set; } = 90;
    /// <summary>Empty = <see cref="DefaultSaveFolder"/>, resolved at run time; may contain %VARIABLES%.</summary>
    public string SaveFolder { get; set; } = "";

    /// <summary>The user's Pictures\ShotLite; never stored, so it follows a moved or redirected Pictures folder.</summary>
    public static string DefaultSaveFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), Strings.AppName);

    [JsonIgnore]
    public string ResolvedSaveFolder =>
        string.IsNullOrWhiteSpace(SaveFolder) ? DefaultSaveFolder : Environment.ExpandEnvironmentVariables(SaveFolder);
    public bool AutoSave { get; set; }

    /// <summary>Empty = <see cref="DefaultRecordFolder"/>, resolved at run time; may contain %VARIABLES%.</summary>
    public string RecordFolder { get; set; } = "";

    /// <summary>The user's Videos\ShotLite; never stored, like <see cref="DefaultSaveFolder"/>.</summary>
    public static string DefaultRecordFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), Strings.AppName);

    [JsonIgnore]
    public string ResolvedRecordFolder =>
        string.IsNullOrWhiteSpace(RecordFolder) ? DefaultRecordFolder : Environment.ExpandEnvironmentVariables(RecordFolder);
    public bool OpenFolderAfterRecording { get; set; } = true;
    public string? LastSaveDir { get; set; }

    public bool ShowNotifications { get; set; } = true;
    public bool RunAtStartup { get; set; }
    public bool FirstRunDone { get; set; }

    /// <summary>Once a day ask GitHub for a newer release (the only network access besides the optional upload).</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>Installs a new release without asking; off by default, so nothing changes behind the user's back.</summary>
    public bool AutoInstallUpdates { get; set; }

    /// <summary>The newest version the user was already notified about, so the balloon appears once per release.</summary>
    public string? LastNotifiedUpdate { get; set; }

    /// <summary>ISO culture name of a file in languages\ (e.g. sk-SK); null = Windows display language.</summary>
    public string? Language { get; set; }

    public int ColorArgb { get; set; } = Color.FromArgb(255, 229, 57, 53).ToArgb();
    public float PenWidth { get; set; } = 3;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RedactStyle RedactStyle { get; set; } = RedactStyle.Black;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RecordFormat RecordFormat { get; set; } = RecordFormat.Mp4;
    public bool RecordCursor { get; set; } = true;

    /// <summary>Choices of the aspect ratio button, as text like "9:16"; checked by <see cref="SanitizeAspectRatios"/>.</summary>
    public List<string> AspectRatios { get; set; } = new(AspectRatio.Defaults);

    /// <summary>The ratio a new selection starts with; null = free selection.</summary>
    public string? DefaultAspectRatio { get; set; }

    internal List<AspectRatio> ParsedAspectRatios =>
        AspectRatios.Select(text => AspectRatio.TryParse(text, out var ratio) ? ratio : (AspectRatio?)null)
            .OfType<AspectRatio>().ToList();

    public UploadSettings Upload { get; set; } = new();

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions) ?? new();
                settings.SaveFolder = NormalizeSaveFolder(settings.SaveFolder);
                settings.RecordFolder = NormalizeRecordFolder(settings.RecordFolder);
                settings.SanitizeAspectRatios();
                return settings;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Corrupt or unreadable settings fall back to defaults.
        }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings are best effort; the app keeps working with in-memory values.
        }
    }

    /// <summary>The file can be edited by hand: drops what the settings dialog wouldn't have accepted.</summary>
    private void SanitizeAspectRatios()
    {
        AspectRatios ??= new();
        var valid = new List<AspectRatio>();
        foreach (var ratio in ParsedAspectRatios)
        {
            if (valid.Count < AspectRatio.MaxCount && !valid.Exists(ratio.IsSameShape))
                valid.Add(ratio);
        }
        AspectRatios = valid.Count > 0 ? valid.ConvertAll(ratio => ratio.ToString()) : new(AspectRatio.Defaults);
        if (!AspectRatios.Contains(DefaultAspectRatio ?? ""))
            DefaultAspectRatio = null;
    }

    /// <summary>Stores the default folder as empty, so older settings with an absolute default path migrate.</summary>
    public static string NormalizeSaveFolder(string? folder) => NormalizeFolder(folder, DefaultSaveFolder);

    public static string NormalizeRecordFolder(string? folder) => NormalizeFolder(folder, DefaultRecordFolder);

    private static string NormalizeFolder(string? folder, string defaultFolder)
    {
        folder = folder?.Trim() ?? "";
        return string.Equals(Path.TrimEndingDirectorySeparator(folder), defaultFolder, StringComparison.OrdinalIgnoreCase)
            ? ""
            : folder;
    }

    public AppSettings Clone() =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;

    public void CopyFrom(AppSettings other)
    {
        foreach (var property in typeof(AppSettings).GetProperties())
        {
            if (property.CanRead && property.CanWrite)
                property.SetValue(this, property.GetValue(other));
        }
    }
}
