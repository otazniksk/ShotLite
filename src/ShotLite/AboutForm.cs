using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ShotLite;

/// <summary>About dialog: gradient header in the app icon's colours, the logo, version and a short description.</summary>
internal sealed class AboutForm : Form
{
    private const string RepoUrl = "https://github.com/otazniksk/ShotLite";
    private static readonly Color LinkBlue = Color.FromArgb(37, 99, 235);

    private readonly Bitmap _logo;

    public AboutForm(Icon windowIcon)
    {
        Text = Strings.MenuAbout;
        Icon = windowIcon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        // The window grows with its content, so larger fonts or DPI never clip the OK button.
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = SystemColors.Window;
        Font = new Font("Segoe UI", 9F);

        // The 256 px frame scaled down stays sharp at any DPI.
        using (var icon = AppIcon.Load(new Size(256, 256)))
            _logo = icon.ToBitmap();

        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        AddRow(layout, new HeaderPanel(_logo) { Size = new Size(380, 150), Margin = Padding.Empty });
        AddRow(layout, CenteredLabel(string.Format(Strings.AboutVersion, VersionText()), 11F, FontStyle.Bold,
            SystemColors.ControlText, new Padding(24, 18, 24, 6)));
        AddRow(layout, CenteredLabel(Strings.AboutTagline, 9.5F, FontStyle.Regular,
            SystemColors.ControlText, new Padding(24, 4, 24, 4)));
        AddRow(layout, Link(RepoUrl.Replace("https://", ""), RepoUrl, new Padding(24, 10, 24, 0)));
        AddRow(layout, Link(Strings.AboutLicense, RepoUrl + "/blob/main/LICENSE", new Padding(24, 2, 24, 0)));
        AddRow(layout, CenteredLabel(RuntimeText(), 8.5F, FontStyle.Regular,
            SystemColors.GrayText, new Padding(24, 10, 24, 4)));

        var ok = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            MinimumSize = new Size(96, 30),
            Anchor = AnchorStyles.None,
            Margin = new Padding(0, 14, 0, 20),
        };
        AddRow(layout, ok);

        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = ok;
    }

    /// <summary>
    /// The informational version stamped at build time from git (see ShotLite.csproj):
    /// "1.2.0" on a release tag, "0.1.0-main+a1b2c3d" on a branch; the plain version without git.
    /// </summary>
    private static string VersionText()
    {
        var assembly = typeof(AboutForm).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
            return informational;
        return (assembly.GetName().Version ?? new Version(0, 0, 0)).ToString(3);
    }

    private static string RuntimeText() =>
        $".NET {Environment.Version} · {RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()} · {Localization.CurrentCode}";

    private static Label CenteredLabel(string text, float size, FontStyle style, Color color, Padding margin) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(320, 0),
        TextAlign = ContentAlignment.MiddleCenter,
        Anchor = AnchorStyles.None,
        Font = new Font("Segoe UI", size, style),
        ForeColor = color,
        Margin = margin,
    };

    private static LinkLabel Link(string text, string url, Padding margin)
    {
        var link = new LinkLabel
        {
            Text = text,
            AutoSize = true,
            Anchor = AnchorStyles.None,
            Font = new Font("Segoe UI", 9F),
            LinkColor = LinkBlue,
            ActiveLinkColor = LinkBlue,
            VisitedLinkColor = LinkBlue,
            LinkBehavior = LinkBehavior.HoverUnderline,
            Margin = margin,
        };
        link.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        return link;
    }

    private static void AddRow(TableLayoutPanel table, Control control)
    {
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(control);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _logo.Dispose();
        base.Dispose(disposing);
    }

    private sealed class HeaderPanel : Panel
    {
        // Same gradient as the app icon (design/draw_icon.py).
        private static readonly Color GradientStart = Color.FromArgb(56, 132, 255);
        private static readonly Color GradientEnd = Color.FromArgb(139, 61, 255);

        private readonly Bitmap _logo;

        public HeaderPanel(Bitmap logo)
        {
            _logo = logo;
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

            int logo = r.Height * 46 / 100;
            var logoBounds = new Rectangle(r.Left + (r.Width - logo) / 2, r.Top + r.Height * 11 / 100, logo, logo);
            using (var shadow = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
                g.FillEllipse(shadow, logoBounds.Left + logo / 10, logoBounds.Bottom - logo / 12, logo * 8 / 10, logo / 6);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(_logo, logoBounds);

            using var font = new Font("Segoe UI Semibold", 17F);
            var textBounds = new Rectangle(r.Left, logoBounds.Bottom + r.Height * 4 / 100, r.Width, r.Bottom - logoBounds.Bottom);
            TextRenderer.DrawText(g, Strings.AppName, font, textBounds, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding);
        }
    }
}
