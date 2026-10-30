using System.Reflection;

namespace ShotLite;

/// <summary>Values fixed at build time from ShotLite.csproj; compiled in, so nothing on the user's PC can replace them.</summary>
internal static class BuildInfo
{
    /// <summary>The GitHub owner/name that releases come from.</summary>
    public static string Repository => Metadata("UpdateRepository");

    public static string RepositoryUrl => $"https://github.com/{Repository}";

    /// <summary>Verifies update signatures; empty when this build has no signing key, which turns installing updates off.</summary>
    public static string UpdatePublicKey => Metadata("UpdatePublicKey");

    private static string Metadata(string key) =>
        typeof(BuildInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value ?? "";
}
