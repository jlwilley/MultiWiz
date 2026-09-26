using System.Text.Json;
using Microsoft.Extensions.Logging;
using MultiWiz.Core.Accounts;
using MultiWiz.Core.Games;

namespace MultiWiz.Core.Storage;

/// <summary>
/// <see cref="IAccountStore"/> backed by <see cref="AppPaths.AccountsFile"/>. Loads on first use and saves on every
/// mutation. <see cref="Account.SortOrder"/> always equals the account's position in the list.
/// </summary>
public sealed class JsonAccountStore : IAccountStore
{
    private readonly string _path;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<JsonAccountStore> _logger;
    private readonly Lock _lock = new();
    private List<Account>? _accounts;
    private Dictionary<string, JsonElement>? _documentExtensionData;
    private string? _recoveredFromCorruptFile;

    public JsonAccountStore(AppPaths paths, TimeProvider timeProvider, ILogger<JsonAccountStore> logger)
    {
        _path = paths.AccountsFile;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public event EventHandler? Changed;

    /// <summary>
    /// Where the accounts file was moved because it could not be read (it was not valid JSON), or null. The store then
    /// started without accounts, so the UI can tell the user where the old file is. Reading this loads the store if it
    /// has not been used yet.
    /// </summary>
    public string? RecoveredFromCorruptFile
    {
        get
        {
            lock (_lock)
            {
                EnsureLoaded();
                return _recoveredFromCorruptFile;
            }
        }
    }

    public IReadOnlyList<Account> GetAll()
    {
        lock (_lock)
        {
            return EnsureLoaded().ToArray();
        }
    }

    public Account? Find(Guid id)
    {
        lock (_lock)
        {
            return EnsureLoaded().Find(account => account.Id == id);
        }
    }

    public void Upsert(Account account)
    {
        ArgumentNullException.ThrowIfNull(account);

        lock (_lock)
        {
            var next = new List<Account>(EnsureLoaded());
            var index = next.FindIndex(existing => existing.Id == account.Id);
            if (index >= 0)
            {
                // Replacing keeps the account's position; Reorder is the way to move it.
                next[index] = account with { SortOrder = index };
            }
            else
            {
                next.Add(account with { SortOrder = next.Count });
            }

            Commit(next);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Remove(Guid id)
    {
        lock (_lock)
        {
            var current = EnsureLoaded();
            var index = current.FindIndex(account => account.Id == id);
            if (index < 0)
            {
                return false;
            }

            var next = new List<Account>(current);
            next.RemoveAt(index);
            Commit(Renumber(next));
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Reorder(IReadOnlyList<Guid> orderedIds)
    {
        ArgumentNullException.ThrowIfNull(orderedIds);

        lock (_lock)
        {
            var current = EnsureLoaded();
            var remaining = current.ToDictionary(account => account.Id);
            var next = new List<Account>(current.Count);
            foreach (var id in orderedIds)
            {
                if (remaining.Remove(id, out var account))
                {
                    next.Add(account);
                }
            }

            next.AddRange(current.Where(account => remaining.ContainsKey(account.Id)));
            if (next.Select(account => account.Id).SequenceEqual(current.Select(account => account.Id)))
            {
                return;
            }

            Commit(Renumber(next));
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private List<Account> EnsureLoaded()
    {
        if (_accounts is not null)
        {
            return _accounts;
        }

        // No JsonDefaults needed: the only Account initializer that differs from default(T) is RealmId, and Normalize
        // derives a missing realm from the account's game instead.
        var document = JsonFileStore.Load(
            _path,
            CoreJsonContext.Default.AccountsDocument,
            _logger,
            _timeProvider,
            onQuarantined: target => _recoveredFromCorruptFile = target);
        _documentExtensionData = document?.ExtensionData;
        var loaded = (document?.Accounts ?? [])
            .Where(account => account is not null && account.Id != Guid.Empty)
            .Select(Normalize)
            .DistinctBy(account => account.Id)
            .OrderBy(account => account.SortOrder)
            .ToList();

        _accounts = Renumber(loaded);
        return _accounts;
    }

    private void Commit(List<Account> next)
    {
        JsonFileStore.Save(_path, new AccountsDocument { Accounts = next, ExtensionData = _documentExtensionData }, CoreJsonContext.Default.AccountsDocument);
        _accounts = next;
    }

    private static List<Account> Renumber(List<Account> accounts)
    {
        for (var i = 0; i < accounts.Count; i++)
        {
            if (accounts[i].SortOrder != i)
            {
                accounts[i] = accounts[i] with { SortOrder = i };
            }
        }

        return accounts;
    }

    // Hand-edited files may contain nulls where the model does not allow them.
    private static Account Normalize(Account account) =>
        account.Username is null || account.DisplayName is null || account.RealmId is null
            ? account with
            {
                Username = account.Username ?? string.Empty,
                DisplayName = account.DisplayName ?? account.Username ?? string.Empty,
                RealmId = account.RealmId ?? BuiltInRealms.DefaultFor(account.Game).Id,
            }
            : account;
}
