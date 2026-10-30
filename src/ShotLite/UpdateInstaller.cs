using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace ShotLite;

/// <summary>
/// Downloads the installer of a new release and runs it silently. It only ever runs a file signed with the
/// project's own key: the signature comes with the release and is checked against the public key compiled in, so
/// replacing the files on GitHub isn't enough to reach users. The signature also covers the version number,
/// which stops an older signed installer from being passed off as the newest one.
/// </summary>
internal static class UpdateInstaller
{
    private const string InstallerName = "ShotLite-install.exe";
    private const string SignatureName = InstallerName + ".sig";
    private const string InstallArguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /relaunch=1";
    private const long MaxInstallerBytes = 200 * 1024 * 1024;
    private const long MaxSignatureBytes = 1024;
    /// <summary>How long a download may stand still; HttpClient.Timeout ends once the headers are in.</summary>
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Written by Inno Setup from the AppId in installer\ShotLite.iss.</summary>
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{AF23D553-9105-488B-AC09-5588769C2D56}_is1";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    private static string DownloadFolder => Path.Combine(Path.GetTempPath(), "ShotLite-update");

    /// <summary>
    /// True for the installed copy of a build with a signing key. A portable copy can't be updated by the
    /// installer, and without a key nothing could be verified, so those offer a download link instead.
    /// </summary>
    public static bool CanInstall
    {
        get
        {
            if (BuildInfo.UpdatePublicKey.Length == 0)
                return false;
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
            return key?.GetValue("InstallLocation") is string location
                && Path.GetDirectoryName(Environment.ProcessPath) is { } folder
                && string.Equals(Path.TrimEndingDirectorySeparator(location), folder, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <returns>The path of the verified installer.</returns>
    /// <exception cref="InvalidDataException">The signature doesn't match; the file is already deleted.</exception>
    public static async Task<string> DownloadAsync(Version version)
    {
        var number = version.ToString(3);
        DeleteDownloads();
        Directory.CreateDirectory(DownloadFolder);

        var installer = Path.Combine(DownloadFolder, InstallerName);
        var signature = Path.Combine(DownloadFolder, SignatureName);
        await DownloadFileAsync($"{BuildInfo.RepositoryUrl}/releases/download/v{number}/{InstallerName}", installer, MaxInstallerBytes);
        await DownloadFileAsync($"{BuildInfo.RepositoryUrl}/releases/download/v{number}/{SignatureName}", signature, MaxSignatureBytes);

        await using (var file = File.OpenRead(installer))
        {
            if (IsSigned(file, await File.ReadAllTextAsync(signature), number, BuildInfo.UpdatePublicKey))
                return installer;
        }
        DeleteDownloads();
        throw new InvalidDataException();
    }

    /// <summary>Starts the setup, which waits for this process to end and starts the new version when done.</summary>
    /// <exception cref="InvalidDataException">The file changed since the download; it is deleted and not run.</exception>
    /// <exception cref="Win32Exception">The setup couldn't be started.</exception>
    public static void Launch(string installer, Version version)
    {
        // The update may have waited long for an idle moment, so verify again, under a lock without
        // write sharing: the file that was checked is then the file that runs.
        using var locked = new FileStream(installer, FileMode.Open, FileAccess.Read, FileShare.Read);
        var signature = File.ReadAllText(Path.ChangeExtension(installer, ".exe.sig"));
        if (!IsSigned(locked, signature, version.ToString(3), BuildInfo.UpdatePublicKey))
        {
            locked.Dispose();
            DeleteDownloads();
            throw new InvalidDataException();
        }

        Process.Start(new ProcessStartInfo(installer, InstallArguments)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(installer),
        })?.Dispose();
    }

    /// <summary>Removes the installer left behind by the previous update.</summary>
    public static void DeleteDownloads()
    {
        try
        {
            if (Directory.Exists(DownloadFolder))
                Directory.Delete(DownloadFolder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only a leftover in the temp folder; opening the file for writing reports a real problem.
        }
    }

    /// <summary>Same message as installer\sign-update.ps1 signs.</summary>
    private static bool IsSigned(Stream installer, string signature, string version, string publicKey)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);

            byte[] message = [.. Encoding.UTF8.GetBytes($"ShotLite update {version}\n"), .. SHA256.HashData(installer)];
            return key.VerifyData(message, Convert.FromBase64String(signature.Trim()), HashAlgorithmName.SHA256);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return false;
        }
    }

    private static async Task DownloadFileAsync(string url, string path, long maxBytes)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd($"ShotLite/{UpdateChecker.CurrentVersion}");
        using var stall = new CancellationTokenSource(StallTimeout);
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stall.Token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maxBytes)
            throw new InvalidDataException();

        await using var source = await response.Content.ReadAsStreamAsync(stall.Token);
        await using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
        long total = 0;
        for (int read; (read = await source.ReadAsync(buffer, stall.Token)) > 0;)
        {
            total += read;
            if (total > maxBytes)
                throw new InvalidDataException();
            await target.WriteAsync(buffer.AsMemory(0, read));
            stall.CancelAfter(StallTimeout);
        }
    }
}
