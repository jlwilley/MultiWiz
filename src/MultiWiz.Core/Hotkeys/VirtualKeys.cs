namespace MultiWiz.Core.Hotkeys;

/// <summary>Maps Win32 virtual-key codes to the names used in hotkey strings ("Alt+1", "Ctrl+Shift+F5").</summary>
public static class VirtualKeys
{
    public const int Tab = 0x09;
    public const int Enter = 0x0D;

    private static readonly Dictionary<int, string> NamesByKey = BuildNames();
    private static readonly Dictionary<string, int> KeysByName = BuildLookup(NamesByKey);

    public static IReadOnlyDictionary<int, string> All => NamesByKey;

    public static string? GetName(int virtualKey) => NamesByKey.GetValueOrDefault(virtualKey);

    public static bool TryGetKey(string name, out int virtualKey) => KeysByName.TryGetValue(name.Trim(), out virtualKey);

    /// <summary>Letters, digits and the OEM punctuation keys: the keys a keyboard layout maps to characters.</summary>
    public static bool IsCharacterKey(int virtualKey) =>
        virtualKey is (>= 0x30 and <= 0x39) or (>= 0x41 and <= 0x5A) or (>= 0xBA and <= 0xC0) or (>= 0xDB and <= 0xDF) or 0xE2;

    private static Dictionary<int, string> BuildNames()
    {
        var names = new Dictionary<int, string>
        {
            [0x08] = "Backspace",
            [0x09] = "Tab",
            [0x0D] = "Enter",
            [0x13] = "Pause",
            [0x14] = "CapsLock",
            [0x1B] = "Escape",
            [0x20] = "Space",
            [0x21] = "PageUp",
            [0x22] = "PageDown",
            [0x23] = "End",
            [0x24] = "Home",
            [0x25] = "Left",
            [0x26] = "Up",
            [0x27] = "Right",
            [0x28] = "Down",
            [0x2C] = "PrintScreen",
            [0x2D] = "Insert",
            [0x2E] = "Delete",
            [0x6A] = "NumMultiply",
            [0x6B] = "NumAdd",
            [0x6D] = "NumSubtract",
            [0x6E] = "NumDecimal",
            [0x6F] = "NumDivide",
            [0x90] = "NumLock",
            [0x91] = "ScrollLock",
            [0xBA] = ";",
            [0xBB] = "=",
            [0xBC] = ",",
            [0xBD] = "-",
            [0xBE] = ".",
            [0xBF] = "/",
            [0xC0] = "`",
            [0xDB] = "[",
            [0xDC] = "\\",
            [0xDD] = "]",
            [0xDE] = "'",
        };

        for (var c = '0'; c <= '9'; c++)
        {
            names[c] = c.ToString();
        }

        for (var c = 'A'; c <= 'Z'; c++)
        {
            names[c] = c.ToString();
        }

        for (var i = 0; i <= 9; i++)
        {
            names[0x60 + i] = $"Num{i}";
        }

        for (var i = 1; i <= 24; i++)
        {
            names[0x70 + i - 1] = $"F{i}";
        }

        return names;
    }

    private static Dictionary<string, int> BuildLookup(Dictionary<int, string> names)
    {
        var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, name) in names)
        {
            lookup[name] = key;
        }

        // Common aliases accepted when parsing.
        lookup["Return"] = 0x0D;
        lookup["Esc"] = 0x1B;
        lookup["Del"] = 0x2E;
        lookup["Ins"] = 0x2D;
        lookup["PgUp"] = 0x21;
        lookup["PgDn"] = 0x22;
        lookup["Backtick"] = 0xC0;
        lookup["Tilde"] = 0xC0;
        lookup["Minus"] = 0xBD;
        lookup["Plus"] = 0xBB;
        lookup["Equals"] = 0xBB;
        return lookup;
    }
}
