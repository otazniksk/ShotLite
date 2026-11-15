using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace ShotLite;

/// <summary>
/// Text recognition with the OCR engine built into Windows (offline). It knows only the languages
/// installed in Windows, so the engine follows the user's language list.
/// </summary>
internal static class TextRecognizer
{
    /// <summary>Screen text is small for the engine; upscaling a modest selection makes it far more reliable.</summary>
    private const int SmallImage = 1500;

    /// <summary>The engine for the user's Windows languages, or null when none of them has OCR installed.</summary>
    public static OcrEngine? CreateEngine() => OcrEngine.TryCreateFromUserProfileLanguages();

    /// <summary>Recognized text, one line per row; empty when there is none.</summary>
    public static async Task<string> RecognizeAsync(OcrEngine engine, Bitmap image)
    {
        using var bitmap = Prepare(image);
        var result = await engine.RecognizeAsync(bitmap);
        return string.Join(Environment.NewLine, result.Lines.Select(line => line.Text)).Trim();
    }

    private static SoftwareBitmap Prepare(Bitmap image)
    {
        int max = (int)OcrEngine.MaxImageDimension;
        double scale = Math.Max(image.Width, image.Height) <= SmallImage ? 3 : 2;
        scale = Math.Min(scale, (double)max / Math.Max(image.Width, image.Height));

        int width = Math.Max(1, (int)(image.Width * scale));
        int height = Math.Max(1, (int)(image.Height * scale));
        using var scaled = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        scaled.SetResolution(96, 96);
        using (var g = Graphics.FromImage(scaled))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using var attributes = new ImageAttributes();
            attributes.SetWrapMode(WrapMode.TileFlipXY); // no dark seam along the edges
            g.DrawImage(image, new Rectangle(0, 0, width, height), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
        }

        var data = scaled.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            // Bgra8 rows without padding; GDI+ 32 bpp rows are already width * 4 bytes.
            var pixels = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * width * 4, width * 4);
            return SoftwareBitmap.CreateCopyFromBuffer(pixels.AsBuffer(), BitmapPixelFormat.Bgra8, width, height);
        }
        finally
        {
            scaled.UnlockBits(data);
        }
    }
}
