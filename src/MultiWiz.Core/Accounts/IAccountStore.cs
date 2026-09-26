namespace MultiWiz.Core.Accounts;

/// <summary>Persistent list of accounts. Implementations are thread-safe and save on every mutation.</summary>
public interface IAccountStore
{
    /// <summary>Snapshot ordered by <see cref="Account.SortOrder"/>.</summary>
    IReadOnlyList<Account> GetAll();

    Account? Find(Guid id);

    /// <summary>Adds or replaces the account with the same <see cref="Account.Id"/>. New accounts go to the end of the list.</summary>
    void Upsert(Account account);

    bool Remove(Guid id);

    /// <summary>Rewrites <see cref="Account.SortOrder"/> to match <paramref name="orderedIds"/>; unknown ids are ignored, missing ones keep relative order after.</summary>
    void Reorder(IReadOnlyList<Guid> orderedIds);

    /// <summary>Raised after any change, on the thread that made it.</summary>
    event EventHandler? Changed;
}
