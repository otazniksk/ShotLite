using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ShotLite;

namespace DocShots;

/// <summary>
/// Renders the wiki images from ShotLite's real UI over fictional HTML scenes (rendered by headless Edge).
/// Usage (from the repository root, on Windows or WSL with the Windows .NET SDK):
///   dotnet run -c Release --project tools/DocShots -p:GitVersion=false -p:Version=&lt;latest release&gt; -- --out &lt;folder&gt;
/// overview.gif, black-out.gif and text.gif go to docs/ for the README; all of them go to images/ in the wiki repository.
/// GitVersion=false and Version give the settings header a clean version instead of the branch build label.
/// Everything is drawn in code at 100 % scaling, so the images look the same every time.
/// </summary>
internal static class Program
{
    private static readonly Size SceneSize = new(1280, 800);

    [STAThread]
    private static int Main(string[] args)
    {
        var output = Path.GetFullPath(ArgValue(args, "--out") ?? "docshots-out");
        Directory.CreateDirectory(output);

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Localization.Apply("en-US"); // the wiki is in English

        using var article = RenderScene("article.html", SceneSize);
        // GIFs are at most 800 px wide (GifEncoder.MaxWidth): a scene of that size isn't scaled down.
        using var articleNarrow = RenderScene("article.html", new Size(GifEncoder.MaxWidth, 540));
        using var articleTall = RenderScene("article.html", new Size(GifEncoder.MaxWidth, 600));
        using var accountNarrow = RenderScene("account.html", new Size(GifEncoder.MaxWidth, 600));
        var shots = new Shots(output);

        shots.Tools(article);
        shots.SelectArrowCopyGif(articleNarrow);
        shots.BlackOutGif(accountNarrow);
        shots.OverviewGif(articleTall);
        shots.TextGif(articleTall);
        shots.AspectGif(articleTall);
        shots.SettingsTabs();

        Console.WriteLine($"Done: {output}");
        return 0;
    }

