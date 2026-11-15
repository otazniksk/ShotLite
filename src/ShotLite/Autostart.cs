using Microsoft.Win32;

namespace ShotLite;

/// <summary>Per-user autostart via HKCU\...\Run (no admin rights needed).</summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>The registry is the source of truth: the installer can turn autostart on as well.</summary>
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(Strings.AppName) is string;
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(Strings.AppName, $"\"{Application.ExecutablePath}\"");
        else
            key.DeleteValue(Strings.AppName, throwOnMissingValue: false);
    }

    private const string KeyboardKey = @"Control Panel\Keyboard";
    private const string SnippingValue = "PrintScreenKeyForSnippingEnabled";

    /// <summary>True when Windows opens its screen capture (Snipping Tool) on PrtScr, which steals the default hotkey.</summary>
    public static bool PrintScreenOpensSnippingTool()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyboardKey);
        return key?.GetValue(SnippingValue) switch
        {
            int value => value == 1,
            // Windows 11 has it on by default; the value appears only once the user flips the setting.
            // Windows 10 has the same setting, off by default.
            _ => Environment.OSVersion.Version.Build >= 22000,
        };
    }

    /// <summary>
    /// Turns off "Use the Print screen key to open screen capture" for the current user (HKCU, no admin rights).
    /// Windows reads the value on every key press, so it applies at once.
    /// </summary>
    public static void DisablePrintScreenSnipping()
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyboardKey);
        key.SetValue(SnippingValue, 0, RegistryValueKind.DWord);
    }
}
