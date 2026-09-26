using MultiWiz.Core.Teams;

namespace MultiWiz.Core.Tests.Fakes;

internal sealed class FakeTeamStore : ITeamStore
{
    private readonly Lock _lock = new();
    private readonly List<Team> _teams = [];

    public event EventHandler? Changed;

    public Team Add(string name, IReadOnlyList<Guid> accountIds, string layoutId = BuiltInLayouts.NoneId, bool resizeWindows = true)
    {
        var team = new Team { Id = Guid.NewGuid(), Name = name, AccountIds = accountIds, LayoutId = layoutId, ResizeWindows = resizeWindows };
        Upsert(team);
        return Find(team.Id)!;
    }

    public IReadOnlyList<Team> GetAll()
    {
        lock (_lock)
        {
            return _teams.OrderBy(team => team.SortOrder).ToArray();
        }
    }

    public Team? Find(Guid id)
    {
        lock (_lock)
        {
            return _teams.Find(team => team.Id == id);
        }
    }

    public void Upsert(Team team)
    {
        lock (_lock)
        {
            var index = _teams.FindIndex(existing => existing.Id == team.Id);
            if (index >= 0)
            {
                _teams[index] = team;
            }
            else
            {
                _teams.Add(team with { SortOrder = _teams.Count });
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Remove(Guid id)
    {
        bool removed;
        lock (_lock)
        {
            removed = _teams.RemoveAll(team => team.Id == id) > 0;
        }

        if (removed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return removed;
    }

    public void RemoveAccountEverywhere(Guid accountId)
    {
        lock (_lock)
        {
            for (var i = 0; i < _teams.Count; i++)
            {
                _teams[i] = _teams[i] with { AccountIds = _teams[i].AccountIds.Where(id => id != accountId).ToArray() };
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
