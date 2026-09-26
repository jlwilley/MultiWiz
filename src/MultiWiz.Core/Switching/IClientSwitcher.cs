using MultiWiz.Core.Sessions;

namespace MultiWiz.Core.Switching;

/// <summary>
/// Keeps the ordered list of switchable clients and moves focus between them.
/// Order: the active team's slot order first, then any other running clients by account order, then clients started
/// outside MultiWiz (see <see cref="ClientSession.IsExternal"/>) by label.
/// Tracks focus changes made outside MultiWiz (alt-tab, clicking) so <see cref="Current"/> stays accurate,
/// and applies audio/performance policies to the focused and background clients.
/// </summary>
public interface IClientSwitcher
{
    /// <summary>Alive sessions that have a window, in slot order.</summary>
    IReadOnlyList<ClientSession> OrderedSessions { get; }

    /// <summary>The game client that most recently had focus, if it is still alive.</summary>
    ClientSession? Current { get; }

    Guid? ActiveTeamId { get; }

    void SetActiveTeam(Guid? teamId);

    /// <summary>Focuses the client in the given zero-based slot. Returns false if the slot is empty.</summary>
    bool FocusSlot(int slotIndex);

    bool FocusNext();
    bool FocusPrevious();
    bool Focus(Guid accountId);

    /// <summary>Raised when the order, the membership, or <see cref="Current"/> changes. Arbitrary thread.</summary>
    event EventHandler? Changed;
}
