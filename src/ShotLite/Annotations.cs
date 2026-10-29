using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ShotLite;

public enum RedactStyle { Black, Frosted, Pixelate }

/// <summary>A shape drawn on top of the screenshot, in overlay (= screenshot) pixel coordinates.</summary>
internal abstract class Annotation
{
    protected Annotation(Color color, float width)
    {
        Color = color;
        Width = width;
    }

    public Color Color { get; }
    public float Width { get; }

    /// <summary>False for shapes too small to keep, e.g. a zero-length line.</summary>
    public virtual bool IsMeaningful => true;

    public abstract void Draw(Graphics g);

    protected Pen CreatePen() => new(Color, Width)
    {
        StartCap = LineCap.Round,
        EndCap = LineCap.Round,
        LineJoin = LineJoin.Round,
    };
}

/// <summary>Freehand stroke; used for both the pencil and the (semi-transparent) marker.</summary>
internal sealed class StrokeAnnotation : Annotation
{
    private readonly List<Point> _points;

    public StrokeAnnotation(Color color, float width, Point start) : base(color, width) => _points = [start];

    public void Add(Point p)
    {
        if (_points[^1] != p)
            _points.Add(p);
    }

    public override void Draw(Graphics g)
    {
        if (_points.Count == 1)
        {
            using var brush = new SolidBrush(Color);
            float r = Width / 2;
            g.FillEllipse(brush, _points[0].X - r, _points[0].Y - r, Width, Width);
            return;
        }

        using var pen = CreatePen();
        g.DrawLines(pen, _points.ToArray());
    }
}

internal sealed class LineAnnotation : Annotation
{
    private readonly bool _arrow;

    public LineAnnotation(Color color, float width, Point start, bool arrow) : base(color, width)
    {
        Start = start;
        End = start;
        _arrow = arrow;
    }

    public Point Start { get; }
    public Point End { get; set; }

    public override bool IsMeaningful => Start != End;

    public override void Draw(Graphics g)
    {
        if (_arrow)
        {
            Shapes.DrawArrow(g, Color, Width, Start, End);
            return;
        }

        using var pen = CreatePen();
        g.DrawLine(pen, Start, End);
    }
}

internal sealed class RectAnnotation : Annotation
{
    public RectAnnotation(Color color, float width, Point start) : base(color, width)
    {
        Start = start;
        End = start;
    }

    public Point Start { get; }
    public Point End { get; set; }

    public override bool IsMeaningful => Start.X != End.X && Start.Y != End.Y;

    public override void Draw(Graphics g)
    {
        using var pen = CreatePen();
        pen.LineJoin = LineJoin.Miter;
        g.DrawRectangle(pen, Shapes.Normalize(Start, End));
    }
}

/// <summary>
/// Hides a region, e.g. passwords. Black is irreversible; frosted glass and pixelation are made
/// from a heavily downscaled copy, so fine detail is gone, but they are not as absolute as black.
/// </summary>
internal sealed class RedactAnnotation : Annotation, IDisposable
{
    private const int FrostedFactor = 12;
    private const int PixelBlock = 14;

    private readonly Bitmap _source;
    private readonly RedactStyle _style;
    private Bitmap? _effect;
    private Rectangle _effectBounds;

    /// <param name="source">The screenshot; its pixel coordinates equal annotation coordinates.</param>
    public RedactAnnotation(RedactStyle style, Bitmap source, Point start) : base(Color.Black, 0)
    {
        _style = style;
        _source = source;
        Start = start;
        End = start;
    }

    public Point Start { get; }
    public Point End { get; set; }

    public override bool IsMeaningful => Start.X != End.X && Start.Y != End.Y;

    public override void Draw(Graphics g)
    {
        var bounds = Rectangle.Intersect(Shapes.Normalize(Start, End), new Rectangle(Point.Empty, _source.Size));
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        if (_style == RedactStyle.Black)
        {
            var smoothing = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.None; // crisp, fully opaque edges
            using var brush = new SolidBrush(Color.Black);
            g.FillRectangle(brush, bounds);
            g.SmoothingMode = smoothing;
            return;
        }

        if (_effect == null || _effectBounds != bounds)
        {
            _effect?.Dispose();
            _effect = CreateEffect(bounds);
            _effectBounds = bounds;
        }
        g.DrawImage(_effect, bounds, new Rectangle(Point.Empty, bounds.Size), GraphicsUnit.Pixel);
    }

