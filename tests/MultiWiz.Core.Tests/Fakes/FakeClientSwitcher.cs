using MultiWiz.Core.Sessions;
using MultiWiz.Core.Switching;

namespace MultiWiz.Core.Tests.Fakes;

/// <summary>Records calls as "slot:N", "next", "previous", "focus:guid" and "team:guid".</summary>
internal sealed class FakeClientSwitcher : IClientSwitcher
{
    private readonly Lock _lock = new();
    private readonly List<string> _calls = [];
    private Guid? _activeTeamId;

    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_lock)
            {
                return _calls.ToArray();
            }
        }
    }

    public IReadOnlyList<ClientSession> OrderedSessions => [];

    public ClientSession? Current => null;

    public Guid? ActiveTeamId
    {
        get
        {
            lock (_lock)
            {
                return _activeTeamId;
            }
        }
    }

    public void SetActiveTeam(Guid? teamId)
    {
        lock (_lock)
        {
            _activeTeamId = teamId;
            _calls.Add($"team:{teamId}");
        }
    }

    public bool FocusSlot(int slotIndex) => Record($"slot:{slotIndex}");

    public bool FocusNext() => Record("next");

    public bool FocusPrevious() => Record("previous");

    public bool Focus(Guid accountId) => Record($"focus:{accountId}");

    private bool Record(string call)
    {
        lock (_lock)
        {
            _calls.Add(call);
        }

        return true;
    }
}
