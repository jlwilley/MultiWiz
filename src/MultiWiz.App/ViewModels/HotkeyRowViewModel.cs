using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MultiWiz.App.Services;
using MultiWiz.Core.Hotkeys;

namespace MultiWiz.App.ViewModels;

/// <summary>
/// One hotkey in Settings. Click the capture box, then press a combination: Esc cancels, Backspace/Delete clears.
/// </summary>
public sealed partial class HotkeyRowViewModel : ObservableObject
{
    private readonly SettingsPageViewModel _owner;

    public HotkeyRowViewModel(SettingsPageViewModel owner, HotkeyAction action)
    {
        _owner = owner;
        Action = action;
        Label = Describe(action);
    }

    public HotkeyAction Action { get; }

    public string Label { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText), nameof(IsUnbound), nameof(ShowAltGrWarning))]
    public partial string BindingText { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText), nameof(ShowAltGrWarning))]
    public partial bool IsCapturing { get; private set; }

    [ObservableProperty]
    public partial bool HasFailed { get; set; }

    [ObservableProperty]
    public partial string? CaptureHint { get; private set; }

    public bool IsUnbound => BindingText.Length == 0;

    public string DisplayText => IsCapturing ? "Press a key combination…" : IsUnbound ? "Not set" : BindingText;

    /// <summary>
    /// The binding is Ctrl+Alt plus a key that types a character: Windows reports AltGr as Ctrl+Alt, so on layouts with
    /// AltGr that character can no longer be typed anywhere while the hotkey is registered.
    /// </summary>
    public bool ShowAltGrWarning =>
        !IsCapturing && HotkeyBinding.TryParse(BindingText, out var binding) && binding.MayCollideWithAltGr;

    internal void SetBindingText(string text) => BindingText = text;

    internal void SetCapturing(bool capturing)
    {
        IsCapturing = capturing;
        CaptureHint = capturing ? "Esc cancels · Backspace clears" : null;
    }

    /// <summary>Handles a key press while capturing. Returns true when the key was consumed.</summary>
    public bool HandleKey(Key key, KeyModifiers modifiers)
    {
        if (!IsCapturing)
        {
            return false;
        }

        if (modifiers == KeyModifiers.None && key == Key.Escape)
        {
            _owner.EndCapture(this, newBinding: null);
            return true;
        }

        if (modifiers == KeyModifiers.None && key is Key.Back or Key.Delete)
        {
            _owner.EndCapture(this, newBinding: string.Empty);
            return true;
        }

        if (HotkeyKeyMapper.IsModifierKey(key))
        {
            return true; // Wait for the main key.
        }

        if (!HotkeyKeyMapper.TryCreateBinding(key, modifiers, out var binding))
        {
            CaptureHint = "That key can't be used for a hotkey.";
            return true;
        }

        var isFunctionKey = binding.VirtualKey is >= 0x70 and <= 0x87;
        if (binding.Modifiers == HotkeyModifiers.None && !isFunctionKey)
        {
            CaptureHint = "Add Ctrl, Alt, Shift or Win so normal typing keeps working.";
            return true;
        }

        _owner.EndCapture(this, binding.ToString());
        return true;
    }

    [RelayCommand]
    private void ToggleCapture()
    {
        if (IsCapturing)
        {
            _owner.EndCapture(this, newBinding: null);
        }
        else
        {
            _owner.BeginCapture(this);
        }
    }

    private static string Describe(HotkeyAction action)
    {
        var slotIndex = (int)action - (int)HotkeyAction.FocusSlot1;
        return slotIndex is >= 0 and <= 7 ? $"Focus client in slot {slotIndex + 1}" : DescribeOther(action);
    }

    private static string DescribeOther(HotkeyAction action) => action switch
    {
        HotkeyAction.NextClient => "Focus next client",
        HotkeyAction.PreviousClient => "Focus previous client",
        HotkeyAction.ToggleSwitcher => "Show or hide the switcher",
        HotkeyAction.ToggleCommandCenter => "Show or hide Command Center",
        HotkeyAction.ShowMainWindow => "Show MultiWiz",
        HotkeyAction.ToggleNameBadges => "Show or hide name badges",
        _ => action.ToString(),
    };
}
