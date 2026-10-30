namespace ShotLite;

/// <summary>One recording from the RECORDING sign to the saved file.</summary>
internal sealed class RecordingSession
{
    private const int SignMilliseconds = 1000;
    private const int TimeRefreshMilliseconds = 200;
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(10);

    private readonly RecordingArea _area;
    private readonly AppSettings _settings;
    private readonly ImageActions _actions;
    private readonly RecordingFrame _frame;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = TimeRefreshMilliseconds };
    private ScreenRecorder? _recorder;
    private long _signUntil;
    private bool _stopped;

    public RecordingSession(Rectangle requestedArea, AppSettings settings, ImageActions actions)
    {
        _area = RecordingArea.From(requestedArea);
        _settings = settings;
        _actions = actions;
        var limit = settings.RecordFormat == RecordFormat.Gif ? $" / {GifEncoder.MaxSeconds / 60:00}:{GifEncoder.MaxSeconds % 60:00}" : "";
        _frame = new RecordingFrame(_area, limit);
        _frame.StopClicked += Stop;
        _timer.Tick += (_, _) => OnTick();
    }

    /// <summary>Raised on the UI thread after the file is saved, or the recording failed or was cancelled.</summary>
    public event Action? Finished;

    public void Start()
    {
        _frame.Show();
        _frame.ShowSign(true);
        _signUntil = Environment.TickCount64 + SignMilliseconds;
        _timer.Start();
        // Already under the sign, which stays out of the capture: the encoder is ready by the time it goes away.
        Record();
    }

    /// <summary>Ends the recording; before the first frame it cancels it and no file is created.</summary>
    public void Stop()
    {
        if (_stopped)
            return;
        _stopped = true;
        _timer.Stop();
        CloseFrame();

        if (_recorder != null)
            _recorder.RequestStop();
        else
            Finished?.Invoke();
    }

    /// <summary>For exiting the app: the file must be complete before the process ends.</summary>
    public void StopAndWait()
    {
        Stop();
        _recorder?.Completion.Wait(ExitTimeout);
    }

    private void OnTick()
    {
        if (_signUntil != 0 && Environment.TickCount64 >= _signUntil)
        {
            _signUntil = 0;
            _frame.ShowSign(false);
        }
        if (_recorder != null)
            _frame.ShowTime(_recorder.Elapsed);
    }

    private async void Record()
    {
        ScreenRecorder recorder;
        try
        {
            bool gif = _settings.RecordFormat == RecordFormat.Gif;
            var path = _actions.NewRecordingPath(gif ? ".gif" : ".mp4");
            recorder = new ScreenRecorder(_area, _settings.RecordFormat, _settings.RecordCursor, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _stopped = true;
            _timer.Stop();
            CloseFrame();
            _actions.NotifyRecordingFailed(ex.Message);
            Finished?.Invoke();
            return;
        }

        _recorder = recorder;
        recorder.Start();
        var result = await recorder.Completion; // continues on the UI thread

        _stopped = true;
        _timer.Stop();
        CloseFrame();
        if (result.Path != null)
            _actions.NotifyRecordingSaved(result.Path, result.LimitReached, result.Error?.Message);
        else if (result.Error != null)
            _actions.NotifyRecordingFailed(result.Error.Message);
        Finished?.Invoke();
    }

    private void CloseFrame()
    {
        if (!_frame.IsDisposed)
            _frame.Close();
        _timer.Dispose();
    }
}
