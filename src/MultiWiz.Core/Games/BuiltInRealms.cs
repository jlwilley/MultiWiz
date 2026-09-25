namespace MultiWiz.Core.Games;

public static class BuiltInRealms
{
    public static readonly Realm Wizard101US = new()
    {
        Id = "w101-us", Game = GameKind.Wizard101, DisplayName = "Wizard101 (US)",
        LoginHost = "login.us.wizard101.com", LoginPort = 12000, IsBuiltIn = true,
    };

    public static readonly Realm Wizard101EU = new()
    {
        Id = "w101-eu", Game = GameKind.Wizard101, DisplayName = "Wizard101 (EU)",
        LoginHost = "login.eu.wizard101.com", LoginPort = 12000, IsBuiltIn = true,
    };

    public static readonly Realm Wizard101Test = new()
    {
        Id = "w101-test", Game = GameKind.Wizard101, DisplayName = "Wizard101 Test Realm",
        LoginHost = "testlogin.us.wizard101.com", LoginPort = 12000, IsBuiltIn = true,
    };

    public static readonly Realm Pirate101US = new()
    {
        Id = "p101-us", Game = GameKind.Pirate101, DisplayName = "Pirate101 (US)",
        LoginHost = "login.us.pirate101.com", LoginPort = 12000, IsBuiltIn = true,
    };

    public static IReadOnlyList<Realm> All { get; } = [Wizard101US, Wizard101EU, Wizard101Test, Pirate101US];

    public static Realm DefaultFor(GameKind game) => game == GameKind.Pirate101 ? Pirate101US : Wizard101US;
}
