namespace MultiWiz.Core.Teams;

/// <summary>Persistent list of teams. Thread-safe; saves on every mutation.</summary>
public interface ITeamStore
{
    /// <summary>Snapshot ordered by <see cref="Team.SortOrder"/>.</summary>
    IReadOnlyList<Team> GetAll();
    Team? Find(Guid id);
    void Upsert(Team team);
    bool Remove(Guid id);

    /// <summary>Removes an account id from every team (called when an account is deleted).</summary>
    void RemoveAccountEverywhere(Guid accountId);

    event EventHandler? Changed;
}
