using System.Diagnostics;
using Windows.Graphics.Capture;

namespace ShotLite;

public enum RecordFormat { Mp4, Gif }

/// <summary>The monitor and the part of it to record, in physical pixels.</summary>
internal sealed record RecordingArea(IntPtr Monitor, Rectangle MonitorBounds, Rectangle Bounds)
{
    private const int MinSize = 64;

    /// <summary>Limits a rectangle to the monitor holding most of it, with the even size that H.264 requires.</summary>
    public static RecordingArea From(Rectangle requested)
    {
        var monitor = Screen.FromRectangle(requested).Bounds;
        var area = Rectangle.Intersect(requested, monitor);
        area.Width = Math.Max(area.Width, MinSize);
        area.Height = Math.Max(area.Height, MinSize);
        area.X = Math.Min(area.X, monitor.Right - area.Width);
        area.Y = Math.Min(area.Y, monitor.Bottom - area.Height);
        area.Width &= ~1;
        area.Height &= ~1;

        var center = new Native.POINT(monitor.Left + monitor.Width / 2, monitor.Top + monitor.Height / 2);
        return new RecordingArea(Native.MonitorFromPoint(center, Native.MONITOR_DEFAULTTONEAREST), monitor, area);
    }
}

/// <summary>No path and no error means the recording was stopped before its first frame.</summary>
/// <param name="Path">The saved file; with <paramref name="Error"/> set, it holds what was recorded before the failure.</param>
internal sealed record RecordingResult(string? Path, bool LimitReached, Exception? Error);

/// <summary>
/// Runs one recording on its own thread: takes the newest captured frame at a steady rate and feeds the encoder.
/// The file gets its final name only after the encoder has finished it.
/// </summary>
internal sealed class ScreenRecorder
{
    private const int MoveAttempts = 5;
    private const int MoveRetryMilliseconds = 500;
    private static readonly TimeSpan FirstFrameTimeout = TimeSpan.FromSeconds(3);

    private readonly RecordingArea _area;
    private readonly RecordFormat _format;
    private readonly bool _cursor;
    private readonly string _path;
    private readonly TaskCompletionSource<RecordingResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Stopwatch _clock = new();
    private volatile bool _stopRequested;
    private Exception? _interruption;

    public ScreenRecorder(RecordingArea area, RecordFormat format, bool cursor, string path)
    {
        _area = area;
        _format = format;
        _cursor = cursor;
        _path = path;
    }

    /// <summary>Windows.Graphics.Capture with cursor control and capture exclusion arrived in Windows 10 2004.</summary>
    public static bool IsSupported =>
        OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) && GraphicsCaptureSession.IsSupported();

    /// <summary>Set when the recording ends, however it ended.</summary>
    public Task<RecordingResult> Completion => _completion.Task;

    public TimeSpan Elapsed => _clock.Elapsed;

    public void Start() => new Thread(Run) { IsBackground = true, Name = "ShotLite recorder" }.Start();

    public void RequestStop() => _stopRequested = true;

    private void Run()
    {
        // The encoder writes under a temporary name with a real extension: the sink writer picks the container from it.
        var extension = Path.GetExtension(_path);
        var temporary = Path.ChangeExtension(_path, ".recording" + extension);
        try
        {
            Native.timeBeginPeriod(1);
            bool limitReached = Record(temporary);
            if (_format == RecordFormat.Mp4)
                TagMp4(temporary);
            _completion.SetResult(new RecordingResult(MoveToFinalName(temporary), limitReached, _interruption));
        }
        catch (Exception ex)
        {
            // Last stop of this thread: anything unhandled here would end the whole tray app.
            try
            {
                File.Delete(temporary);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
            }
            _completion.SetResult(new RecordingResult(null, false, ex is OperationCanceledException ? null : ex));
        }
        finally
        {
            Native.timeEndPeriod(1);
        }
    }

    /// <summary>A virus scanner or sync client may hold a fresh file for a moment; the video must survive that.</summary>
    /// <returns>Where the recording ended up: under the temporary name if it can't be renamed.</returns>
    private string MoveToFinalName(string temporary)
    {
        for (int attempt = 1; attempt <= MoveAttempts; attempt++)
        {
            try
            {
                File.Move(temporary, _path);
                return _path;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt < MoveAttempts)
                    Thread.Sleep(MoveRetryMilliseconds);
            }
        }
        return temporary;
    }

    /// <summary>The credits aren't worth losing a finished recording over.</summary>
    private static void TagMp4(string path)
    {
        try
        {
            RecordingMetadata.TagMp4(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private bool Record(string path)
    {
        var bounds = _area.Bounds;
        var crop = new Rectangle(bounds.X - _area.MonitorBounds.X, bounds.Y - _area.MonitorBounds.Y, bounds.Width, bounds.Height);

        using var source = new CaptureSource(CaptureSource.ForMonitor(_area.Monitor), crop, _cursor);
        using var encoder = CreateEncoder(path, bounds.Size);
        int fps = _format == RecordFormat.Gif ? GifEncoder.Fps : Mp4Encoder.Fps;

        source.Start();
        WaitForFirstFrame(source);

        _clock.Start();
        long ticksPerFrame = TimeSpan.TicksPerSecond / fps;
        long next = 0, frames = 0;
        bool limitReached = false;
        try
        {
            while (!_stopRequested && !source.IsClosed)
            {
                long now = _clock.Elapsed.Ticks;
                if (_format == RecordFormat.Gif && now >= TimeSpan.TicksPerSecond * GifEncoder.MaxSeconds)
                {
                    limitReached = true;
                    break;
                }
                if (now < next)
                {
                    Thread.Sleep(1);
                    continue;
                }

                source.TryCopyLatest();
                var pixels = source.MapPixels(out int stride);
                try
                {
                    encoder.Write(pixels, stride, next);
                }
                finally
                {
                    source.UnmapPixels();
                }
                frames++;

                // After a slow frame the timestamps jump ahead instead of the clip falling behind real time.
                next = (_clock.Elapsed.Ticks / ticksPerFrame + 1) * ticksPerFrame;
            }
        }
        catch (Exception ex) when (frames > 0)
        {
            // Whatever broke the capture (e.g. a lost graphics device), the frames so far are worth keeping.
            _interruption = ex;
        }

        // Stopped before the first frame: there is nothing to save or to report.
        if (frames == 0)
            throw new OperationCanceledException();

        encoder.Finish(_clock.Elapsed.Ticks);
        return limitReached;
    }

    private IFrameEncoder CreateEncoder(string path, Size size) =>
        _format == RecordFormat.Gif
            ? new GifEncoder(File.Create(path), size.Width, size.Height)
            : new Mp4Encoder(path, size.Width, size.Height);

    private void WaitForFirstFrame(CaptureSource source)
    {
        var waited = Stopwatch.StartNew();
        while (!source.TryCopyLatest())
        {
            // Stopped before anything was recorded: there is nothing to save or to report.
            if (_stopRequested)
                throw new OperationCanceledException();
            if (source.IsClosed || waited.Elapsed > FirstFrameTimeout)
                throw new TimeoutException("No frame arrived from the screen.");
            Thread.Sleep(5);
        }
    }
}
