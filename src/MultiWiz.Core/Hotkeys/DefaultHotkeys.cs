namespace MultiWiz.Core.Hotkeys;

/// <summary>
/// Out-of-the-box bindings. Chosen to avoid combinations Wizard101 and common apps use
/// (the v3 switcher's global Ctrl+W / Ctrl+S broke closing tabs and saving files everywhere).
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
        [HotkeyAction.ToggleSwitcher] = "Ctrl+Alt+S",
        [HotkeyAction.ToggleCommandCenter] = "Ctrl+Alt+C",
        [HotkeyAction.ShowMainWindow] = "Ctrl+Alt+M",
        [HotkeyAction.ToggleNameBadges] = "Ctrl+Alt+B",
    };

    /// <summary>The effective binding text for an action: the user's value if present (even if empty = unbound), else the default.</summary>
    public static string Resolve(IReadOnlyDictionary<HotkeyAction, string> userBindings, HotkeyAction action) =>
        userBindings.TryGetValue(action, out var text) ? text : Bindings.GetValueOrDefault(action, string.Empty);
}