    private static string? ArgValue(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    /// <summary>A scene from scenes\*.html as a screenshot of a screen of the given size, rendered by headless Edge.</summary>
    private static Bitmap RenderScene(string name, Size size)
    {
        var html = Path.Combine(AppContext.BaseDirectory, "scenes", name);
        var png = Path.Combine(Path.GetTempPath(), $"docshots-{Path.GetFileNameWithoutExtension(name)}-{size.Width}x{size.Height}.png");
        File.Delete(png);

        var edge = new[]
        {
            Environment.ExpandEnvironmentVariables(@"%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe"),
            Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\Microsoft\Edge\Application\msedge.exe"),
        }.FirstOrDefault(File.Exists) ?? throw new FileNotFoundException("Microsoft Edge not found.");

        // Headless Edge sometimes hangs at start: a time limit, a few attempts and an own profile each time.
        for (int attempt = 1; attempt <= 3 && !File.Exists(png); attempt++)
        {
            var profile = Path.Combine(Path.GetTempPath(), $"docshots-edge-{Guid.NewGuid():N}");
            var start = new ProcessStartInfo(edge) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in new[]
                     {
                         "--headless=new", "--disable-gpu", "--hide-scrollbars", "--force-device-scale-factor=1",
                         "--no-first-run", "--disable-extensions", $"--user-data-dir={profile}",
                         $"--window-size={size.Width},{size.Height}", $"--screenshot={png}",
                         new Uri(html).AbsoluteUri,
                     })
                start.ArgumentList.Add(arg);
            using (var process = Process.Start(start)!)
            {
                if (!process.WaitForExit(TimeSpan.FromSeconds(45)))
                {
                    Console.WriteLine($"  Edge hung on {name}, attempt {attempt}");
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit();
                }
            }
            try { Directory.Delete(profile, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        if (!File.Exists(png))
            throw new InvalidOperationException($"Edge did not render {name}.");

        using var loaded = new Bitmap(png);
        var scene = new Bitmap(loaded); // detached from the file
        scene.SetResolution(96, 96);
        return scene;
    }
}

/// <summary>The individual wiki images.</summary>
internal sealed class Shots
{
    private static readonly Color Red = Color.FromArgb(229, 57, 53);
    private static readonly Color Yellow = Color.FromArgb(255, 214, 0);

    private readonly string _output;

    public Shots(string output) => _output = output;

    /// <summary>The editor with every drawing tool used once and the Arrow tooltip.</summary>
    public void Tools(Bitmap scene)
    {
        using var form = Overlay(scene);
        var selection = Rectangle.FromLTRB(64, 150, 900, 630);

        var arrow = new LineAnnotation(Red, 4, new Point(744, 196), arrow: true) { End = new Point(612, 226) };
        var frame = new RectAnnotation(Red, 4, new Point(82, 288)) { End = new Point(866, 494) };
        var marker = Stroke(Color.FromArgb(110, Yellow), 18, Line(new Point(90, 586), new Point(470, 586)));
        var pencil = Stroke(Red, 3, Ellipse(new Rectangle(176, 252, 104, 26)));
        var text = new TextAnnotation(Red, new Point(704, 160), "Point at it", new Font("Segoe UI", 22, FontStyle.Bold, GraphicsUnit.Pixel));

        form.DocsSetState(selection, [frame, arrow, marker, pencil, text], Tool.Arrow, hover: ToolbarAction.Arrow);
        using var image = form.DocsRender();
        Save(image, "editor-tools.png");
    }

    /// <summary>PrtScr → drag a selection → draw an arrow → copy, with a visible mouse cursor.</summary>
    public void SelectArrowCopyGif(Bitmap scene)
    {
        using var form = Overlay(scene);
        var start = new Point(64, 150);
        var end = new Point(736, 476);
        var arrowFrom = new Point(708, 184);
        var arrowTo = new Point(606, 226);

        using var gif = new GifWriter(Path.Combine(_output, "select-arrow-copy.gif"), new Rectangle(Point.Empty, scene.Size));

        // Nothing selected yet: the hint, the cursor moves to the start corner.
        for (int i = 0; i <= 8; i++)
        {
            form.DocsSetState(Rectangle.Empty, []);
            gif.Add(form, Lerp(new Point(360, 400), start, i / 8f), Cursors.Cross);
        }
        // Dragging out the selection.
        for (int i = 1; i <= 12; i++)
        {
            var corner = Lerp(start, end, Ease(i / 12f));
            form.DocsSetState(Rectangle.FromLTRB(start.X, start.Y, corner.X, corner.Y), [], creating: true);
            gif.Add(form, corner, Cursors.Cross);
        }
        var selection = Rectangle.FromLTRB(start.X, start.Y, end.X, end.Y);

        // Toolbars appear; the cursor goes to the Arrow tool.
        form.DocsSetState(selection, []);
        var arrowButton = Center(form.DocsButtonBounds(ToolbarAction.Arrow));
        for (int i = 0; i <= 8; i++)
            gif.Add(form, Lerp(end, arrowButton, Ease(i / 8f)), Cursors.Default);
        form.DocsSetState(selection, [], Tool.Arrow, hover: ToolbarAction.Arrow);
        gif.Add(form, arrowButton, Cursors.Default, frames: 6);

        // Drawing the arrow.
        for (int i = 0; i <= 8; i++)
        {
            form.DocsSetState(selection, [], Tool.Arrow);
            gif.Add(form, Lerp(arrowButton, arrowFrom, Ease(i / 8f)), Cursors.Cross);
        }
        LineAnnotation? arrow = null;
        for (int i = 1; i <= 10; i++)
        {
            var tip = Lerp(arrowFrom, arrowTo, Ease(i / 10f));
            arrow = new LineAnnotation(Red, 4, arrowFrom, arrow: true) { End = tip };
            form.DocsSetState(selection, [arrow], Tool.Arrow);
            gif.Add(form, tip, Cursors.Cross);
        }

        // Copy.
        var copyButton = Center(form.DocsButtonBounds(ToolbarAction.Copy));
        for (int i = 0; i <= 8; i++)
        {
            form.DocsSetState(selection, [arrow!], Tool.Arrow);
            gif.Add(form, Lerp(arrowTo, copyButton, Ease(i / 8f)), Cursors.Default);
        }
        form.DocsSetState(selection, [arrow!], Tool.Arrow, hover: ToolbarAction.Copy);
        gif.Add(form, copyButton, Cursors.Default, frames: 25);
        Console.WriteLine("  select-arrow-copy.gif");
    }

    /// <summary>
    /// Select a form → Black out tool → the three styles, one per secret: black for the password,
    /// then (held button → menu) frosted glass for the API key and pixelate for the card number.
    /// </summary>
    public void BlackOutGif(Bitmap scene)
    {
        using var form = Overlay(scene);
        var start = new Point(190, 140);
        var end = new Point(706, 540);
        var selection = Rectangle.FromLTRB(start.X, start.Y, end.X, end.Y);
        var secrets = new (Rectangle Area, RedactStyle Style)[]
        {
            (Rectangle.FromLTRB(350, 260, 512, 286), RedactStyle.Black),    // password
            (Rectangle.FromLTRB(350, 308, 602, 334), RedactStyle.Frosted),  // API key
            (Rectangle.FromLTRB(350, 450, 526, 476), RedactStyle.Pixelate), // card number
        };

        using var gif = new GifWriter(Path.Combine(_output, "black-out.gif"), new Rectangle(Point.Empty, scene.Size));
        var cursor = SelectArea(form, gif, new Point(420, 420), start, end);
        var done = new List<Annotation>();

        // The Black out tool.
        var button = Center(form.DocsButtonBounds(ToolbarAction.Redact));
        cursor = Move(gif, form, cursor, button, Cursors.Default, () => form.DocsSetState(selection, done));
        form.DocsSetState(selection, done, Tool.Redact, hover: ToolbarAction.Redact);
        gif.Add(form, cursor, Cursors.Default, frames: 12);

        foreach (var (area, style) in secrets)
        {
            if (style != RedactStyle.Black)
                cursor = ChooseRedactStyle(form, gif, selection, done, cursor, style);
            cursor = DragRedact(form, gif, selection, done, style, cursor, area);
        }
        form.DocsSetState(selection, done, Tool.Redact);
        gif.Add(form, cursor, Cursors.Cross, frames: 30);
        Console.WriteLine("  black-out.gif");
    }

    /// <summary>
    /// The README's first image, ShotLite at a glance: select → arrow → highlighter → black out a name →
    /// a note → copy.
    /// </summary>
    public void OverviewGif(Bitmap scene)
    {
        using var form = Overlay(scene);
        var start = new Point(60, 150);
        var end = new Point(736, 548);
        var selection = Rectangle.FromLTRB(start.X, start.Y, end.X, end.Y);
        var done = new List<Annotation>();

        using var gif = new GifWriter(Path.Combine(_output, "overview.gif"), new Rectangle(Point.Empty, scene.Size));
        var cursor = SelectArea(form, gif, new Point(400, 420), start, end);

        // An arrow at the headline.
        cursor = PickTool(form, gif, selection, done, cursor, ToolbarAction.Arrow, Tool.Arrow);
        cursor = Drag(form, gif, selection, done, Tool.Arrow, cursor, new Point(700, 182), new Point(608, 222),
            (from, to) => new LineAnnotation(Red, 4, from, arrow: true) { End = to });

        // The first sentence highlighted.
        cursor = PickTool(form, gif, selection, done, cursor, ToolbarAction.Marker, Tool.Marker);
        cursor = Drag(form, gif, selection, done, Tool.Marker, cursor, new Point(90, 521), new Point(458, 521),
            (from, to) => Stroke(Color.FromArgb(110, Yellow), 16, Line(from, to)), frames: 10);

        // The author's name blacked out.
        cursor = PickTool(form, gif, selection, done, cursor, ToolbarAction.Redact, Tool.Redact);
        cursor = DragRedact(form, gif, selection, done, RedactStyle.Black, cursor, Rectangle.FromLTRB(86, 254, 182, 275));

        // A note next to it.
        cursor = PickTool(form, gif, selection, done, cursor, ToolbarAction.Text, Tool.Text);
        cursor = TypeText(form, gif, selection, done, cursor, new Point(398, 264), "\u2190 name hidden", 22);

        // Copy.
        var copy = Center(form.DocsButtonBounds(ToolbarAction.Copy));
        cursor = Move(gif, form, cursor, copy, Cursors.Default, () => form.DocsSetState(selection, done, Tool.Text));
        form.DocsSetState(selection, done, Tool.Text, hover: ToolbarAction.Copy);
        gif.Add(form, cursor, Cursors.Default, frames: 30);
        Console.WriteLine("  overview.gif");
    }

    /// <summary>Text: hold T for the size menu, pick 32 px, click and type.</summary>
    public void TextGif(Bitmap scene)
    {
        using var form = Overlay(scene);
        var start = new Point(60, 150);
        var end = new Point(736, 500);
        var selection = Rectangle.FromLTRB(start.X, start.Y, end.X, end.Y);
        var done = new List<Annotation>();

        using var gif = new GifWriter(Path.Combine(_output, "text.gif"), new Rectangle(Point.Empty, scene.Size));
        var cursor = SelectArea(form, gif, new Point(400, 420), start, end);

        int index = Array.IndexOf(TextSizes, 32);
        cursor = ChooseFromMenu(form, gif, ToolbarAction.Text, index, cursor,
            setState: hover => form.DocsSetState(selection, done, Tool.Text, hover: hover ? ToolbarAction.Text : null),
            choose: () => form.DocsSetOptions(textSize: 32));
        cursor = TypeText(form, gif, selection, done, cursor, new Point(176, 184), "New in March!", 32);
        form.DocsSetState(selection, done, Tool.Text);
        gif.Add(form, cursor, Cursors.IBeam, frames: 30);
        Console.WriteLine("  text.gif");
    }

    /// <summary>
    /// Aspect ratio: hold the ratio button, pick 16:9 (the selection takes the shape, keeping its height and center),
    /// then hold it again and pick Full size (the whole monitor).
    /// </summary>
    public void AspectGif(Bitmap scene)
    {
        using var form = Overlay(scene);
        var start = new Point(150, 170);
        var end = new Point(520, 470);
        var selection = Rectangle.FromLTRB(start.X, start.Y, end.X, end.Y);
        var done = new List<Annotation>();

        using var gif = new GifWriter(Path.Combine(_output, "aspect-ratio.gif"), new Rectangle(Point.Empty, scene.Size));
        var cursor = SelectArea(form, gif, new Point(400, 420), start, end);

        // 16:9 (menu: Free, 9:16, 4:3, 16:9, separator, Full size).
        var wide = new AspectRatio(16, 9);
        cursor = ChooseFromMenu(form, gif, ToolbarAction.Aspect, 3, cursor,
            setState: hover => form.DocsSetState(selection, done, hover: hover ? ToolbarAction.Aspect : null),
            choose: () => form.DocsSetOptions(aspect: wide));
        // As SetAspect: whole steps of the ratio, the height kept, the center kept.
        int steps = selection.Height / wide.StepHeight;
        int width = steps * wide.StepWidth, height = steps * wide.StepHeight;
        var shaped = new Rectangle(selection.Left + selection.Width / 2 - width / 2, selection.Top + selection.Height / 2 - height / 2, width, height);
        selection = Morph(form, gif, cursor, selection, shaped, done);

        // Full size.
        cursor = ChooseFromMenu(form, gif, ToolbarAction.Aspect, 5, cursor,
            setState: hover => form.DocsSetState(selection, done, hover: hover ? ToolbarAction.Aspect : null),
            choose: () => form.DocsSetOptions(freeAspect: true));
        selection = Morph(form, gif, cursor, selection, new Rectangle(Point.Empty, scene.Size), done);
        gif.Add(form, cursor, Cursors.Default, frames: 25);
        Console.WriteLine("  aspect-ratio.gif");
    }

    /// <summary>The selection changes shape over a few frames, the cursor stays.</summary>
    private static Rectangle Morph(CaptureForm form, GifWriter gif, Point cursor, Rectangle from, Rectangle to, List<Annotation> done)
    {
        for (int i = 1; i <= 6; i++)
        {
            float t = Ease(i / 6f);
            var a = Lerp(from.Location, to.Location, t);
            var b = Lerp(new Point(from.Right, from.Bottom), new Point(to.Right, to.Bottom), t);
            form.DocsSetState(Rectangle.FromLTRB(a.X, a.Y, b.X, b.Y), done);
            gif.Add(form, cursor, Cursors.Default);
        }
        gif.Add(form, cursor, Cursors.Default, frames: 10);
        return to;
    }

    /// <summary>Every tab of the settings window with default settings (nothing from the PC it runs on).</summary>
    public void SettingsTabs()
    {
        using var icon = AppIcon.Load(SystemInformation.IconSize);
        // The folders of a made-up user, not the real paths of the PC it runs on.
        var settings = new AppSettings { SaveFolder = @"C:\Users\Example\Pictures\ShotLite", RecordFolder = @"C:\Users\Example\Videos\ShotLite" };
        using var form = new SettingsForm(settings, icon) { StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -4000) };
        form.Show();
        Application.DoEvents();
        var tabs = Descendants(form).OfType<TabControl>().Single();
        // By title: the Video tab is missing where Windows can't record.
        var names = new Dictionary<string, string>
        {
            [Strings.SectionGeneral] = "general",
            [Strings.SectionHotkeys] = "shortcuts",
            [Strings.TabScreenshots] = "screenshots",
            [Strings.TabVideo] = "video",
            [Strings.TabServer] = "server",
        };
        for (int i = 0; i < tabs.TabCount; i++)
        {
            tabs.SelectedIndex = i;
            Application.DoEvents();
            using var window = new Bitmap(form.Width, form.Height, PixelFormat.Format24bppRgb);
            form.DrawToBitmap(window, new Rectangle(Point.Empty, form.Size));
            // Only the client area: DrawToBitmap paints the title bar in an old style that doesn't match Windows 11.
            var origin = form.PointToScreen(Point.Empty);
            var client = new Rectangle(origin.X - form.Left, origin.Y - form.Top, form.ClientSize.Width, form.ClientSize.Height);
            using var image = window.Clone(client, PixelFormat.Format24bppRgb);
            Save(image, $"settings-{names[tabs.TabPages[i].Text]}.png");
        }
        form.Close();
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var grandchild in Descendants(child))
                yield return grandchild;
        }
    }

