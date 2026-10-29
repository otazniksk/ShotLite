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

    /// <summary>True when Windows 11 opens the Snipping Tool on PrtScr, which steals the default hotkey.</summary>
    public static bool PrintScreenOpensSnippingTool()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Keyboard");
        return key?.GetValue("PrintScreenKeyForSnippingEnabled") is int value && value == 1;
    }
}
