using System.Text.Json;
using Microsoft.Extensions.Logging;
using MultiWiz.Core.Teams;

namespace MultiWiz.Core.Storage;

/// <summary>
/// <see cref="ITeamStore"/> backed by <see cref="AppPaths.TeamsFile"/>. Loads on first use and saves on every
/// mutation. <see cref="Team.SortOrder"/> always equals the team's position in the list.
/// </summary>
public sealed class JsonTeamStore : ITeamStore
{
    private readonly string _path;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<JsonTeamStore> _logger;
    private readonly Lock _lock = new();
    private List<Team>? _teams;
    private Dictionary<string, JsonElement>? _documentExtensionData;
    private string? _recoveredFromCorruptFile;

    public JsonTeamStore(AppPaths paths, TimeProvider timeProvider, ILogger<JsonTeamStore> logger)
    {
        _path = paths.TeamsFile;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public event EventHandler? Changed;

    /// <summary>
    /// Where the teams file was moved because it could not be read (it was not valid JSON), or null. The store then
    /// started without teams, so the UI can tell the user where the old file is. Reading this loads the store if it
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

    public IReadOnlyList<Team> GetAll()
    {
        lock (_lock)
        {
            return EnsureLoaded().ToArray();
        }
    }

    public Team? Find(Guid id)
    {
        lock (_lock)
        {
            return EnsureLoaded().Find(team => team.Id == id);
        }
    }

    public void Upsert(Team team)
    {
        ArgumentNullException.ThrowIfNull(team);

        lock (_lock)
        {
            var next = new List<Team>(EnsureLoaded());
            var index = next.FindIndex(existing => existing.Id == team.Id);
            if (index >= 0)
            {
                next[index] = team with { SortOrder = index };
            }
            else
            {
                next.Add(team with { SortOrder = next.Count });
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
            var index = current.FindIndex(team => team.Id == id);
            if (index < 0)
            {
                return false;
            }

            var next = new List<Team>(current);
            next.RemoveAt(index);
            Commit(Renumber(next));
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void RemoveAccountEverywhere(Guid accountId)
    {
        lock (_lock)
        {
            var current = EnsureLoaded();
            if (!current.Any(team => team.AccountIds.Contains(accountId)))
            {
                return;
            }

            var next = current
                .Select(team => team.AccountIds.Contains(accountId)
                    ? team with { AccountIds = team.AccountIds.Where(id => id != accountId).ToArray() }
                    : team)
                .ToList();
            Commit(next);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private List<Team> EnsureLoaded()
    {
        if (_teams is not null)
        {
            return _teams;
        }

        var document = JsonFileStore.Load(
            _path,
            CoreJsonContext.Default.TeamsDocument,
            _logger,
            _timeProvider,
            CreateFileDefaults(),
            target => _recoveredFromCorruptFile = target);
        _documentExtensionData = document?.ExtensionData;
        var loaded = (document?.Teams ?? [])
            .Where(team => team is not null && team.Id != Guid.Empty)
            .Select(Normalize)
            .DistinctBy(team => team.Id)
            .OrderBy(team => team.SortOrder)
            .ToList();

        _teams = Renumber(loaded);
        return _teams;
    }

    // Team properties the file lacks (e.g. resizeWindows from an older version) keep their C# defaults.
    private static JsonDefaults CreateFileDefaults() =>
        JsonDefaults.From(new TeamsDocument(), CoreJsonContext.Default.TeamsDocument)
            .WithArrayElements(
                nameof(TeamsDocument.Teams),
                JsonDefaults.From(new Team { Id = Guid.Empty, Name = string.Empty }, CoreJsonContext.Default.Team));

    private void Commit(List<Team> next)
    {
        JsonFileStore.Save(_path, new TeamsDocument { Teams = next, ExtensionData = _documentExtensionData }, CoreJsonContext.Default.TeamsDocument);
        _teams = next;
    }

    private static List<Team> Renumber(List<Team> teams)
    {
        for (var i = 0; i < teams.Count; i++)
        {
            if (teams[i].SortOrder != i)
            {
                teams[i] = teams[i] with { SortOrder = i };
            }
        }

        return teams;
    }

    // Hand-edited files may contain nulls where the model does not allow them.
    private static Team Normalize(Team team) =>
        team.Name is null || team.AccountIds is null || team.LayoutId is null
            ? team with
            {
                Name = team.Name ?? string.Empty,
                AccountIds = team.AccountIds ?? [],
                LayoutId = team.LayoutId ?? BuiltInLayouts.NoneId,
            }
            : team;
}
