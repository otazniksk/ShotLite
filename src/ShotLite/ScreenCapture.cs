using System.Drawing.Imaging;

namespace ShotLite;

internal static class ScreenCapture
{
    /// <summary>
    /// Captures all monitors as one bitmap. The process is per-monitor DPI aware,
    /// so the virtual screen is in physical pixels.
    /// </summary>
    public static Bitmap CaptureVirtualScreen(out Rectangle bounds)
    {
        bounds = SystemInformation.VirtualScreen;
        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
        bitmap.SetResolution(96, 96);
        using var g = Graphics.FromImage(bitmap);
        g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
        return bitmap;
    }
}
