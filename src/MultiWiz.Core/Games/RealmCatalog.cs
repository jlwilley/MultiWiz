using MultiWiz.Core.Settings;

namespace MultiWiz.Core.Games;

/// <summary>
/// Built-in realms followed by the user's custom realms from <see cref="AppSettings.CustomRealms"/>.
/// Custom realms cannot replace a built-in realm or an earlier custom realm with the same id, and custom realms without
/// a login host or with a login port outside 1..65535 are left out.
/// </summary>
public sealed class RealmCatalog : IRealmCatalog
{
    private readonly ISettingsStore _settings;

    public RealmCatalog(ISettingsStore settings)
    {
        _settings = settings;
    }

    public IReadOnlyList<Realm> GetAll()
    {
        var realms = new List<Realm>(BuiltInRealms.All);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var realm in realms)
        {
            ids.Add(realm.Id);
        }

        foreach (var realm in _settings.Current.CustomRealms)
        {
            if (string.IsNullOrWhiteSpace(realm.Id)
                || string.IsNullOrWhiteSpace(realm.LoginHost)
                || realm.LoginPort is < 1 or > 65535
                || !ids.Add(realm.Id))
            {
                continue;
            }

            realms.Add(realm.IsBuiltIn ? realm with { IsBuiltIn = false } : realm);
        }

        return realms;
    }

    public Realm? Find(string realmId) =>
        GetAll().FirstOrDefault(realm => string.Equals(realm.Id, realmId, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<Realm> ForGame(GameKind game) => GetAll().Where(realm => realm.Game == game).ToArray();

    public Realm DefaultFor(GameKind game) => BuiltInRealms.DefaultFor(game);
}
