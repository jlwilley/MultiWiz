using MultiWiz.Core.Hotkeys;

namespace MultiWiz.Core.Tests;

public sealed class HotkeyBindingTests
{
    [Theory]
    [InlineData("Alt+1", HotkeyModifiers.Alt, 0x31)]
    [InlineData("Ctrl+Shift+F5", HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x74)]
    [InlineData("Alt+`", HotkeyModifiers.Alt, 0xC0)]
    [InlineData("Win+Num0", HotkeyModifiers.Win, 0x60)]
    [InlineData("F13", HotkeyModifiers.None, 0x7C)]
    [InlineData("Ctrl+Alt+Shift+Win+Z", HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift | HotkeyModifiers.Win, 0x5A)]
    public void Parses_modifiers_and_key(string text, HotkeyModifiers modifiers, int virtualKey)
    {
        Assert.True(HotkeyBinding.TryParse(text, out var binding));
        Assert.Equal(new HotkeyBinding(modifiers, virtualKey), binding);
        Assert.True(binding.IsValid);
    }

    [Theory]
    [InlineData("Alt+1")]
    [InlineData("Ctrl+Shift+F5")]
    [InlineData("Alt+Shift+`")]
    [InlineData("Ctrl+Alt+S")]
    [InlineData("Ctrl+Alt+Shift+Win+PageDown")]
    [InlineData("Num5")]
    public void Canonical_text_round_trips(string text)
    {
        Assert.True(HotkeyBinding.TryParse(text, out var binding));
        Assert.Equal(text, binding.ToString());
        Assert.True(HotkeyBinding.TryParse(binding.ToString(), out var again));
        Assert.Equal(binding, again);
    }

    [Theory]
    [InlineData("shift+ctrl+f5", "Ctrl+Shift+F5")]
    [InlineData(" Alt + 1 ", "Alt+1")]
    [InlineData("Control+Esc", "Ctrl+Escape")]
    [InlineData("Windows+Return", "Win+Enter")]
    [InlineData("alt+backtick", "Alt+`")]
    [InlineData("Ctrl+PgDn", "Ctrl+PageDown")]
    public void Aliases_and_any_order_normalize_to_canonical_text(string text, string expected)
    {
        Assert.True(HotkeyBinding.TryParse(text, out var binding));
        Assert.Equal(expected, binding.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Alt")]
    [InlineData("Ctrl+Shift")]
    [InlineData("Alt+1+2")]
    [InlineData("Alt+Banana")]
    [InlineData("Ctrl++")]
    [InlineData("F25")]
    public void Rejects_invalid_text(string? text)
    {
        Assert.False(HotkeyBinding.TryParse(text, out var binding));
        Assert.Equal(default(HotkeyBinding), binding);
    }

    [Fact]
    public void Modifier_keys_and_unknown_codes_are_not_valid_keys()
    {
        Assert.False(new HotkeyBinding(HotkeyModifiers.Alt, 0x10).IsValid); // VK_SHIFT
        Assert.False(new HotkeyBinding(HotkeyModifiers.Alt, 0).IsValid);
        Assert.Equal("Alt+0x10", new HotkeyBinding(HotkeyModifiers.Alt, 0x10).ToString());
    }

    [Fact]
    public void Every_default_binding_parses_and_is_valid()
    {
        foreach (var (action, text) in DefaultHotkeys.Bindings)
        {
            Assert.True(HotkeyBinding.TryParse(text, out var binding), $"{action}: {text}");
            Assert.True(binding.IsValid, $"{action}: {text}");
        }
    }

    [Fact]
    public void Default_bindings_are_unique()
    {
        var parsed = DefaultHotkeys.Bindings.Values.Select(text => HotkeyBinding.TryParse(text, out var binding) ? binding : default).ToArray();

        Assert.Equal(parsed.Length, parsed.Distinct().Count());
    }

    [Fact]
    public void Resolve_uses_the_default_when_the_user_has_no_binding()
    {
        var user = new Dictionary<HotkeyAction, string>();

        Assert.Equal("Alt+1", DefaultHotkeys.Resolve(user, HotkeyAction.FocusSlot1));
        Assert.Equal("Ctrl+Alt+F9", DefaultHotkeys.Resolve(user, HotkeyAction.ToggleSwitcher));
    }

    [Fact]
    public void No_default_binding_takes_over_an_altgr_character()
    {
        foreach (var (action, text) in DefaultHotkeys.Bindings)
        {
            Assert.True(HotkeyBinding.TryParse(text, out var binding), $"{action}: {text}");
            Assert.False(binding.MayCollideWithAltGr, $"{action}: {text}");
        }
    }

    [Theory]
    [InlineData("Ctrl+Alt+S", true)]
    [InlineData("Ctrl+Alt+Shift+C", true)]
    [InlineData("Ctrl+Alt+7", true)]
    [InlineData("Ctrl+Alt+[", true)]
    [InlineData("Ctrl+Alt+F9", false)]
    [InlineData("Ctrl+Alt+PageDown", false)]
    [InlineData("Ctrl+Alt+Win+S", false)]
    [InlineData("Ctrl+S", false)]
    [InlineData("Alt+S", false)]
    public void Ctrl_alt_character_keys_may_collide_with_altgr(string text, bool expected)
    {
        Assert.True(HotkeyBinding.TryParse(text, out var binding));
        Assert.Equal(expected, binding.MayCollideWithAltGr);
    }

    [Fact]
    public void Resolve_prefers_the_user_binding_and_keeps_an_empty_one_as_unbound()
    {
        var user = new Dictionary<HotkeyAction, string>
        {
            [HotkeyAction.FocusSlot1] = "F1",
            [HotkeyAction.NextClient] = string.Empty,
        };

        Assert.Equal("F1", DefaultHotkeys.Resolve(user, HotkeyAction.FocusSlot1));
        Assert.Equal(string.Empty, DefaultHotkeys.Resolve(user, HotkeyAction.NextClient));
        Assert.Equal("Alt+2", DefaultHotkeys.Resolve(user, HotkeyAction.FocusSlot2));
    }

    [Fact]
    public void Every_action_has_a_default_binding()
    {
        foreach (var action in Enum.GetValues<HotkeyAction>())
        {
            Assert.False(string.IsNullOrEmpty(DefaultHotkeys.Resolve(new Dictionary<HotkeyAction, string>(), action)), action.ToString());
        }
    }
}
