namespace MultiWiz.Core.Hotkeys;

/// <summary>
/// Out-of-the-box bindings. Chosen to avoid combinations Wizard101 and common apps use
/// (the v3 switcher's global Ctrl+W / Ctrl+S broke closing tabs and saving files everywhere).
/// The Ctrl+Alt ones use F-keys on purpose: Windows reports AltGr as Ctrl+Alt, so a global Ctrl+Alt+letter hotkey
/// swallows the AltGr character on many European layouts (Polish ś is AltGr+S, German µ is AltGr+M), while
/// AltGr+F-key types nothing on any standard layout.
/// </summary>
public static class DefaultHotkeys
{
    public static IReadOnlyDictionary<HotkeyAction, string> Bindings { get; } = new Dictionary<HotkeyAction, string>
    {
        [HotkeyAction.FocusSlot1] = "Alt+1",
        [HotkeyAction.FocusSlot2] = "Alt+2",
        [HotkeyAction.FocusSlot3] = "Alt+3",
        [HotkeyAction.FocusSlot4] = "Alt+4",
        [HotkeyAction.FocusSlot5] = "Alt+5",
        [HotkeyAction.FocusSlot6] = "Alt+6",
        [HotkeyAction.FocusSlot7] = "Alt+7",
        [HotkeyAction.FocusSlot8] = "Alt+8",
        [HotkeyAction.NextClient] = "Alt+`",
        [HotkeyAction.PreviousClient] = "Alt+Shift+`",
        [HotkeyAction.ToggleSwitcher] = "Ctrl+Alt+F9",
        [HotkeyAction.ToggleCommandCenter] = "Ctrl+Alt+F10",
        [HotkeyAction.ShowMainWindow] = "Ctrl+Alt+F11",
        [HotkeyAction.ToggleNameBadges] = "Ctrl+Alt+F12",
    };

    /// <summary>The effective binding text for an action: the user's value if present (even if empty = unbound), else the default.</summary>
    public static string Resolve(IReadOnlyDictionary<HotkeyAction, string> userBindings, HotkeyAction action) =>
        userBindings.TryGetValue(action, out var text) ? text : Bindings.GetValueOrDefault(action, string.Empty);
}
