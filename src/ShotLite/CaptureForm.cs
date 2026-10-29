using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ShotLite;

internal enum Tool { None, Pencil, Line, Arrow, Rectangle, Redact, Marker, Text }

internal enum ToolbarAction { Pencil, Line, Arrow, Rectangle, Redact, Marker, Text, Color, Undo, Upload, Print, Copy, Save, Close }

internal sealed class ToolbarButton
{
    public ToolbarButton(ToolbarAction action, string tooltip, bool vertical)
    {
        Action = action;
        Tooltip = tooltip;
        Vertical = vertical;
    }

    public ToolbarAction Action { get; }
    public string Tooltip { get; set; }
    public bool Vertical { get; }
    public Rectangle Bounds { get; set; }
}

/// <summary>
/// Full-screen overlay spanning all monitors: frozen screenshot, region selection and the annotation editor.
/// Client coordinates are identical to screenshot pixel coordinates.
/// </summary>
internal sealed class CaptureForm : Form
{
    private const int NoHandle = -1;
    private static readonly Color Accent = Color.FromArgb(0, 120, 215);

    /// <summary>Outside: mouse pressed outside the selection; a click copies, a drag starts a new selection.</summary>
    private enum DragMode { None, Creating, Moving, Resizing, Drawing, Outside }

    private readonly Bitmap _shot;
    private readonly Bitmap _dimmed;
    private readonly Rectangle _screenBounds;
    private readonly AppSettings _settings;
    private readonly ImageActions _actions;
    private readonly List<Annotation> _annotations = new();
    private readonly List<ToolbarButton> _buttons = new();

    private Rectangle _selection;
    private DragMode _drag;
    private Point _dragStart;
    private Rectangle _selectionAtDragStart;
    private int _activeHandle = NoHandle;
    private Annotation? _current;
    private Tool _tool = Tool.None;
    private Color _color;
    private float _penWidth;
    private TextBox? _textBox;
    private ToolbarButton? _hover;
    private Rectangle _verticalBar, _horizontalBar;
    private Rectangle _lastVisual;
    private Rectangle? _hintBounds;
    private Font? _uiFont, _iconFont, _iconTextFont;
    private RedactStyle _redactStyle;
    private readonly System.Windows.Forms.Timer _holdTimer = new() { Interval = 350 };
    private bool _redactPressed;

    public CaptureForm(Bitmap capture, Rectangle bounds, AppSettings settings, ImageActions actions)
    {
        _settings = settings;
        _actions = actions;
        _screenBounds = bounds;
        _color = Color.FromArgb(settings.ColorArgb);
        _penWidth = Math.Clamp(settings.PenWidth, 1, 40);
        _redactStyle = Enum.IsDefined(settings.RedactStyle) ? settings.RedactStyle : RedactStyle.Black;
        _holdTimer.Tick += (_, _) => OnRedactHold();

        // PArgb is the fastest format for GDI+ to blit to the screen.
        _shot = capture.Clone(new Rectangle(Point.Empty, capture.Size), PixelFormat.Format32bppPArgb);
        _dimmed = CreateDimmed(_shot);

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        Bounds = bounds;
        TopMost = true;
        ShowInTaskbar = false;
        KeyPreview = true;
        Text = Strings.AppName;
        Cursor = Cursors.Cross;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                 | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);

        AddButton(ToolbarAction.Pencil, Strings.ToolPencil, vertical: true);
        AddButton(ToolbarAction.Line, Strings.ToolLine, vertical: true);
        AddButton(ToolbarAction.Arrow, Strings.ToolArrow, vertical: true);
        AddButton(ToolbarAction.Rectangle, Strings.ToolRectangle, vertical: true);
        AddButton(ToolbarAction.Redact, RedactTooltip(), vertical: true);
        AddButton(ToolbarAction.Marker, Strings.ToolMarker, vertical: true);
        AddButton(ToolbarAction.Text, Strings.ToolText, vertical: true);
        AddButton(ToolbarAction.Color, Strings.ToolColor, vertical: true);
        AddButton(ToolbarAction.Undo, Strings.ToolUndo, vertical: true);

