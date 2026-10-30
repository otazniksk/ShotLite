using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ShotLite;

/// <summary>About dialog: gradient header in the app icon's colours, the logo, version and a short description.</summary>
internal sealed class AboutForm : Form
{
    public static string RepoUrl => BuildInfo.RepositoryUrl;
    private static readonly Color LinkBlue = Color.FromArgb(37, 99, 235);
    private static readonly (string Name, string Url)[] Testers =
    [
        ("Arduinak", "https://github.com/Arduinak"),
        ("Spacesov", "https://github.com/Spacesov"),
    ];

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

        AddRow(layout, new BrandHeader(_logo) { Size = new Size(380, 150), Margin = Padding.Empty });
        AddRow(layout, CenteredLabel(string.Format(Strings.AboutVersion, VersionText()), 11F, FontStyle.Bold,
            SystemColors.ControlText, new Padding(24, 18, 24, 6)));
        AddRow(layout, CenteredLabel(Strings.AboutTagline, 9.5F, FontStyle.Regular,
            SystemColors.ControlText, new Padding(24, 4, 24, 4)));
        AddRow(layout, Link(RepoUrl.Replace("https://", ""), RepoUrl, new Padding(24, 10, 24, 0)));
        AddRow(layout, Link(Strings.AboutLicense, RepoUrl + "/blob/main/LICENSE", new Padding(24, 2, 24, 0)));
        AddRow(layout, TestersLabel());
        AddRow(layout, CenteredLabel(RuntimeText(), 8.5F, FontStyle.Regular,
            SystemColors.GrayText, new Padding(24, 10, 24, 4)));

        var ok = new Button
        {
            Text = "OK",
            AutoSize = true,
            MinimumSize = new Size(96, 30),
            Anchor = AnchorStyles.None,
            Margin = new Padding(0, 14, 0, 20),
        };
        // The dialog is modeless (Show, not ShowDialog), so DialogResult would not close it.
        ok.Click += (_, _) => Close();
        AddRow(layout, ok);

        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = ok;
    }

    /// <summary>
    /// The informational version stamped at build time from git (see ShotLite.csproj):
    /// "1.2.0" on a release tag, "0.1.0-main+a1b2c3d" on a branch; the plain version without git.
    /// </summary>
    public static string VersionText()
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

    private static LinkLabel TestersLabel()
    {
        var label = new LinkLabel
        {
            Text = string.Format(Strings.AboutTesters, string.Join(", ", Testers.Select(t => t.Name))),
            AutoSize = true,
            Anchor = AnchorStyles.None,
            Font = new Font("Segoe UI", 9F),
            LinkColor = LinkBlue,
            ActiveLinkColor = LinkBlue,
            VisitedLinkColor = LinkBlue,
            LinkBehavior = LinkBehavior.HoverUnderline,
            Margin = new Padding(24, 8, 24, 0),
        };
        label.Links.Clear();
        foreach (var (name, url) in Testers)
            label.Links.Add(label.Text.IndexOf(name, StringComparison.Ordinal), name.Length, url);
        label.LinkClicked += (_, e) => Process.Start(new ProcessStartInfo((string)e.Link!.LinkData!) { UseShellExecute = true });
        return label;
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
}
