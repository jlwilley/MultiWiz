using MultiWiz.Core.Accounts;
using MultiWiz.Core.Games;

namespace MultiWiz.Core.Tests.Fakes;

internal sealed class FakeAccountStore : IAccountStore
{
    private readonly Lock _lock = new();
    private readonly List<Account> _accounts = [];

    public event EventHandler? Changed;

    /// <summary>When set, <see cref="Upsert"/> throws it instead of saving, like a locked or full disk.</summary>
    public Exception? UpsertException { get; set; }

    public Account Add(string displayName, GameKind game = GameKind.Wizard101, string? realmId = null, string? installId = null)
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            DisplayName = displayName,
            Username = displayName.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant() + "_login",
            Game = game,
            RealmId = realmId ?? BuiltInRealms.DefaultFor(game).Id,
            InstallId = installId,
        };
        Upsert(account);
        return Find(account.Id)!;
    }

    public IReadOnlyList<Account> GetAll()
    {
        lock (_lock)
        {
            return _accounts.OrderBy(account => account.SortOrder).ToArray();
        }
    }

    public Account? Find(Guid id)
    {
        lock (_lock)
        {
            return _accounts.Find(account => account.Id == id);
        }
    }

    public void Upsert(Account account)
    {
        if (UpsertException is { } error)
        {
            throw error;
        }

        lock (_lock)
        {
            var index = _accounts.FindIndex(existing => existing.Id == account.Id);
            if (index >= 0)
            {
                _accounts[index] = account with { SortOrder = _accounts[index].SortOrder };
            }
            else
            {
                _accounts.Add(account with { SortOrder = _accounts.Count });
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Remove(Guid id)
    {
        bool removed;
        lock (_lock)
        {
            removed = _accounts.RemoveAll(account => account.Id == id) > 0;
        }

        if (removed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return removed;
    }

    public void Reorder(IReadOnlyList<Guid> orderedIds)
    {
        lock (_lock)
        {
            var ordered = orderedIds.Select(id => _accounts.Find(account => account.Id == id)).OfType<Account>()
                .Concat(_accounts.OrderBy(account => account.SortOrder).Where(account => !orderedIds.Contains(account.Id)))
                .Select((account, index) => account with { SortOrder = index })
                .ToList();
            _accounts.Clear();
            _accounts.AddRange(ordered);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