    /// <summary>The sizes in the Text button's menu (CaptureForm.TextSizes).</summary>
    private static readonly int[] TextSizes = { 10, 12, 14, 16, 20, 24, 32, 48, 64 };

    /// <summary>Holds the Black out button, the style menu opens, the cursor picks <paramref name="style"/>.</summary>
    private static Point ChooseRedactStyle(CaptureForm form, GifWriter gif, Rectangle selection, List<Annotation> done, Point cursor, RedactStyle style)
    {
        int index = Array.IndexOf(Enum.GetValues<RedactStyle>(), style);
        return ChooseFromMenu(form, gif, ToolbarAction.Redact, index, cursor,
            setState: hover => form.DocsSetState(selection, done, Tool.Redact, hover: hover ? ToolbarAction.Redact : null),
            choose: () => form.DocsSetOptions(redactStyle: style));
    }

    /// <summary>
    /// Holds a toolbar button, its menu opens (as ShowSideMenu places it), the cursor picks item <paramref name="index"/>.
    /// <paramref name="setState"/> prepares the overlay, with or without the button hovered.
    /// </summary>
    private static Point ChooseFromMenu(CaptureForm form, GifWriter gif, ToolbarAction action, int index, Point cursor,
        Action<bool> setState, Action choose)
    {
        var buttonBounds = form.DocsButtonBounds(action);
        cursor = Move(gif, form, cursor, Center(buttonBounds), Cursors.Default, () => setState(false));
        setState(true);
        gif.Add(form, cursor, Cursors.Default, frames: 4); // holding

        using (var menu = form.DocsMenu(action))
        {
            using var closed = RenderMenu(menu, selected: null);
            Point at;
            var actionBar = form.DocsActionBar;
            if (actionBar.Contains(Center(buttonBounds)))
            {
                // ShowBarMenu: under the action bar, or above it where there's no room.
                bool roomBelow = actionBar.Bottom + closed.Height < form.ClientSize.Height;
                at = new Point(buttonBounds.Left, roomBelow ? actionBar.Bottom : actionBar.Top - closed.Height);
            }
            else
            {
                // ShowSideMenu; Windows opens the menu upwards when it doesn't fit below.
                var bar = form.DocsToolBar;
                int x = buttonBounds.Left - bar.Width > 0 ? bar.Left - closed.Width : bar.Right;
                at = new Point(x, Math.Min(buttonBounds.Top, form.ClientSize.Height - closed.Height));
            }
            setState(false);
            gif.Add(form, cursor, Cursors.Default, frames: 5, overlay: (closed, at));

            var item = menu.Items[index].Bounds;
            var target = new Point(at.X + item.X + item.Width / 2 - 10, at.Y + item.Y + item.Height / 2);
            for (int i = 1; i <= 6; i++)
                gif.Add(form, Lerp(cursor, target, Ease(i / 6f)), Cursors.Default, overlay: (closed, at));
            cursor = target;
            using var hovered = RenderMenu(menu, selected: index);
            gif.Add(form, cursor, Cursors.Default, frames: 9, overlay: (hovered, at));
            foreach (ToolStripItem menuItem in menu.Items)
                menuItem.Image?.Dispose();
        }

        choose();
        setState(true);
        gif.Add(form, cursor, Cursors.Default, frames: 4);
        return cursor;
    }

