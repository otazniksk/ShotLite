namespace ShotLite;

/// <summary>Tray balloon notifications with an optional click action.</summary>
internal sealed class Notifier
{
    private readonly NotifyIcon _tray;
    private readonly Func<bool> _enabled;
    private Action? _onClick;

    public Notifier(NotifyIcon tray, Func<bool> enabled)
    {
        _tray = tray;
        _enabled = enabled;
        _tray.BalloonTipClicked += (_, _) =>
        {
            var action = _onClick;
            _onClick = null;
            action?.Invoke();
        };
    }

    public void Show(string text, Action? onClick = null, bool force = false)
    {
        if (!force && !_enabled())
            return;
        _onClick = onClick;
        _tray.ShowBalloonTip(3000, Strings.AppName, text, ToolTipIcon.None);
    }

    public void Error(string text)
    {
        _onClick = null;
        _tray.ShowBalloonTip(6000, Strings.AppName, text, ToolTipIcon.Warning);
    }
}
