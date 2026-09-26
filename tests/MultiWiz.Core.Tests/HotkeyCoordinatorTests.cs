using Microsoft.Extensions.Logging.Abstractions;
using MultiWiz.Core.Hotkeys;
using MultiWiz.Core.Settings;
using MultiWiz.Core.Tests.Fakes;

namespace MultiWiz.Core.Tests;

public sealed class HotkeyCoordinatorTests : IDisposable
{
    private readonly FakeHotkeyService _hotkeys = new();
    private readonly FakeClientSwitcher _switcher = new();
    private readonly FakeSettingsStore _settings = new();
    private readonly HotkeyCoordinator _coordinator;

    public HotkeyCoordinatorTests()
    {
        _coordinator = new HotkeyCoordinator(_hotkeys, _switcher, _settings, NullLogger<HotkeyCoordinator>.Instance);
    }

    public void Dispose() => _coordinator.Dispose();

    [Fact]
    public void Start_registers_every_default_binding()
    {
        var raised = 0;
        _coordinator.RegistrationsChanged += (_, _) => raised++;

        _coordinator.Start();

        Assert.Equal(DefaultHotkeys.Bindings.Count, _hotkeys.Active.Count);
        foreach (var binding in DefaultHotkeys.Bindings.Values)
        {
            Assert.True(_hotkeys.IsActive(binding), $"{binding} was not registered");
        }

        Assert.Empty(_coordinator.FailedActions);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Bindings_taken_by_other_programs_are_reported_as_failed()
    {
        _hotkeys.MarkTaken("Alt+1");

        _coordinator.Start();

        Assert.Equal(new[] { HotkeyAction.FocusSlot1 }, _coordinator.FailedActions);
        Assert.False(_hotkeys.IsActive("Alt+1"));
        Assert.True(_hotkeys.IsActive("Alt+2"));
    }

    [Fact]
    public void A_registration_that_throws_is_reported_as_failed_and_the_rest_still_register()
    {
        _hotkeys.MarkBroken("Alt+3");
        var raised = 0;
        _coordinator.RegistrationsChanged += (_, _) => raised++;

        _coordinator.Start();

        Assert.Equal(new[] { HotkeyAction.FocusSlot3 }, _coordinator.FailedActions);
        Assert.Equal(DefaultHotkeys.Bindings.Count - 1, _hotkeys.Active.Count);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Focus_hotkeys_go_straight_to_the_switcher()
    {
        _coordinator.Start();

        Assert.True(_hotkeys.Press("Alt+1"));
        Assert.True(_hotkeys.Press("Alt+8"));
        Assert.True(_hotkeys.Press("Alt+`"));
        Assert.True(_hotkeys.Press("Alt+Shift+`"));

        Assert.Equal(new[] { "slot:0", "slot:7", "next", "previous" }, _switcher.Calls);
    }

    [Fact]
    public void Other_hotkeys_are_raised_for_the_ui()
    {
        var requested = new List<HotkeyAction>();
        _coordinator.UiActionRequested += (_, action) => requested.Add(action);
        _coordinator.Start();

        Assert.True(_hotkeys.Press("Ctrl+Alt+F9"));
        Assert.True(_hotkeys.Press("Ctrl+Alt+F10"));
        Assert.True(_hotkeys.Press("Ctrl+Alt+F11"));
        Assert.True(_hotkeys.Press("Ctrl+Alt+F12"));

        Assert.Equal(
            new[] { HotkeyAction.ToggleSwitcher, HotkeyAction.ToggleCommandCenter, HotkeyAction.ShowMainWindow, HotkeyAction.ToggleNameBadges },
            requested);
        Assert.Empty(_switcher.Calls);
    }

    [Fact]
    public void Changing_hotkey_settings_re_registers_everything()
    {
        _coordinator.Start();
        var raised = 0;
        _coordinator.RegistrationsChanged += (_, _) => raised++;

        _settings.Update(settings => settings with
        {
            Hotkeys = settings.Hotkeys with
            {
                Bindings = new Dictionary<HotkeyAction, string>
                {
                    [HotkeyAction.NextClient] = "F2",
                    [HotkeyAction.ShowMainWindow] = string.Empty,
                    [HotkeyAction.ToggleNameBadges] = "Ctrl+Nonsense",
                    [HotkeyAction.ToggleCommandCenter] = "Alt+1",
                },
            },
        });

        Assert.Equal(1, raised);
        Assert.True(_hotkeys.IsActive("F2"));
        Assert.False(_hotkeys.IsActive("Alt+`"));
        Assert.False(_hotkeys.IsActive("Ctrl+Alt+F11"));
        Assert.False(_hotkeys.IsActive("Ctrl+Alt+F10"));

        // Unbound (empty) is not a failure; an unparseable binding and a duplicate are.
        Assert.Equal(new[] { HotkeyAction.ToggleCommandCenter, HotkeyAction.ToggleNameBadges }, _coordinator.FailedActions);
        Assert.True(_hotkeys.Press("F2"));
        Assert.Equal(new[] { "next" }, _switcher.Calls);
    }

    [Fact]
    public void Unrelated_settings_changes_do_not_re_register()
    {
        _coordinator.Start();
        var registrations = _hotkeys.RegisterCalls;

        _settings.Update(settings => settings with { Audio = settings.Audio with { UnfocusedVolumePercent = 25 } });

        Assert.Equal(registrations, _hotkeys.RegisterCalls);
    }

    [Fact]
    public void Nothing_is_registered_when_hotkeys_are_disabled()
    {
        _settings.Update(settings => settings with { Hotkeys = settings.Hotkeys with { Enabled = false } });

        _coordinator.Start();

        Assert.Empty(_hotkeys.Active);
        Assert.Empty(_coordinator.FailedActions);

        _settings.Update(settings => settings with { Hotkeys = settings.Hotkeys with { Enabled = true } });
        Assert.Equal(DefaultHotkeys.Bindings.Count, _hotkeys.Active.Count);
    }

    [Fact]
    public void Stop_unregisters_everything()
    {
        _hotkeys.MarkTaken("Alt+2");
        _coordinator.Start();

        _coordinator.Stop();

        Assert.Empty(_hotkeys.Active);
        Assert.Empty(_coordinator.FailedActions);

        // Settings changes after Stop are ignored.
        _settings.Update(settings => settings with { Hotkeys = settings.Hotkeys with { Enabled = false } });
        _settings.Update(settings => settings with { Hotkeys = settings.Hotkeys with { Enabled = true } });
        Assert.Empty(_hotkeys.Active);
    }
}