    // ---------------------------------------------------------------- GIF steps

    /// <summary>The hint, the cursor goes to the start corner, the selection is dragged out.</summary>
    private static Point SelectArea(CaptureForm form, GifWriter gif, Point cursor, Point start, Point end)
    {
        cursor = Move(gif, form, cursor, start, Cursors.Cross, () => form.DocsSetState(Rectangle.Empty, []));
        for (int i = 1; i <= 12; i++)
        {
            var corner = Lerp(start, end, Ease(i / 12f));
            form.DocsSetState(Rectangle.FromLTRB(start.X, start.Y, corner.X, corner.Y), [], creating: true);
            gif.Add(form, corner, Cursors.Cross);
        }
        form.DocsSetState(Rectangle.FromLTRB(start.X, start.Y, end.X, end.Y), []);
        gif.Add(form, end, Cursors.Default, frames: 6);
        return end;
    }

    /// <summary>Clicks a tool in the toolbar.</summary>
    private static Point PickTool(CaptureForm form, GifWriter gif, Rectangle selection, List<Annotation> done, Point cursor,
        ToolbarAction button, Tool tool)
    {
        var at = Center(form.DocsButtonBounds(button));
        cursor = Move(gif, form, cursor, at, Cursors.Default, () => form.DocsSetState(selection, done));
        form.DocsSetState(selection, done, tool, hover: button);
        gif.Add(form, cursor, Cursors.Default, frames: 5);
        return cursor;
    }

