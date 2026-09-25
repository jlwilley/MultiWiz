namespace MultiWiz.Core.Games;

/// <summary>
/// A login server a client can be pointed at with <c>-L host port</c>.
/// Built-in realms ship with the app; users can add custom ones in settings.
/// </summary>
public sealed record Realm
{
    /// <summary>Stable identifier, e.g. "w101-us". Referenced by <see cref="Accounts.Account.RealmId"/>.</summary>
    public required string Id { get; init; }
    public required GameKind Game { get; init; }
    public required string DisplayName { get; init; }
    public required string LoginHost { get; init; }
    public int LoginPort { get; init; } = 12000;
    public bool IsBuiltIn { get; init; }
}
