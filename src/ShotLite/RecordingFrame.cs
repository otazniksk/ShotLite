using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ShotLite;

/// <summary>
/// The on-screen frame around the recorded area with the RECORDING sign and the time/stop bar. It covers one
/// monitor but its region is only those shapes, so the area stays clickable; Windows keeps it out of the capture.
/// </summary>
internal sealed class RecordingFrame : Form
{
    private const int RingWidth = 2;
    private const float MaxSignWidth = 0.9f;
    private static readonly Color Recording = Color.FromArgb(229, 57, 53);
    private static readonly Color Waiting = Color.FromArgb(0, 120, 215);
    private static readonly Color BarBackground = Color.FromArgb(32, 32, 32);

    private readonly Rectangle _monitor, _area, _bar, _stop;
    private readonly GraphicsPath _sign = new();
    private readonly int _dpi;
    private readonly string _limit;
    private readonly Font _timeFont;
    private bool _signVisible;
    private bool _live;
    private string _time = "00:00";

    /// <param name="limit">Text after the time, e.g. " / 01:00"; empty for no limit.</param>
    public RecordingFrame(RecordingArea area, string limit)
    {
        _monitor = area.MonitorBounds;
        _limit = limit;
        _dpi = Native.GetDpiForMonitor(area.Monitor, Native.MDT_EFFECTIVE_DPI, out uint dpi, out _) == 0 ? (int)dpi : 96;
        _area = area.Bounds;
        _area.Offset(-_monitor.X, -_monitor.Y);

        _timeFont = new Font("Segoe UI", S(14), FontStyle.Regular, GraphicsUnit.Pixel);
        _bar = PlaceBar();
        int stop = _bar.Height - S(8);
        _stop = new Rectangle(_bar.Right - stop - S(4), _bar.Top + S(4), stop, stop);
        CreateSign();

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        Bounds = _monitor;
        ShowInTaskbar = false;
        Text = Strings.AppName;
        BackColor = BarBackground;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        UpdateShape();
    }

    /// <summary>The user pressed the stop button.</summary>
    public event Action? StopClicked;

    // Clicking stop must not take the focus away from the recorded application.
    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            // In the style rather than TopMost = true, which would activate the window.
            parameters.ExStyle |= Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
            return parameters;
        }
    }

    public void ShowSign(bool visible)
    {
        _signVisible = visible;
        UpdateShape();
        Invalidate();
    }

    /// <summary>Turns red only with the first real frame: starting the encoder takes a moment.</summary>
    public void ShowTime(TimeSpan elapsed)
    {
        if (!_live && elapsed > TimeSpan.Zero)
        {
            _live = true;
            Invalidate();
        }
        var time = $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";
        if (time == _time)
            return;
        _time = time;
        Invalidate(_bar);
    }

    private int S(int value) => (int)Math.Round(value * _dpi / 96.0);

    /// <summary>Below the area, else above it, else inside it (a whole monitor has no room around it).</summary>
    private Rectangle PlaceBar()
    {
        var text = TextRenderer.MeasureText("00:00" + _limit, _timeFont);
        var size = new Size(text.Width + S(58), S(36));
        int gap = S(8);
        int y = _area.Bottom + gap;
        if (y + size.Height > _monitor.Height)
            y = _area.Top - gap - size.Height;
        if (y < 0)
            y = _area.Bottom - gap * 2 - size.Height;
        int x = Math.Clamp(_area.Left + (_area.Width - size.Width) / 2, 0, Math.Max(0, _monitor.Width - size.Width));
        return new Rectangle(new Point(x, y), size);
    }

    /// <summary>One line in the middle of the area, shrunk to fit when the area is narrower than the text.</summary>
    private void CreateSign()
    {
        using var family = new FontFamily("Segoe UI");
        using var centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        var center = new PointF(_area.Left + _area.Width / 2f, _area.Top + _area.Height / 2f);
        // From a point, not a rectangle: a rectangle narrower than the text would cut it off.
        _sign.AddString("RECORDING", family, (int)FontStyle.Bold, S(72), center, centered);

        float room = _area.Width * MaxSignWidth, width = _sign.GetBounds().Width;
        if (width <= room)
            return;
        using var shrink = new Matrix();
        shrink.Translate(center.X, center.Y);
        shrink.Scale(room / width, room / width);
        shrink.Translate(-center.X, -center.Y);
        _sign.Transform(shrink);
    }

    private void UpdateShape()
    {
        using var path = new GraphicsPath();
        // Alternate filling turns the inner rectangle into a hole, which leaves just the ring.
        path.FillMode = FillMode.Alternate;
        path.AddRectangle(Rectangle.Inflate(_area, RingWidth, RingWidth));
        path.AddRectangle(_area);
        var region = new Region(path);
        using (var bar = Shapes.RoundedRect(_bar, S(8)))
            region.Union(bar);
        if (_signVisible)
            region.Union(_sign);
        Region = region; // The setter disposes the previous one.
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.SetWindowDisplayAffinity(Handle, Native.WDA_EXCLUDEFROMCAPTURE);
    }

    protected override void WndProc(ref Message m)
    {
        // Same as the overlay: stay on the monitor's exact physical rectangle whatever its DPI is.
        switch (m.Msg)
        {
            case Native.WM_DPICHANGED:
                m.Result = IntPtr.Zero;
                return;
            case Native.WM_WINDOWPOSCHANGING when m.LParam != IntPtr.Zero:
                var pos = Marshal.PtrToStructure<Native.WINDOWPOS>(m.LParam);
                pos.x = _monitor.X;
                pos.y = _monitor.Y;
                pos.cx = _monitor.Width;
                pos.cy = _monitor.Height;
                pos.flags &= ~(Native.SWP_NOMOVE | Native.SWP_NOSIZE);
                Marshal.StructureToPtr(pos, m.LParam, fDeleteOld: false);
                break;
        }
        base.WndProc(ref m);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var ring = new SolidBrush(_live ? Recording : Waiting))
            g.FillRectangle(ring, Rectangle.Inflate(_area, RingWidth, RingWidth));

        if (_signVisible)
        {
            using var red = new SolidBrush(Recording);
            g.FillPath(red, _sign);
        }

        using (var background = new SolidBrush(BarBackground))
            g.FillRectangle(background, _bar);
        int dot = S(12);
        using (var status = new SolidBrush(_live ? Recording : Waiting))
            g.FillEllipse(status, _bar.Left + S(12), _bar.Top + (_bar.Height - dot) / 2, dot, dot);
        var text = Rectangle.FromLTRB(_bar.Left + S(30), _bar.Top, _stop.Left, _bar.Bottom);
        TextRenderer.DrawText(g, _time + _limit, _timeFont, text, Color.White,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        using (var button = Shapes.RoundedRect(_stop, S(4)))
        using (var fill = new SolidBrush(Color.FromArgb(70, 70, 70)))
            g.FillPath(fill, button);
        int square = S(12);
        using var white = new SolidBrush(Color.White);
        g.FillRectangle(white, _stop.Left + (_stop.Width - square) / 2, _stop.Top + (_stop.Height - square) / 2, square, square);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Cursor = _stop.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && _stop.Contains(e.Location))
            StopClicked?.Invoke();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timeFont.Dispose();
            _sign.Dispose();
            Region?.Dispose();
        }
        base.Dispose(disposing);
    }
}