    /// <summary>Goes to <paramref name="from"/> and drags a shape to <paramref name="to"/>.</summary>
    private static Point Drag(CaptureForm form, GifWriter gif, Rectangle selection, List<Annotation> done, Tool tool,
        Point cursor, Point from, Point to, Func<Point, Point, Annotation> shape, int frames = 8)
    {
        Move(gif, form, cursor, from, Cursors.Cross, () => form.DocsSetState(selection, done, tool), frames: 6);
        Annotation? current = null;
        for (int i = 1; i <= frames; i++)
        {
            var p = Lerp(from, to, Ease(i / (float)frames));
            current = shape(from, p);
            form.DocsSetState(selection, [.. done, current], tool);
            gif.Add(form, p, Cursors.Cross);
        }
        done.Add(current!);
        form.DocsSetState(selection, done, tool);
        gif.Add(form, to, Cursors.Cross, frames: 4);
        return to;
    }

    /// <summary>Clicks at <paramref name="at"/> (the I-beam's middle, like the app) and types letter by letter.</summary>
    private static Point TypeText(CaptureForm form, GifWriter gif, Rectangle selection, List<Annotation> done, Point cursor,
        Point at, string text, int size)
    {
        cursor = Move(gif, form, cursor, at, Cursors.IBeam, () => form.DocsSetState(selection, done, Tool.Text), frames: 6);
        var font = new Font("Segoe UI", size, FontStyle.Regular, GraphicsUnit.Pixel);
        var location = new Point(at.X, at.Y - font.Height / 2);
        for (int i = 0; i <= text.Length; i++)
        {
            form.DocsSetState(selection, [.. done, new EditingText(Red, location, text[..i], font)], Tool.Text);
            gif.Add(form, cursor, Cursors.IBeam, frames: i == 0 ? 4 : 1);
        }
        // Enter: the text box becomes a text annotation.
        done.Add(new TextAnnotation(Red, location, text, font));
        form.DocsSetState(selection, done, Tool.Text);
        gif.Add(form, cursor, Cursors.IBeam, frames: 6);
        return cursor;
    }