    private Bitmap CreateEffect(Rectangle bounds)
    {
        if (_style == RedactStyle.Pixelate)
        {
            using var blocks = Resample(_source, bounds, Downscaled(bounds.Size, PixelBlock), InterpolationMode.HighQualityBilinear);
            return Resample(blocks, new Rectangle(Point.Empty, blocks.Size), bounds.Size, InterpolationMode.NearestNeighbor);
        }

        // Blur: average down hard, then scale up in two bicubic steps so no grid of blobs shows.
        using var tiny = Resample(_source, bounds, Downscaled(bounds.Size, FrostedFactor), InterpolationMode.HighQualityBilinear);
        using var soft = Resample(tiny, new Rectangle(Point.Empty, tiny.Size), Downscaled(bounds.Size, 3), InterpolationMode.HighQualityBicubic);
        var effect = Resample(soft, new Rectangle(Point.Empty, soft.Size), bounds.Size, InterpolationMode.HighQualityBicubic);
        DrawGlass(effect);
        return effect;
    }

    private static Size Downscaled(Size size, int factor) =>
        new(Math.Max(1, size.Width / factor), Math.Max(1, size.Height / factor));

    private Bitmap Resample(Bitmap from, Rectangle source, Size size, InterpolationMode mode)
    {
        var result = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
        result.SetResolution(_source.HorizontalResolution, _source.VerticalResolution);
        using var g = Graphics.FromImage(result);
        using var attributes = new ImageAttributes();
        // Mirrored edges keep bicubic scaling from fading to transparent at the border.
        attributes.SetWrapMode(WrapMode.TileFlipXY);
        g.InterpolationMode = mode;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(from, new Rectangle(Point.Empty, size), source.X, source.Y, source.Width, source.Height,
            GraphicsUnit.Pixel, attributes);
        return result;
    }

    /// <summary>Light frost, a diagonal sheen and bright/dark edges so the blur reads as a pane of glass.</summary>
    private static void DrawGlass(Bitmap image)
    {
        var r = new Rectangle(Point.Empty, image.Size);
        using var g = Graphics.FromImage(image);

        using (var frost = new SolidBrush(Color.FromArgb(45, 255, 255, 255)))
            g.FillRectangle(frost, r);

        // Sheen: brighter top-left fading out towards the middle.
        var sheenEnd = new Point(r.Width / 2, r.Height);
        if (sheenEnd != Point.Empty)
        {
            using var sheen = new LinearGradientBrush(Point.Empty, sheenEnd,
                Color.FromArgb(70, 255, 255, 255), Color.FromArgb(0, 255, 255, 255));
            sheen.WrapMode = WrapMode.TileFlipX;
            g.FillRectangle(sheen, r);
        }

        if (r.Width > 4 && r.Height > 4)
        {
            using var shadow = new Pen(Color.FromArgb(70, 0, 0, 0));
            g.DrawRectangle(shadow, 0, 0, r.Width - 1, r.Height - 1);
            using var highlight = new Pen(Color.FromArgb(150, 255, 255, 255));
            g.DrawRectangle(highlight, 1, 1, r.Width - 3, r.Height - 3);
        }
    }

    public void Dispose() => _effect?.Dispose();
}

internal sealed class TextAnnotation : Annotation, IDisposable
{
    private const TextFormatFlags Flags =
        TextFormatFlags.NoPadding
        | TextFormatFlags.PreserveGraphicsTranslateTransform
        | TextFormatFlags.PreserveGraphicsClipping;

    private readonly Point _location;
    private readonly string _text;
    private readonly Font _font;

    public TextAnnotation(Color color, Point location, string text, Font font) : base(color, 0)
    {
        _location = location;
        _text = text;
        _font = font;
    }

    public override void Draw(Graphics g) => TextRenderer.DrawText(g, _text, _font, _location, Color, Flags);

    public void Dispose() => _font.Dispose();
}
