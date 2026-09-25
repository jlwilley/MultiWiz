namespace MultiWiz.Core.Hotkeys;

/// <summary>Registers the configured global hotkeys and routes them to the switcher or the UI.</summary>
public interface IHotkeyCoordinator : IDisposable
{
    void Start();
    void Stop();

    /// <summary>Actions whose binding could not be registered (usually taken by another program).</summary>
    IReadOnlyList<HotkeyAction> FailedActions { get; }

    /// <summary>Raised for actions the UI handles (ToggleSwitcher, ToggleCommandCenter, ShowMainWindow, ToggleNameBadges). Platform message thread.</summary>
    event EventHandler<HotkeyAction>? UiActionRequested;

    /// <summary>Raised after (re)registration; check <see cref="FailedActions"/>.</summary>
    event EventHandler? RegistrationsChanged;
}