    /// <summary>Moves the cursor over a still scene that <paramref name="setState"/> prepares.</summary>
    private static Point Move(GifWriter gif, CaptureForm form, Point from, Point to, Cursor cursor, Action setState, int frames = 8)
    {
        setState();
        for (int i = 1; i <= frames; i++)
            gif.Add(form, Lerp(from, to, Ease(i / (float)frames)), cursor);
        return to;
    }

    private static Point DragRedact(CaptureForm form, GifWriter gif, Rectangle selection, List<Annotation> done,
        RedactStyle style, Point cursor, Rectangle area)
    {
        cursor = Move(gif, form, cursor, area.Location, Cursors.Cross, () => form.DocsSetState(selection, done, Tool.Redact), frames: 6);
        RedactAnnotation? current = null;
        for (int i = 1; i <= 8; i++)
        {
            var corner = Lerp(area.Location, new Point(area.Right, area.Bottom), Ease(i / 8f));
            current = new RedactAnnotation(style, form.DocsShot, area.Location) { End = corner };
            form.DocsSetState(selection, [.. done, current], Tool.Redact);
            gif.Add(form, corner, Cursors.Cross);
        }
        done.Add(current!);
        form.DocsSetState(selection, done, Tool.Redact);
        gif.Add(form, new Point(area.Right, area.Bottom), Cursors.Cross, frames: 4);
        return new Point(area.Right, area.Bottom);
    }

