using System.Drawing.Drawing2D;

namespace ShotLite;

internal static class Shapes
{
    public static Rectangle Normalize(Point a, Point b) =>
        Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    /// <summary>Union that treats an empty rectangle as "nothing" instead of the origin.</summary>
    public static Rectangle Union(Rectangle a, Rectangle b) =>
        a.IsEmpty ? b : b.IsEmpty ? a : Rectangle.Union(a, b);

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void DrawArrow(Graphics g, Color color, float width, PointF from, PointF to)
    {
        float dx = to.X - from.X, dy = to.Y - from.Y;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        if (length < 1)
            return;

        float ux = dx / length, uy = dy / length;
        float head = Math.Min(length, width * 3 + 10);
        const float spread = 0.45f; // ~26° per wing

        PointF Wing(float angle)
        {
            float c = MathF.Cos(angle), s = MathF.Sin(angle);
            return new PointF(to.X - head * (ux * c - uy * s), to.Y - head * (ux * s + uy * c));
        }

        var shaftEnd = new PointF(to.X - ux * head * 0.7f, to.Y - uy * head * 0.7f);
        using (var pen = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Flat })
            g.DrawLine(pen, from, shaftEnd);

        using var brush = new SolidBrush(color);
        g.FillPolygon(brush, new[] { to, Wing(spread), Wing(-spread) });
    }

    /// <summary>Snaps the end point to the nearest 45° direction from the origin.</summary>
    public static Point Snap45(Point origin, Point p)
    {
        double dx = p.X - origin.X, dy = p.Y - origin.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        double step = Math.PI / 4;
        double angle = Math.Round(Math.Atan2(dy, dx) / step) * step;
        return new Point(origin.X + (int)Math.Round(Math.Cos(angle) * length),
                         origin.Y + (int)Math.Round(Math.Sin(angle) * length));
    }

    public static Point SnapSquare(Point origin, Point p)
    {
        int dx = p.X - origin.X, dy = p.Y - origin.Y;
        int size = Math.Max(Math.Abs(dx), Math.Abs(dy));
        return new Point(origin.X + (dx < 0 ? -size : size), origin.Y + (dy < 0 ? -size : size));
    }
}
