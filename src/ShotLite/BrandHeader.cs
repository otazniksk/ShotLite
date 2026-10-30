using System.Drawing.Drawing2D;

namespace ShotLite;

/// <summary>Gradient header in the app icon's colours, shared by the About dialog and the settings.</summary>
internal sealed class BrandHeader : Panel
{
    // Same gradient as the app icon (design/draw_icon.py).
    private static readonly Color GradientStart = Color.FromArgb(56, 132, 255);
    private static readonly Color GradientEnd = Color.FromArgb(139, 61, 255);

    private readonly Bitmap _logo;
    private readonly string? _subtitle;

    /// <param name="subtitle">With one the header is a low strip: logo on the left, text next to it. Without, all centered.</param>
    public BrandHeader(Bitmap logo, string? subtitle = null)
    {
        _logo = logo;
        _subtitle = subtitle;
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var r = ClientRectangle;
        if (r.Width <= 0 || r.Height <= 0)
            return;

        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var gradient = new LinearGradientBrush(r, GradientStart, GradientEnd, 30F))
            g.FillRectangle(gradient, r);

        // Soft light circles for depth.
        using (var glow = new SolidBrush(Color.FromArgb(28, 255, 255, 255)))
        {
            int big = r.Height * 2;
            g.FillEllipse(glow, r.Right - big * 2 / 3, -big / 2, big, big);
            int small = r.Height;
            g.FillEllipse(glow, -small / 3, r.Bottom - small / 2, small, small);
        }

        if (_subtitle == null)
            DrawCentered(g, r);
        else
            DrawStrip(g, r, _subtitle);
    }

    private void DrawCentered(Graphics g, Rectangle r)
    {
        int logo = r.Height * 46 / 100;
        var logoBounds = new Rectangle(r.Left + (r.Width - logo) / 2, r.Top + r.Height * 11 / 100, logo, logo);
        using (var shadow = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
            g.FillEllipse(shadow, logoBounds.Left + logo / 10, logoBounds.Bottom - logo / 12, logo * 8 / 10, logo / 6);
        DrawLogo(g, logoBounds);

        using var font = new Font("Segoe UI Semibold", 17F);
        var textBounds = new Rectangle(r.Left, logoBounds.Bottom + r.Height * 4 / 100, r.Width, r.Bottom - logoBounds.Bottom);
        TextRenderer.DrawText(g, Strings.AppName, font, textBounds, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding);
    }

    private void DrawStrip(Graphics g, Rectangle r, string subtitle)
    {
        int logo = r.Height * 62 / 100, margin = (r.Height - logo) / 2;
        var logoBounds = new Rectangle(r.Left + margin + r.Height / 8, r.Top + margin, logo, logo);
        DrawLogo(g, logoBounds);

        using var name = new Font("Segoe UI Semibold", 15F);
        using var small = new Font("Segoe UI", 9F);
        int nameHeight = TextRenderer.MeasureText(g, Strings.AppName, name, Size.Empty, TextFormatFlags.NoPadding).Height;
        int subtitleHeight = TextRenderer.MeasureText(g, subtitle, small, Size.Empty, TextFormatFlags.NoPadding).Height;
        int left = logoBounds.Right + margin, top = r.Top + (r.Height - nameHeight - subtitleHeight) / 2;
        TextRenderer.DrawText(g, Strings.AppName, name, new Point(left, top), Color.White, TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, subtitle, small, new Point(left, top + nameHeight), Color.FromArgb(230, 235, 255), TextFormatFlags.NoPadding);
    }

    private void DrawLogo(Graphics g, Rectangle bounds)
    {
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(_logo, bounds);
    }
}
