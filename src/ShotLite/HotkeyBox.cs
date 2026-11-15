using System.ComponentModel;

namespace ShotLite;

/// <summary>Text box that records a key combination instead of text.</summary>
internal sealed class HotkeyBox : TextBox
{
    private Keys _hotkey;

    public HotkeyBox()
    {
        ReadOnly = true;
        BackColor = SystemColors.Window;
        ShortcutsEnabled = false;
        Width = 170;
    }

    /// <summary>True while the box has focus and records keys; the global hotkeys must be released meanwhile.</summary>
    public event Action<bool>? EditingChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Keys Hotkey
    {
        get => _hotkey;
        set
        {
            _hotkey = value;
            Text = HotkeyText.Format(value);
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Tab || keyData == (Keys.Shift | Keys.Tab))
            return base.ProcessCmdKey(ref msg, keyData);

        Record(keyData);
        return true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        // Windows sends no key-down for PrintScreen, only key-up.
        if (e.KeyCode == Keys.PrintScreen)
            Hotkey = e.KeyData;
        else if (IsModifierOnly(e.KeyCode))
            Text = HotkeyText.Format(_hotkey);
    }

    protected override void OnEnter(EventArgs e)
    {
        base.OnEnter(e);
        EditingChanged?.Invoke(true);
    }

    protected override void OnLeave(EventArgs e)
    {
        base.OnLeave(e);
        Text = HotkeyText.Format(_hotkey);
        EditingChanged?.Invoke(false);
    }

    private void Record(Keys keyData)
    {
        var code = keyData & Keys.KeyCode;
        var modifiers = keyData & Keys.Modifiers;

        if (IsModifierOnly(code))
        {
            Text = HotkeyText.Modifiers(modifiers) + "…";
            return;
        }
        if (modifiers == Keys.None && code is Keys.Back or Keys.Delete)
        {
            Hotkey = Keys.None;
            return;
        }
        Hotkey = keyData;
    }

    private static bool IsModifierOnly(Keys code) =>
        code is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin;
}
