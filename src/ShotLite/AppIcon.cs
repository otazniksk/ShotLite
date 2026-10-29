namespace ShotLite;

/// <summary>Loads the multi-size app icon (Assets\app.ico, drawn by design/draw_icon.py).</summary>
internal static class AppIcon
{
    /// <param name="size">Requested size in pixels; the closest frame in the .ico is used.</param>
    public static Icon Load(Size size)
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("ShotLite.app.ico")
            ?? throw new InvalidOperationException("Embedded resource ShotLite.app.ico is missing.");
        return new Icon(stream, size);
    }

    /// <summary>Tray icons use the small icon size of the current DPI (16 px at 100 %, 20 px at 125 %).</summary>
    public static Icon LoadSmall() => Load(SystemInformation.SmallIconSize);
}
