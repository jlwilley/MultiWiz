namespace MultiWiz.Core.Games;

/// <summary>Built-in realms plus the user's custom realms from settings.</summary>
public interface IRealmCatalog
{
    IReadOnlyList<Realm> GetAll();
    Realm? Find(string realmId);
    IReadOnlyList<Realm> ForGame(GameKind game);
    Realm DefaultFor(GameKind game);
}
