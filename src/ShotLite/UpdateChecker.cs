using System.Text.Json;

namespace ShotLite;

internal sealed record UpdateInfo(Version Version, string Url);

/// <summary>
/// Asks the public GitHub API for the latest release (no token, no data sent besides the request itself).
/// Drafts and pre-releases are never "latest", so only real releases are offered.
/// </summary>
internal static class UpdateChecker
{
    public static string ReleasesUrl => $"{BuildInfo.RepositoryUrl}/releases/latest";
    private static string LatestReleaseApi => $"https://api.github.com/repos/{BuildInfo.Repository}/releases/latest";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>Major.Minor.Patch of this build; branch builds carry the base version from ShotLite.csproj.</summary>
    public static Version CurrentVersion
    {
        get
        {
            var version = typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
        }
    }

    /// <returns>The newer release, or null when up to date, offline or GitHub is unavailable.</returns>
    public static async Task<UpdateInfo?> CheckAsync()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
            request.Headers.UserAgent.ParseAdd($"ShotLite/{CurrentVersion}"); // required by the GitHub API
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var response = await Http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return null; // e.g. 404 before the first release, 403 when rate limited

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = json.RootElement;
            var tag = root.GetProperty("tag_name").GetString();
            if (tag is null || !Version.TryParse(tag.TrimStart('v', 'V'), out var latest))
                return null;

            var url = root.TryGetProperty("html_url", out var htmlUrl) ? htmlUrl.GetString() : null;
            return latest > CurrentVersion ? new UpdateInfo(latest, url ?? ReleasesUrl) : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
                                       or KeyNotFoundException or InvalidOperationException)
        {
            // Update checks are best effort and must never bother the user.
            return null;
        }
    }
}
