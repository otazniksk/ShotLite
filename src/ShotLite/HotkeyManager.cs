namespace ShotLite;

/// <summary>Registers system-wide hotkeys on a hidden message window.</summary>
internal sealed class HotkeyManager : NativeWindow, IDisposable
{
    private readonly HashSet<int> _registered = new();

    public HotkeyManager() => CreateHandle(new CreateParams());

    public event Action<int>? Pressed;

    public bool Register(int id, Keys keys)
    {
        uint vk = (uint)(keys & Keys.KeyCode);
        if (vk == 0)
            return false;

        uint modifiers = Native.MOD_NOREPEAT;
        if (keys.HasFlag(Keys.Control)) modifiers |= Native.MOD_CONTROL;
        if (keys.HasFlag(Keys.Shift)) modifiers |= Native.MOD_SHIFT;
        if (keys.HasFlag(Keys.Alt)) modifiers |= Native.MOD_ALT;

        if (!Native.RegisterHotKey(Handle, id, modifiers, vk))
            return false;

        _registered.Add(id);
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in _registered)
            Native.UnregisterHotKey(Handle, id);
        _registered.Clear();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY)
            Pressed?.Invoke(m.WParam.ToInt32());
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterAll();
        DestroyHandle();
    }
}

internal static class HotkeyText
{
    public static string Format(Keys keys)
    {
        var code = keys & Keys.KeyCode;
        if (code == Keys.None)
            return "—";
        return Modifiers(keys) + KeyName(code);
    }

    public static string Modifiers(Keys keys)
    {
        var text = "";
        if (keys.HasFlag(Keys.Control)) text += "Ctrl+";
        if (keys.HasFlag(Keys.Shift)) text += "Shift+";
        if (keys.HasFlag(Keys.Alt)) text += "Alt+";
        return text;
    }

    private static string KeyName(Keys code) => code switch
    {
        Keys.PrintScreen => "PrtScr",
        >= Keys.D0 and <= Keys.D9 => ((char)('0' + (code - Keys.D0))).ToString(),
        Keys.Next => "PageDown",
        Keys.Prior => "PageUp",
        Keys.Return => "Enter",
        _ => code.ToString(),
    };
}