        if (settings.Upload.IsConfigured)
            AddButton(ToolbarAction.Upload, Strings.ActionUpload, vertical: false);
        AddButton(ToolbarAction.Print, Strings.ActionPrint, vertical: false);
        AddButton(ToolbarAction.Copy, Strings.ActionCopy, vertical: false);
        AddButton(ToolbarAction.Save, Strings.ActionSave, vertical: false);
        AddButton(ToolbarAction.Close, Strings.ActionClose, vertical: false);
    }

    private bool HasSelection => _selection.Width >= 3 && _selection.Height >= 3;
    private bool ToolbarsVisible => HasSelection && _drag is DragMode.None or DragMode.Drawing;
    private float TextPixelSize => S(14) + _penWidth * 2;

    private Font UiFont => _uiFont ??= new Font("Segoe UI", S(12), FontStyle.Regular, GraphicsUnit.Pixel);
    private Font IconFont => _iconFont ??= new Font("Segoe MDL2 Assets", S(16), FontStyle.Regular, GraphicsUnit.Pixel);
    private Font IconTextFont => _iconTextFont ??= new Font("Segoe UI", S(17), FontStyle.Bold, GraphicsUnit.Pixel);

    private int S(int value) => (int)Math.Round(value * DeviceDpi / 96.0);

    private void AddButton(ToolbarAction action, string tooltip, bool vertical) =>
        _buttons.Add(new ToolbarButton(action, tooltip, vertical));

    private static Bitmap CreateDimmed(Bitmap source)
    {
        var dimmed = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
        // A new bitmap gets the system DPI (120 at 125 %); copy pixel-for-pixel so it is never rescaled.
        dimmed.SetResolution(source.HorizontalResolution, source.VerticalResolution);
        using var g = Graphics.FromImage(dimmed);
        var all = new Rectangle(Point.Empty, source.Size);
        g.DrawImage(source, all, all, GraphicsUnit.Pixel);
        using var shade = new SolidBrush(Color.FromArgb(110, 0, 0, 0));
        g.FillRectangle(shade, 0, 0, source.Width, source.Height);
        return dimmed;
    }

    // ---------------------------------------------------------------- lifecycle

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        _lastVisual = HintBounds();
        Invalidate();
    }

    protected override void WndProc(ref Message m)
    {
        // The overlay deliberately spans monitors with different DPI (e.g. 125 % + 100 %).
        // Windows/WinForms would rescale or move it to the DPI of one monitor, which shifts the
        // frozen screenshot against the real screen. Ignore DPI changes and pin the window
        // to the exact physical virtual-screen rectangle on every position/size change.
        switch (m.Msg)
        {
            case Native.WM_DPICHANGED:
                m.Result = IntPtr.Zero;
                return;
            case Native.WM_WINDOWPOSCHANGING when m.LParam != IntPtr.Zero:
                var pos = Marshal.PtrToStructure<Native.WINDOWPOS>(m.LParam);
                pos.x = _screenBounds.X;
                pos.y = _screenBounds.Y;
                pos.cx = _screenBounds.Width;
                pos.cy = _screenBounds.Height;
                pos.flags &= ~(Native.SWP_NOMOVE | Native.SWP_NOSIZE);
                Marshal.StructureToPtr(pos, m.LParam, fDeleteOld: false);
                break;
        }
        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _shot.Dispose();
            _dimmed.Dispose();
            _holdTimer.Dispose();
            (_current as IDisposable)?.Dispose();
            _uiFont?.Dispose();
            _iconFont?.Dispose();
            _iconTextFont?.Dispose();
            foreach (var annotation in _annotations.OfType<IDisposable>())
                annotation.Dispose();
        }
        base.Dispose(disposing);
    }

    // ---------------------------------------------------------------- painting

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Everything is painted in OnPaint.
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var clip = e.ClipRectangle;
        g.DrawImage(_dimmed, clip, clip, GraphicsUnit.Pixel);

        if (!HasSelection)
        {
            if (_drag != DragMode.Creating)
                DrawHint(g);
            return;
        }

        g.DrawImage(_shot, _selection, _selection, GraphicsUnit.Pixel);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.SetClip(_selection);
        foreach (var annotation in _annotations)
            annotation.Draw(g);
        _current?.Draw(g);
        g.ResetClip();

        DrawSelectionFrame(g);
        DrawSizeLabel(g);
        if (ToolbarsVisible)
            DrawToolbars(g);
    }

    private void DrawHint(Graphics g)
    {
        var bounds = HintBounds();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Shapes.RoundedRect(bounds, S(6)))
        using (var background = new SolidBrush(Color.FromArgb(200, 20, 20, 20)))
            g.FillPath(background, path);
        TextRenderer.DrawText(g, Strings.Hint, UiFont, bounds, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private void DrawSelectionFrame(Graphics g)
    {
        var frame = new Rectangle(_selection.X, _selection.Y, _selection.Width - 1, _selection.Height - 1);
        g.SmoothingMode = SmoothingMode.None;
        using (var dark = new Pen(Color.FromArgb(160, 0, 0, 0)))
            g.DrawRectangle(dark, frame);
        using (var dashes = new Pen(Color.White) { DashStyle = DashStyle.Dash })
            g.DrawRectangle(dashes, frame);

        int size = S(7);
        using var fill = new SolidBrush(Color.White);
        using var border = new Pen(Color.FromArgb(90, 90, 90));
        foreach (var point in HandlePoints())
        {
            var handle = new Rectangle(point.X - size / 2, point.Y - size / 2, size, size);
            g.FillRectangle(fill, handle);
            g.DrawRectangle(border, handle);
        }
        g.SmoothingMode = SmoothingMode.AntiAlias;
    }

    private void DrawSizeLabel(Graphics g)
    {
        var bounds = SizeLabelBounds();
        using (var path = Shapes.RoundedRect(bounds, S(4)))
        using (var background = new SolidBrush(Color.FromArgb(200, 20, 20, 20)))
            g.FillPath(background, path);
        TextRenderer.DrawText(g, SizeLabelText(), UiFont, bounds, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private void DrawToolbars(Graphics g)
    {
        DrawBar(g, _verticalBar);
        DrawBar(g, _horizontalBar);
        foreach (var button in _buttons)
            DrawButton(g, button);
        if (_hover != null)
            DrawTooltip(g, _hover);
    }

    private void DrawBar(Graphics g, Rectangle bar)
    {
        using var path = Shapes.RoundedRect(bar, S(6));
        using var background = new SolidBrush(Color.FromArgb(250, 250, 250));
        using var border = new Pen(Color.FromArgb(70, 0, 0, 0));
        g.FillPath(background, path);
        g.DrawPath(border, path);
    }

    private void DrawButton(Graphics g, ToolbarButton button)
    {
        var bounds = button.Bounds;
        bool active = ToolFor(button.Action) is { } tool && tool == _tool;
        if (active || button == _hover)
        {
            using var path = Shapes.RoundedRect(Rectangle.Inflate(bounds, -S(2), -S(2)), S(4));
            using var highlight = new SolidBrush(active ? Color.FromArgb(60, Accent) : Color.FromArgb(25, 0, 0, 0));
            g.FillPath(highlight, path);
        }

        var foreground = Color.FromArgb(40, 40, 40);
        var inner = Rectangle.Inflate(bounds, -S(9), -S(9));
        using var pen = new Pen(foreground, Math.Max(1.5f, S(2)))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };

        switch (button.Action)
        {
            case ToolbarAction.Pencil: Glyph(g, "", bounds, foreground); break;
            case ToolbarAction.Line: g.DrawLine(pen, inner.Left, inner.Bottom, inner.Right, inner.Top); break;
            case ToolbarAction.Arrow:
                Shapes.DrawArrow(g, foreground, pen.Width, new PointF(inner.Left, inner.Bottom), new PointF(inner.Right, inner.Top));
                break;
            case ToolbarAction.Rectangle:
                pen.LineJoin = LineJoin.Miter;
                g.DrawRectangle(pen, inner.Left, inner.Top + S(2), inner.Width, inner.Height - S(4));
                break;
            case ToolbarAction.Redact:
                DrawRedactGlyph(g, _redactStyle, new Rectangle(inner.Left, inner.Top + S(3), inner.Width, inner.Height - S(6)), foreground);
                // Small corner triangle: press and hold opens the style menu.
                using (var corner = new SolidBrush(Color.FromArgb(120, foreground)))
                {
                    int c = S(5);
                    g.FillPolygon(corner, new Point[]
                    {
                        new(bounds.Right - S(3), bounds.Bottom - S(3) - c),
                        new(bounds.Right - S(3), bounds.Bottom - S(3)),
                        new(bounds.Right - S(3) - c, bounds.Bottom - S(3)),
                    });
                }
                break;
            case ToolbarAction.Marker:
                DrawMarkerGlyph(g, Rectangle.Inflate(bounds, -S(6), -S(6)), foreground, 1.6f * DeviceDpi / 96f);
                break;
            case ToolbarAction.Text:
                TextRenderer.DrawText(g, "T", IconTextFont, bounds, foreground,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                break;
            case ToolbarAction.Color:
                using (var swatch = new SolidBrush(_color))
                    g.FillEllipse(swatch, inner);
                using (var outline = new Pen(Color.FromArgb(120, 0, 0, 0)))
                    g.DrawEllipse(outline, inner);
                break;
            case ToolbarAction.Undo: Glyph(g, "", bounds, _annotations.Count > 0 ? foreground : Color.Silver); break;
            case ToolbarAction.Upload: Glyph(g, "", bounds, foreground); break;
            case ToolbarAction.Print: Glyph(g, "", bounds, foreground); break;
            case ToolbarAction.Copy: Glyph(g, "", bounds, foreground); break;
            case ToolbarAction.Save: Glyph(g, "", bounds, foreground); break;
            case ToolbarAction.Close: Glyph(g, "", bounds, foreground); break;
        }
    }

    /// <summary>Outline highlighter tilted 35°, its chisel tip resting on a baseline.</summary>
    private static void DrawMarkerGlyph(Graphics g, Rectangle r, Color color, float width)
    {
        float s = r.Height;
        float baseline = r.Top + s * 0.90f;
        var state = g.Save();
        // Origin chosen so the tip lands on the baseline.
        g.TranslateTransform(r.Left + r.Width * 0.50f, baseline - s * 0.36f);
        g.RotateTransform(35f);
        using (var pen = new Pen(color, width) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            float hw = s * 0.16f, top = -s * 0.52f, collar = s * 0.08f, neck = s * 0.24f, nw = s * 0.085f, radius = hw * 0.7f;
            using (var body = new GraphicsPath())
            {
                body.AddLine(-hw, collar, -hw, top + radius);
                body.AddArc(-hw, top, radius * 2, radius * 2, 180, 90);
                body.AddArc(hw - radius * 2, top, radius * 2, radius * 2, 270, 90);
                body.AddLine(hw, top + radius, hw, collar);
                body.CloseFigure();
                g.DrawPath(pen, body);
            }
            g.DrawLine(pen, -hw, top + s * 0.18f, hw, top + s * 0.18f); // cap
            g.DrawPolygon(pen, new PointF[] { new(-hw, collar), new(hw, collar), new(nw, neck), new(-nw, neck) });
            g.DrawPolygon(pen, new PointF[] { new(-nw, neck), new(nw, neck), new(nw, neck + s * 0.06f), new(-nw, neck + s * 0.15f) });
        }
        g.Restore(state);
        using var line = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(line, r.Left + r.Width * 0.06f, baseline, r.Left + r.Width * 0.94f, baseline);
    }

    private static void DrawRedactGlyph(Graphics g, RedactStyle style, Rectangle r, Color foreground)
    {
        switch (style)
        {
            case RedactStyle.Frosted:
                using (var glass = new LinearGradientBrush(r, Color.FromArgb(95, 160, 230), Color.FromArgb(185, 140, 235), 45f))
                    g.FillRectangle(glass, r);
                // Diagonal sheen across the upper-left half, like light on a glass pane.
                using (var sheen = new LinearGradientBrush(r, Color.FromArgb(200, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 35f))
                    g.FillPolygon(sheen, new Point[] { new(r.Left, r.Top), new(r.Left + r.Width * 2 / 3, r.Top), new(r.Left, r.Bottom) });
                using (var highlight = new Pen(Color.FromArgb(220, 255, 255, 255)))
                    g.DrawRectangle(highlight, r.Left + 1, r.Top + 1, r.Width - 2, r.Height - 2);
                using (var outline = new Pen(Color.FromArgb(130, foreground)))
                    g.DrawRectangle(outline, r);
                break;
            case RedactStyle.Pixelate:
                int cell = Math.Max(2, r.Width / 4);
                using (var dark = new SolidBrush(Color.FromArgb(70, 70, 70)))
                using (var light = new SolidBrush(Color.FromArgb(175, 175, 175)))
                {
                    for (int y = 0; y * cell < r.Height; y++)
                    for (int x = 0; x * cell < r.Width; x++)
                    {
                        var c = new Rectangle(r.Left + x * cell, r.Top + y * cell,
                            Math.Min(cell, r.Width - x * cell), Math.Min(cell, r.Height - y * cell));
                        g.FillRectangle((x + y) % 2 == 0 ? dark : light, c);
                    }
                }
                break;
            default:
                using (var block = new SolidBrush(foreground))
                    g.FillRectangle(block, r);
                break;
        }
    }

    private void Glyph(Graphics g, string glyph, Rectangle bounds, Color color) =>
        TextRenderer.DrawText(g, glyph, IconFont, bounds, color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

    private void DrawTooltip(Graphics g, ToolbarButton button)
    {
        var bounds = TooltipBounds(button);
        using (var path = Shapes.RoundedRect(bounds, S(4)))
        using (var background = new SolidBrush(Color.FromArgb(230, 20, 20, 20)))
            g.FillPath(background, path);
        TextRenderer.DrawText(g, button.Tooltip, UiFont, bounds, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    // ---------------------------------------------------------------- layout

    private Rectangle HintBounds()
    {
        if (_hintBounds is { } cached)
            return cached;

        var monitor = RectangleToClient(Screen.FromPoint(MousePosition).Bounds);
        var text = TextRenderer.MeasureText(Strings.Hint, UiFont);
        var size = new Size(text.Width + S(32), text.Height + S(18));
        var bounds = new Rectangle(monitor.Left + (monitor.Width - size.Width) / 2,
                                   monitor.Top + (monitor.Height - size.Height) / 2, size.Width, size.Height);
        _hintBounds = bounds;
        return bounds;
    }

    private string SizeLabelText() =>
        _tool == Tool.None
            ? $"{_selection.Width} × {_selection.Height}"
            : $"{_selection.Width} × {_selection.Height}   ·   {_penWidth:0} px";

    private Rectangle SizeLabelBounds()
    {
        var text = TextRenderer.MeasureText(SizeLabelText(), UiFont);
        var size = new Size(text.Width + S(12), text.Height + S(6));
        int y = _selection.Top - size.Height - S(6);
        if (y < 0)
            y = _selection.Top + S(6);
        int x = Math.Clamp(_selection.Left, 0, Math.Max(0, ClientSize.Width - size.Width));
        return new Rectangle(new Point(x, y), size);
    }

    private Rectangle TooltipBounds(ToolbarButton button)
    {
        var text = TextRenderer.MeasureText(button.Tooltip, UiFont);
        var size = new Size(text.Width + S(14), text.Height + S(8));
        var b = button.Bounds;
        Rectangle bounds;
        if (button.Vertical)
        {
            bounds = new Rectangle(new Point(_verticalBar.Left - S(6) - size.Width, b.Top + (b.Height - size.Height) / 2), size);
            if (bounds.Left < 0)
                bounds.X = _verticalBar.Right + S(6);
        }
        else
        {
            bounds = new Rectangle(new Point(b.Left + (b.Width - size.Width) / 2, _horizontalBar.Bottom + S(6)), size);
            if (bounds.Bottom > ClientSize.Height)
                bounds.Y = _horizontalBar.Top - S(6) - size.Height;
        }
        bounds.X = Math.Clamp(bounds.X, 0, Math.Max(0, ClientSize.Width - bounds.Width));
        return bounds;
    }

    /// <summary>Tools to the right of the selection (bottom-aligned), actions below it (right-aligned), like Lightshot.</summary>
    private void LayoutToolbars()
    {
        _verticalBar = _horizontalBar = Rectangle.Empty;
        if (!HasSelection)
            return;

        int button = S(32), pad = S(4), gap = S(8);
        var area = RectangleToClient(Screen.FromRectangle(RectangleToScreen(_selection)).Bounds);
        var vertical = _buttons.Where(b => b.Vertical).ToList();
        var horizontal = _buttons.Where(b => !b.Vertical).ToList();

        int vw = button + 2 * pad, vh = vertical.Count * button + 2 * pad;
        int vx = _selection.Right + gap;
        if (vx + vw > area.Right) vx = _selection.Left - gap - vw;
        if (vx < area.Left) vx = _selection.Right - gap - vw;
        int vy = Math.Clamp(_selection.Bottom - vh, area.Top, Math.Max(area.Top, area.Bottom - vh));
        _verticalBar = new Rectangle(vx, vy, vw, vh);

        int hw = horizontal.Count * button + 2 * pad, hh = button + 2 * pad;
        int hy = _selection.Bottom + gap;
        if (hy + hh > area.Bottom) hy = _selection.Top - gap - hh;
        if (hy < area.Top) hy = _selection.Bottom - gap - hh;
        int hx = Math.Clamp(_selection.Right - hw, area.Left, Math.Max(area.Left, area.Right - hw));
        _horizontalBar = new Rectangle(hx, hy, hw, hh);
        if (_horizontalBar.IntersectsWith(_verticalBar))
            _horizontalBar.X = Math.Max(area.Left, _verticalBar.Left - gap - hw);

        for (int i = 0; i < vertical.Count; i++)
            vertical[i].Bounds = new Rectangle(_verticalBar.Left + pad, _verticalBar.Top + pad + i * button, button, button);
        for (int i = 0; i < horizontal.Count; i++)
            horizontal[i].Bounds = new Rectangle(_horizontalBar.Left + pad + i * button, _horizontalBar.Top + pad, button, button);
    }

    private Rectangle VisualBounds()
    {
        if (!HasSelection)
            return _drag == DragMode.Creating ? Rectangle.Empty : HintBounds();

        var visual = Rectangle.Inflate(_selection, S(8), S(8));
        visual = Shapes.Union(visual, SizeLabelBounds());
        if (ToolbarsVisible)
        {
            visual = Shapes.Union(visual, Rectangle.Inflate(_verticalBar, 1, 1));
            visual = Shapes.Union(visual, Rectangle.Inflate(_horizontalBar, 1, 1));
            if (_hover != null)
                visual = Shapes.Union(visual, TooltipBounds(_hover));
        }
        return visual;
    }

    /// <summary>Repaints only what changed: the previous and the current selection area with its decorations.</summary>
    private void RefreshVisual()
    {
        if (IsDisposed)
            return;
        LayoutToolbars();
        var visual = VisualBounds();
        Invalidate(Shapes.Union(_lastVisual, visual));
        _lastVisual = visual;
    }

    private Point[] HandlePoints()
    {
        var r = _selection;
        int cx = r.Left + r.Width / 2, cy = r.Top + r.Height / 2;
        return
        [
            new(r.Left, r.Top), new(cx, r.Top), new(r.Right, r.Top), new(r.Right, cy),
            new(r.Right, r.Bottom), new(cx, r.Bottom), new(r.Left, r.Bottom), new(r.Left, cy),
        ];
    }

    private int HitHandle(Point p)
    {
        int tolerance = S(6);
        var points = HandlePoints();
        for (int i = 0; i < points.Length; i++)
        {
            if (Math.Abs(p.X - points[i].X) <= tolerance && Math.Abs(p.Y - points[i].Y) <= tolerance)
                return i;
        }
        return NoHandle;
    }

    private ToolbarButton? HitButton(Point p) =>
        ToolbarsVisible ? _buttons.FirstOrDefault(b => b.Bounds.Contains(p)) : null;

    // ---------------------------------------------------------------- mouse

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button == MouseButtons.Right && HitButton(e.Location) is { Action: ToolbarAction.Redact } redactButton)
        {
            ShowRedactMenu(redactButton);
            return;
        }
        if (e.Button == MouseButtons.Right)
        {
            if (_textBox != null)
                CancelText();
            else if (HasSelection && _annotations.Count == 0)
                ResetSelection();
            else
                Close();
            return;
        }
        if (e.Button != MouseButtons.Left)
            return;

        bool wasEditingText = _textBox != null;
        CommitText();
        var p = e.Location;

        if (HitButton(p) is { } button)
        {
            if (button.Action == ToolbarAction.Redact)
            {
                // Click selects the tool on release; holding opens the style menu instead.
                _redactPressed = true;
                _holdTimer.Start();
                return;
            }
            OnToolbarButton(button.Action);
            return;
        }

        if (HasSelection)
        {
            int handle = HitHandle(p);
            if (handle != NoHandle)
            {
                BeginDrag(DragMode.Resizing, p);
                _activeHandle = handle;
                return;
            }

            if (_selection.Contains(p))
            {
                switch (_tool)
                {
                    case Tool.None:
                        BeginDrag(DragMode.Moving, p);
                        break;
                    case Tool.Text:
                        BeginText(p);
                        break;
                    default:
                        _current = CreateAnnotation(p);
                        BeginDrag(DragMode.Drawing, p);
                        RefreshVisual();
                        break;
                }
                return;
            }

            // A click outside only finishes the text being typed; it doesn't copy yet.
            if (!wasEditingText)
                BeginDrag(DragMode.Outside, p);
            return;
        }

        _selection = new Rectangle(p, Size.Empty);
        BeginDrag(DragMode.Creating, p);
        RefreshVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.Location;

        switch (_drag)
        {
            case DragMode.Outside:
                // Keep the work: a new selection is only allowed while nothing is drawn yet.
                if (_annotations.Count == 0 && IsDrag(p))
                {
                    _drag = DragMode.Creating;
                    _selection = Rectangle.Intersect(Shapes.Normalize(_dragStart, p), ClientRectangle);
                    RefreshVisual();
                }
                break;
            case DragMode.Creating:
                _selection = Rectangle.Intersect(Shapes.Normalize(_dragStart, p), ClientRectangle);
                RefreshVisual();
                break;
            case DragMode.Moving:
                var moved = _selectionAtDragStart;
                moved.Offset(p.X - _dragStart.X, p.Y - _dragStart.Y);
                moved.X = Math.Clamp(moved.X, 0, ClientSize.Width - moved.Width);
                moved.Y = Math.Clamp(moved.Y, 0, ClientSize.Height - moved.Height);
                _selection = moved;
                RefreshVisual();
                break;
            case DragMode.Resizing:
                ResizeSelection(p);
                RefreshVisual();
                break;
            case DragMode.Drawing:
                UpdateAnnotation(p);
                RefreshVisual();
                break;
            default:
                UpdateHover(p);
                break;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left && _redactPressed)
        {
            _redactPressed = false;
            _holdTimer.Stop();
            SelectTool(Tool.Redact);
            return;
        }
        if (e.Button != MouseButtons.Left || _drag == DragMode.None)
            return;

        Capture = false;
        if (_drag == DragMode.Outside)
        {
            _drag = DragMode.None;
            if (!IsDrag(e.Location))
                CopyAndClose();
            return;
        }
        if (_drag == DragMode.Drawing && _current != null)
        {
            if (_current.IsMeaningful)
                _annotations.Add(_current);
            else
                (_current as IDisposable)?.Dispose();
            _current = null;
        }
        if (_drag == DragMode.Creating && !HasSelection)
            _selection = Rectangle.Empty;

        _drag = DragMode.None;
        _activeHandle = NoHandle;
        RefreshVisual();
        UpdateHover(e.Location);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_textBox != null)
            return;
        _penWidth = Math.Clamp(_penWidth + (e.Delta > 0 ? 1 : -1), 1, 40);
        _settings.PenWidth = _penWidth;
        RefreshVisual();
    }

    /// <summary>True once the mouse has left the system drag rectangle around the press point.</summary>
    private bool IsDrag(Point p)
    {
        var size = SystemInformation.DragSize;
        return Math.Abs(p.X - _dragStart.X) > size.Width || Math.Abs(p.Y - _dragStart.Y) > size.Height;
    }

    private void BeginDrag(DragMode mode, Point p)
    {
        _drag = mode;
        _dragStart = p;
        _selectionAtDragStart = _selection;
        Capture = true;
    }

    private void ResizeSelection(Point p)
    {
        var r = _selectionAtDragStart;
        int dx = p.X - _dragStart.X, dy = p.Y - _dragStart.Y;
        int left = r.Left, top = r.Top, right = r.Right, bottom = r.Bottom;
        switch (_activeHandle)
        {
            case 0: left += dx; top += dy; break;
            case 1: top += dy; break;
            case 2: right += dx; top += dy; break;
            case 3: right += dx; break;
            case 4: right += dx; bottom += dy; break;
            case 5: bottom += dy; break;
            case 6: left += dx; bottom += dy; break;
            case 7: left += dx; break;
        }
        _selection = Rectangle.Intersect(
            Rectangle.FromLTRB(Math.Min(left, right), Math.Min(top, bottom), Math.Max(left, right), Math.Max(top, bottom)),
            ClientRectangle);
    }

    private void UpdateHover(Point p)
    {
        var button = HitButton(p);
        if (button != _hover)
        {
            _hover = button;
            RefreshVisual();
        }

        var cursor = CursorAt(p, button);
        if (Cursor != cursor)
            Cursor = cursor;
    }

    private Cursor CursorAt(Point p, ToolbarButton? button)
    {
        if (button != null)
            return Cursors.Hand;
        if (!HasSelection)
            return Cursors.Cross;

        switch (HitHandle(p))
        {
            case 0 or 4: return Cursors.SizeNWSE;
            case 2 or 6: return Cursors.SizeNESW;
            case 1 or 5: return Cursors.SizeNS;
            case 3 or 7: return Cursors.SizeWE;
        }

        if (_selection.Contains(p))
        {
            return _tool switch
            {
                Tool.None => Cursors.SizeAll,
                Tool.Text => Cursors.IBeam,
                _ => Cursors.Cross,
            };
        }
        return _annotations.Count > 0 ? Cursors.Default : Cursors.Cross;
    }

    // ---------------------------------------------------------------- annotations

    private Annotation CreateAnnotation(Point p) => _tool switch
    {
        Tool.Pencil => new StrokeAnnotation(_color, _penWidth, p),
        Tool.Marker => new StrokeAnnotation(Color.FromArgb(110, _color), Math.Max(_penWidth * 4, 12), p),
        Tool.Line => new LineAnnotation(_color, _penWidth, p, arrow: false),
        Tool.Arrow => new LineAnnotation(_color, _penWidth, p, arrow: true),
        Tool.Rectangle => new RectAnnotation(_color, _penWidth, p),
        Tool.Redact => new RedactAnnotation(_redactStyle, _shot, p),
        _ => throw new InvalidOperationException($"Tool {_tool} does not create a drawn annotation."),
    };

    private void UpdateAnnotation(Point p)
    {
        bool shift = (ModifierKeys & Keys.Shift) != 0;
        switch (_current)
        {
            case StrokeAnnotation stroke:
                stroke.Add(p);
                break;
            case LineAnnotation line:
                line.End = shift ? Shapes.Snap45(line.Start, p) : p;
                break;
            case RectAnnotation rect:
                rect.End = shift ? Shapes.SnapSquare(rect.Start, p) : p;
                break;
            case RedactAnnotation redact:
                redact.End = p;
                break;
        }
    }

    private void Undo()
    {
        if (_annotations.Count == 0)
            return;
        var last = _annotations[^1];
        _annotations.RemoveAt(_annotations.Count - 1);
        (last as IDisposable)?.Dispose();
        RefreshVisual();
    }

    private void BeginText(Point p)
    {
        var textBox = new TextBox
        {
            Multiline = true,
            WordWrap = false,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", TextPixelSize, FontStyle.Regular, GraphicsUnit.Pixel),
            ForeColor = _color,
            BackColor = _color.GetBrightness() > 0.6f ? Color.FromArgb(45, 45, 45) : Color.White,
            Location = p,
        };
        textBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                e.SuppressKeyPress = true;
                // Defer: the text box must not be disposed inside its own event handler.
                BeginInvoke(new Action(CommitText));
            }
        };
        textBox.TextChanged += (_, _) => FitTextBox(textBox);

        Controls.Add(textBox);
        FitTextBox(textBox);
        textBox.Focus();
        _textBox = textBox;
    }

    private void FitTextBox(TextBox textBox)
    {
        // Measure with one extra character so the caret always has room.
        var size = TextRenderer.MeasureText(textBox.Text + "W", textBox.Font, Size.Empty, TextFormatFlags.NoPadding);
        textBox.Size = new Size(size.Width + S(10), size.Height + S(8));
    }

    private void CommitText()
    {
        if (_textBox is not { } textBox)
            return;
        _textBox = null;

        var text = textBox.Text.TrimEnd();
        if (text.Length > 0)
        {
            int border = (textBox.Width - textBox.ClientSize.Width) / 2;
            var origin = textBox.GetPositionFromCharIndex(0);
            var location = new Point(textBox.Left + border + origin.X, textBox.Top + border + origin.Y);
            _annotations.Add(new TextAnnotation(_color, location, text, (Font)textBox.Font.Clone()));
        }

        RemoveTextBox(textBox);
    }

    private void CancelText()
    {
        if (_textBox is not { } textBox)
            return;
        _textBox = null;
        RemoveTextBox(textBox);
    }

    private void RemoveTextBox(TextBox textBox)
    {
        var font = textBox.Font;
        var bounds = textBox.Bounds;
        Controls.Remove(textBox);
        textBox.Dispose();
        font.Dispose();
        Focus();
        Invalidate(bounds);
        RefreshVisual();
    }

    // ---------------------------------------------------------------- commands

    private static Tool? ToolFor(ToolbarAction action) => action switch
    {
        ToolbarAction.Pencil => Tool.Pencil,
        ToolbarAction.Line => Tool.Line,
        ToolbarAction.Arrow => Tool.Arrow,
        ToolbarAction.Rectangle => Tool.Rectangle,
        ToolbarAction.Redact => Tool.Redact,
        ToolbarAction.Marker => Tool.Marker,
        ToolbarAction.Text => Tool.Text,
        _ => null,
    };

    private void OnToolbarButton(ToolbarAction action)
    {
        if (ToolFor(action) is { } tool)
        {
            SelectTool(tool);
            return;
        }

        switch (action)
        {
            case ToolbarAction.Color: PickColor(); break;
            case ToolbarAction.Undo: Undo(); break;
            case ToolbarAction.Upload: UploadAndClose(); break;
            case ToolbarAction.Print: PrintAndClose(); break;
            case ToolbarAction.Copy: CopyAndClose(); break;
            case ToolbarAction.Save: SaveAndClose(); break;
            case ToolbarAction.Close: Close(); break;
        }
    }

    private void SelectTool(Tool tool)
    {
        if (!HasSelection)
            return;
        CommitText();
        _tool = _tool == tool ? Tool.None : tool;
        RefreshVisual();
    }

    private void PickColor()
    {
        using var dialog = new ColorDialog { Color = _color, FullOpen = true, AnyColor = true };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        _color = dialog.Color;
        _settings.ColorArgb = _color.ToArgb();
        RefreshVisual();
    }

    private static string RedactStyleName(RedactStyle style) => style switch
    {
        RedactStyle.Frosted => Strings.RedactFrosted,
        RedactStyle.Pixelate => Strings.RedactPixelate,
        _ => Strings.RedactBlack,
    };

    private string RedactTooltip() => string.Format(Strings.ToolRedact, RedactStyleName(_redactStyle));

    private void OnRedactHold()
    {
        _holdTimer.Stop();
        if (!_redactPressed)
            return;
        _redactPressed = false;
        if (_buttons.FirstOrDefault(b => b.Action == ToolbarAction.Redact) is { } button)
            ShowRedactMenu(button);
    }

    private void ShowRedactMenu(ToolbarButton button)
    {
        Capture = false;
        // The style preview replaces the check mark; the checked item gets the highlighted image frame.
        int imageSize = S(20);
        var menu = new ContextMenuStrip
        {
            ShowImageMargin = true,
            ShowCheckMargin = false,
            ImageScalingSize = new Size(imageSize, imageSize),
        };
        foreach (var style in Enum.GetValues<RedactStyle>())
        {
            var item = new ToolStripMenuItem(RedactStyleName(style))
            {
                Image = CreateRedactImage(style, imageSize),
                Checked = style == _redactStyle,
            };
            if (item.Checked)
                item.Font = new Font(item.Font, FontStyle.Bold);
            item.Click += (_, _) => SetRedactStyle(style);
            menu.Items.Add(item);
        }
        menu.Closed += (_, _) => BeginInvoke(new Action(() =>
        {
            foreach (ToolStripItem item in menu.Items)
                item.Image?.Dispose();
            menu.Dispose();
        }));
        var b = button.Bounds;
        bool roomOnLeft = b.Left - _verticalBar.Width > 0;
        menu.Show(this, roomOnLeft ? new Point(_verticalBar.Left, b.Top) : new Point(_verticalBar.Right, b.Top),
            roomOnLeft ? ToolStripDropDownDirection.BelowLeft : ToolStripDropDownDirection.BelowRight);
    }

    private static Bitmap CreateRedactImage(RedactStyle style, int size)
    {
        var image = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(image);
        int inset = Math.Max(1, size / 10);
        var r = new Rectangle(inset, inset + size / 8, size - 2 * inset - 1, size - 2 * inset - size / 4 - 1);
        DrawRedactGlyph(g, style, r, Color.FromArgb(40, 40, 40));
        return image;
    }

    private void SetRedactStyle(RedactStyle style)
    {
        _redactStyle = style;
        _settings.RedactStyle = style;
        if (_buttons.FirstOrDefault(b => b.Action == ToolbarAction.Redact) is { } button)
            button.Tooltip = RedactTooltip();
        if (_tool != Tool.Redact)
            SelectTool(Tool.Redact);
        else
            RefreshVisual();
    }

    private void ResetSelection()
    {
        _selection = Rectangle.Empty;
        _tool = Tool.None;
        _hover = null;
        RefreshVisual();
    }

    private void SelectMonitorUnderCursor()
    {
        if (_annotations.Count > 0)
            return;
        _selection = Rectangle.Intersect(RectangleToClient(Screen.FromPoint(MousePosition).Bounds), ClientRectangle);
        RefreshVisual();
    }

    /// <summary>The selected region with all annotations burned in.</summary>
    private Bitmap RenderResult()
    {
        CommitText();
        var result = new Bitmap(_selection.Width, _selection.Height, PixelFormat.Format24bppRgb);
        result.SetResolution(96, 96);
        using var g = Graphics.FromImage(result);
        g.DrawImage(_shot, new Rectangle(Point.Empty, _selection.Size), _selection, GraphicsUnit.Pixel);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TranslateTransform(-_selection.X, -_selection.Y);
        foreach (var annotation in _annotations)
            annotation.Draw(g);
        return result;
    }

    private void CopyAndClose()
    {
        if (!HasSelection)
            return;
        using var image = RenderResult();
        _actions.CopyToClipboard(image);
        Close();
    }

    private void SaveAndClose()
    {
        if (!HasSelection)
            return;
        using var image = RenderResult();
        if (_actions.Save(image, this) != null)
            Close();
    }

    private void PrintAndClose()
    {
        if (!HasSelection)
            return;
        using var image = RenderResult();
        if (_actions.Print(image, this))
            Close();
    }

    private void UploadAndClose()
    {
        if (!HasSelection || !_settings.Upload.IsConfigured)
            return;
        EncodedImage payload;
        using (var image = RenderResult())
            payload = _actions.EncodeForUpload(image);
        Close();
        _ = _actions.UploadAsync(payload);
    }

    // ---------------------------------------------------------------- keyboard

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_textBox != null)
        {
            // Typing goes to the text box; only Esc is handled here (cancels the text, not the capture).
            if (e.KeyCode == Keys.Escape)
            {
                CancelText();
                e.Handled = e.SuppressKeyPress = true;
            }
            return;
        }

        e.Handled = true;
        switch (e.KeyData)
        {
            case Keys.Escape: Close(); break;
            case Keys.Enter:
            case Keys.Control | Keys.C: CopyAndClose(); break;
            case Keys.Control | Keys.S: SaveAndClose(); break;
            case Keys.Control | Keys.P: PrintAndClose(); break;
            case Keys.Control | Keys.U: UploadAndClose(); break;
            case Keys.Control | Keys.Z: Undo(); break;
            case Keys.Control | Keys.A: SelectMonitorUnderCursor(); break;
            case Keys.P: SelectTool(Tool.Pencil); break;
            case Keys.L: SelectTool(Tool.Line); break;
            case Keys.A: SelectTool(Tool.Arrow); break;
            case Keys.R: SelectTool(Tool.Rectangle); break;
            case Keys.B: SelectTool(Tool.Redact); break;
            case Keys.M: SelectTool(Tool.Marker); break;
            case Keys.T: SelectTool(Tool.Text); break;
            default: e.Handled = false; break;
        }
        if (e.Handled)
            e.SuppressKeyPress = true;
        base.OnKeyDown(e);
    }
}
