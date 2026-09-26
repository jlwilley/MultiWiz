using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace MultiWiz.Core.Hotkeys;

/// <summary>A key plus modifiers, written as text like "Alt+1" or "Ctrl+Shift+F5".</summary>
public readonly record struct HotkeyBinding(HotkeyModifiers Modifiers, int VirtualKey)
{
    /// <summary>True when there is a key and it is not itself a modifier key.</summary>
    public bool IsValid => VirtualKey != 0 && VirtualKeys.GetName(VirtualKey) is not null;

    /// <summary>
    /// True for Ctrl+Alt (with or without Shift, without Win) plus a key that types a character. Windows reports AltGr
    /// as Ctrl+Alt, so on layouts with AltGr such a global hotkey swallows that character in every program
    /// (Polish ś is AltGr+S, German µ is AltGr+M). Whether a given key really types something depends on the layout.
    /// </summary>
    public bool MayCollideWithAltGr =>
        (Modifiers & (HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Win)) == (HotkeyModifiers.Control | HotkeyModifiers.Alt)
        && VirtualKeys.IsCharacterKey(VirtualKey);

    public static bool TryParse([NotNullWhen(true)] string? text, out HotkeyBinding binding)
    {
        binding = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        int? key = null;

        // Split on '+' but allow "+" itself as a key name is not supported; "=" is used instead.
        foreach (var rawPart in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (rawPart.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= HotkeyModifiers.Control;
                    continue;
                case "ALT":
                    modifiers |= HotkeyModifiers.Alt;
                    continue;
                case "SHIFT":
                    modifiers |= HotkeyModifiers.Shift;
                    continue;
                case "WIN":
                case "WINDOWS":
                    modifiers |= HotkeyModifiers.Win;
                    continue;
            }

            if (key is not null || !VirtualKeys.TryGetKey(rawPart, out var vk))
            {
                return false;
            }

            key = vk;
        }

        if (key is null)
        {
            return false;
        }

        binding = new HotkeyBinding(modifiers, key.Value);
        return true;
    }

    public override string ToString()
    {
        var builder = new StringBuilder();
        if (Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            builder.Append("Ctrl+");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            builder.Append("Alt+");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            builder.Append("Shift+");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Win))
        {
            builder.Append("Win+");
        }

        builder.Append(VirtualKeys.GetName(VirtualKey) ?? $"0x{VirtualKey:X2}");
        return builder.ToString();
    }
}
