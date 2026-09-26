using Avalonia.Input;
using MultiWiz.Core.Hotkeys;

namespace MultiWiz.App.Services;

/// <summary>Turns an Avalonia key press into a Win32 <see cref="HotkeyBinding"/> for the hotkey capture boxes.</summary>
public static class HotkeyKeyMapper
{
    public static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    /// <summary>Builds a binding from a key press, or returns false when the key has no hotkey name.</summary>
    public static bool TryCreateBinding(Key key, KeyModifiers modifiers, out HotkeyBinding binding)
    {
        binding = default;
        var virtualKey = ToVirtualKey(key);
        if (virtualKey == 0)
        {
            return false;
        }

        var hotkeyModifiers = HotkeyModifiers.None;
        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            hotkeyModifiers |= HotkeyModifiers.Control;
        }

        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            hotkeyModifiers |= HotkeyModifiers.Alt;
        }

        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            hotkeyModifiers |= HotkeyModifiers.Shift;
        }

        if (modifiers.HasFlag(KeyModifiers.Meta))
        {
            hotkeyModifiers |= HotkeyModifiers.Win;
        }

        binding = new HotkeyBinding(hotkeyModifiers, virtualKey);
        return binding.IsValid;
    }

    /// <summary>The Win32 virtual-key code for keys that <see cref="VirtualKeys"/> can name, otherwise 0.</summary>
    public static int ToVirtualKey(Key key)
    {
        if (key >= Key.D0 && key <= Key.D9)
        {
            return 0x30 + (key - Key.D0);
        }

        if (key >= Key.A && key <= Key.Z)
        {
            return 0x41 + (key - Key.A);
        }

        if (key >= Key.NumPad0 && key <= Key.NumPad9)
        {
            return 0x60 + (key - Key.NumPad0);
        }

        if (key >= Key.F1 && key <= Key.F24)
        {
            return 0x70 + (key - Key.F1);
        }

        return key switch
        {
            Key.Back => 0x08,
            Key.Tab => 0x09,
            Key.Enter => 0x0D,
            Key.Pause => 0x13,
            Key.CapsLock => 0x14,
            Key.Escape => 0x1B,
            Key.Space => 0x20,
            Key.PageUp => 0x21,
            Key.PageDown => 0x22,
            Key.End => 0x23,
            Key.Home => 0x24,
            Key.Left => 0x25,
            Key.Up => 0x26,
            Key.Right => 0x27,
            Key.Down => 0x28,
            Key.PrintScreen => 0x2C,
            Key.Insert => 0x2D,
            Key.Delete => 0x2E,
            Key.Multiply => 0x6A,
            Key.Add => 0x6B,
            Key.Subtract => 0x6D,
            Key.Decimal => 0x6E,
            Key.Divide => 0x6F,
            Key.NumLock => 0x90,
            Key.Scroll => 0x91,
            Key.OemSemicolon => 0xBA,
            Key.OemPlus => 0xBB,
            Key.OemComma => 0xBC,
            Key.OemMinus => 0xBD,
            Key.OemPeriod => 0xBE,
            Key.OemQuestion => 0xBF,
            Key.OemTilde => 0xC0,
            Key.OemOpenBrackets => 0xDB,
            Key.OemPipe => 0xDC,
            Key.OemCloseBrackets => 0xDD,
            Key.OemQuotes => 0xDE,
            _ => 0,
        };
    }
}