    /// <summary>A context menu as it looks when open, optionally with one item highlighted.</summary>
    private static Bitmap RenderMenu(ContextMenuStrip menu, int? selected)
    {
        menu.Show(new Point(-4000, -4000));
        if (selected is { } index)
            menu.Items[index].Select();
        Application.DoEvents();
        var image = new Bitmap(menu.Width, menu.Height, PixelFormat.Format24bppRgb);
        menu.DrawToBitmap(image, new Rectangle(Point.Empty, menu.Size));
        menu.Close();
        return image;
    }

    // ---------------------------------------------------------------- helpers

    private static CaptureForm Overlay(Bitmap scene)
    {
        var settings = new AppSettings(); // defaults, nothing from the PC it runs on
        var actions = new ImageActions(settings, new Notifier(new NotifyIcon(), () => false));
        return new CaptureForm(scene, new Rectangle(Point.Empty, scene.Size), settings, actions, onRecord: _ => { });
    }

    private void Save(Bitmap image, string name)
    {
        image.Save(Path.Combine(_output, name), ImageFormat.Png);
        Console.WriteLine($"  {name}");
    }

    private static StrokeAnnotation Stroke(Color color, float width, IEnumerable<Point> points)
    {
        StrokeAnnotation? stroke = null;
        foreach (var p in points)
        {
            if (stroke == null)
                stroke = new StrokeAnnotation(color, width, p);
            else
                stroke.Add(p);
        }
        return stroke!;
    }

    private static IEnumerable<Point> Line(Point a, Point b)
    {
        for (int i = 0; i <= 20; i++)
            yield return Lerp(a, b, i / 20f);
    }

