namespace MultiWiz.Core.Hotkeys;

/// <summary>Values match Win32 MOD_ALT/MOD_CONTROL/MOD_SHIFT/MOD_WIN so they can be passed to RegisterHotKey directly.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Win = 0x8,
}
