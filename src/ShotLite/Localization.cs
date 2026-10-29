using System.Globalization;
using System.Text.Json;

namespace ShotLite;

internal sealed record LanguageInfo(string Code, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// UI texts from languages\&lt;code&gt;.json next to the exe (code = ISO culture name, e.g. sk-SK).
/// Every *.json there is offered as a language; English is also embedded as the fallback,
/// so a missing folder or a missing key never breaks the UI.
/// </summary>
internal static class Localization
{
    public const string FallbackCode = "en-US";
    private const string NameKey = "_language";

    private static readonly Dictionary<string, string> Fallback = LoadEmbedded();
    private static Dictionary<string, string> _current = Fallback;

    public static string CurrentCode { get; private set; } = FallbackCode;

    private static string LanguagesDirectory => Path.Combine(AppContext.BaseDirectory, "languages");

    public static string Get(string key) =>
        _current.TryGetValue(key, out var text) || Fallback.TryGetValue(key, out text) ? text : key;

    public static IReadOnlyList<LanguageInfo> Available()
    {
        var languages = new Dictionary<string, LanguageInfo>(StringComparer.OrdinalIgnoreCase)
        {
            [FallbackCode] = new(FallbackCode, Fallback.GetValueOrDefault(NameKey, "English")),
        };
        if (Directory.Exists(LanguagesDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(LanguagesDirectory, "*.json"))
            {
                var code = Path.GetFileNameWithoutExtension(file);
                if (TryRead(file) is { } texts)
                    languages[code] = new(code, texts.GetValueOrDefault(NameKey, code));
            }
        }
        return languages.Values.OrderBy(l => l.Name, StringComparer.CurrentCulture).ToList();
    }

    /// <param name="code">The saved choice; null means the Windows display language.</param>
    public static void Apply(string? code)
    {
        var wanted = string.IsNullOrWhiteSpace(code) ? CultureInfo.CurrentUICulture.Name : code;
        var available = Available();
        // Exact culture first (de-AT), then the same language (de-DE for de-AT), then English.
        var match = available.FirstOrDefault(l => l.Code.Equals(wanted, StringComparison.OrdinalIgnoreCase))
                    ?? available.FirstOrDefault(l => Neutral(l.Code).Equals(Neutral(wanted), StringComparison.OrdinalIgnoreCase))
                    ?? available.First(l => l.Code == FallbackCode);

        CurrentCode = match.Code;
        _current = TryRead(Path.Combine(LanguagesDirectory, match.Code + ".json")) ?? Fallback;
    }

    private static string Neutral(string code) => code.Split('-', '_')[0];

    private static Dictionary<string, string>? TryRead(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A broken translation file must not stop the app; it falls back to English.
            return null;
        }
    }

    private static Dictionary<string, string> LoadEmbedded()
    {
        using var stream = typeof(Localization).Assembly.GetManifestResourceStream("ShotLite.languages.en-US.json")
            ?? throw new InvalidOperationException("Embedded resource ShotLite.languages.en-US.json is missing.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new();
    }
}