    /// <summary>A slightly open, hand-drawn looking ellipse.</summary>
    private static IEnumerable<Point> Ellipse(Rectangle r)
    {
        for (int i = 0; i <= 40; i++)
        {
            double t = -0.3 + i / 40.0 * 2.15 * Math.PI;
            double wobble = 1 + 0.06 * Math.Sin(t * 3);
            yield return new Point(
                (int)(r.X + r.Width / 2.0 + Math.Cos(t) * r.Width / 2.0 * wobble),
                (int)(r.Y + r.Height / 2.0 + Math.Sin(t) * r.Height / 2.0 * wobble));
        }
    }

    private static Point Lerp(Point a, Point b, float t) =>
        new((int)Math.Round(a.X + (b.X - a.X) * t), (int)Math.Round(a.Y + (b.Y - a.Y) * t));

    private static float Ease(float t) => t < 0.5f ? 2 * t * t : 1 - MathF.Pow(-2 * t + 2, 2) / 2;

    private static Point Center(Rectangle r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);
}

/// <summary>The text box while typing: white box, thin border, the text and a caret.</summary>
internal sealed class EditingText : Annotation
{
    private const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping;
    private readonly Point _location;
    private readonly string _text;
    private readonly Font _font;

    public EditingText(Color color, Point location, string text, Font font) : base(color, 0)
    {
        _location = location;
        _text = text;
        _font = font;
    }

    public override void Draw(Graphics g)
    {
        var size = TextRenderer.MeasureText(g, _text.Length == 0 ? " " : _text, _font, Size.Empty, Flags);
        int width = (_text.Length == 0 ? 0 : size.Width) + _font.Height / 2 + 4;
        var box = new Rectangle(_location.X - 4, _location.Y - 3, width + 4, _font.Height + 6);
        g.FillRectangle(Brushes.White, box);
        using (var border = new Pen(Color.FromArgb(0, 120, 215)))
            g.DrawRectangle(border, box);
        TextRenderer.DrawText(g, _text, _font, _location, Color, Color.White, Flags);
        int caretX = _location.X + (_text.Length == 0 ? 0 : size.Width) + 1;
        using var caret = new Pen(Color.Black, 1);
        g.DrawLine(caret, caretX, _location.Y + 2, caretX, _location.Y + _font.Height - 2);
    }
}

/// <summary>Collects overlay frames with a drawn cursor into a GIF using ShotLite's own encoder.</summary>
internal sealed class GifWriter : IDisposable
{
    private const long TicksPerFrame = 1_000_000; // 100 ms
    private readonly GifEncoder _encoder;
    private readonly Rectangle _crop;
    private long _time;

    public GifWriter(string path, Rectangle crop)
    {
        _crop = crop;
        _encoder = new GifEncoder(File.Create(path), crop.Width, crop.Height);
    }

    /// <param name="overlay">An open menu: drawn over the overlay, under the cursor.</param>
    public void Add(CaptureForm form, Point cursorAt, Cursor cursor, int frames = 1, (Bitmap Image, Point At)? overlay = null)
    {
        using var full = form.DocsRender();
        using (var g = Graphics.FromImage(full))
        {
            if (overlay is { } menu)
            {
                g.DrawImage(menu.Image, menu.At.X, menu.At.Y, menu.Image.Width, menu.Image.Height);
                using var border = new Pen(Color.FromArgb(160, 160, 160));
                g.DrawRectangle(border, menu.At.X, menu.At.Y, menu.Image.Width - 1, menu.Image.Height - 1);
            }
            cursor.Draw(g, new Rectangle(new Point(cursorAt.X - cursor.HotSpot.X, cursorAt.Y - cursor.HotSpot.Y), cursor.Size));
        }
        using var frame = full.Clone(_crop, PixelFormat.Format32bppArgb); // the encoder reads BGRA
        var data = frame.LockBits(new Rectangle(Point.Empty, frame.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            _encoder.Write(data.Scan0, data.Stride, _time);
        }
        finally
        {
            frame.UnlockBits(data);
        }
        _time += TicksPerFrame * frames;
    }

    public void Dispose()
    {
        _encoder.Finish(_time);
        _encoder.Dispose();
    }
}
