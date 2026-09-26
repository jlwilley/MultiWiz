using Microsoft.Extensions.Logging.Abstractions;
using MultiWiz.Core.Accounts;
using MultiWiz.Core.Games;
using MultiWiz.Core.Settings;
using MultiWiz.Core.Tests.Fakes;

namespace MultiWiz.Core.Tests;

public sealed class LaunchArgumentsTests
{
    private static readonly GameInstall Standalone = new()
    {
        Id = "standalone-wizard101",
        Game = GameKind.Wizard101,
        Source = InstallSource.Standalone,
        RootPath = @"C:\ProgramData\KingsIsle Entertainment\Wizard101",
    };

    [Fact]
    public void Standard_install_points_the_client_at_the_realm()
    {
        Assert.Equal("-L login.us.wizard101.com 12000", LaunchArguments.Build(BuiltInRealms.Wizard101US, Standalone));
        Assert.Equal("-L login.us.pirate101.com 12000", LaunchArguments.Build(BuiltInRealms.Pirate101US, Standalone with { Game = GameKind.Pirate101 }));
    }

    [Fact]
    public void Steam_install_adds_the_steam_flag_first()
    {
        var steam = Standalone with { Id = "steam-wizard101-799960", Source = InstallSource.Steam, SteamAppId = "799960" };

        Assert.Equal("-ST -L login.eu.wizard101.com 12000", LaunchArguments.Build(BuiltInRealms.Wizard101EU, steam));
    }

    [Fact]
    public void Extra_arguments_come_last()
    {
        var install = Standalone with { ExtraArguments = "  -windowed -noaudio " };
        var realm = new Realm { Id = "private", Game = GameKind.Wizard101, DisplayName = "Private", LoginHost = "login.example.test", LoginPort = 12345 };

        Assert.Equal("-L login.example.test 12345 -windowed -noaudio", LaunchArguments.Build(realm, install));
        Assert.Equal("-ST -L login.example.test 12345 -windowed -noaudio", LaunchArguments.Build(realm, install with { SteamAppId = "799960" }));
    }

    [Fact]
    public void Blank_optional_values_are_ignored()
    {
        var install = Standalone with { ExtraArguments = "   ", SteamAppId = "" };

        Assert.Equal("-L login.us.wizard101.com 12000", LaunchArguments.Build(BuiltInRealms.Wizard101US, install));
    }

    [Fact]
    public void Install_paths_point_into_the_bin_folder()
    {
        Assert.Equal(Path.Combine(Standalone.RootPath, "Bin"), Standalone.BinPath);
        Assert.Equal(Path.Combine(Standalone.RootPath, "Bin", "WizardGraphicalClient.exe"), Standalone.ExecutablePath);
        Assert.EndsWith("PirateGraphicalClient.exe", (Standalone with { Game = GameKind.Pirate101 }).ExecutablePath);
    }
}

public sealed class RealmCatalogTests
{
    private static readonly Realm CustomRealm = new()
    {
        Id = "my-realm", Game = GameKind.Wizard101, DisplayName = "My realm", LoginHost = "login.example.test", LoginPort = 13000,
    };

    [Fact]
    public void Built_in_realms_are_always_available()
    {
        var catalog = new RealmCatalog(new FakeSettingsStore());

        Assert.Equal(new[] { "w101-us", "w101-eu", "w101-test", "p101-us" }, catalog.GetAll().Select(realm => realm.Id).ToArray());
        Assert.All(catalog.GetAll(), realm => Assert.True(realm.IsBuiltIn));
    }

    [Fact]
    public void Custom_realms_follow_the_built_ins()
    {
        var catalog = new RealmCatalog(new FakeSettingsStore(new AppSettings { CustomRealms = [CustomRealm] }));

        Assert.Equal(5, catalog.GetAll().Count);
        Assert.Equal(CustomRealm, catalog.GetAll()[^1]);
        Assert.Equal(CustomRealm, catalog.Find("MY-REALM"));
        Assert.Contains(CustomRealm, catalog.ForGame(GameKind.Wizard101));
        Assert.DoesNotContain(CustomRealm, catalog.ForGame(GameKind.Pirate101));
    }

    [Fact]
    public void Custom_realms_cannot_replace_built_ins_or_claim_to_be_built_in()
    {
        var impostor = CustomRealm with { Id = "w101-us", LoginHost = "evil.example.test" };
        var claimsBuiltIn = CustomRealm with { Id = "another", IsBuiltIn = true };
        var duplicate = CustomRealm with { DisplayName = "Second copy" };
        var noHost = CustomRealm with { Id = "no-host", LoginHost = " " };
        var noPort = CustomRealm with { Id = "no-port", LoginPort = 0 };
        var badPort = CustomRealm with { Id = "bad-port", LoginPort = 70000 };
        var catalog = new RealmCatalog(new FakeSettingsStore(new AppSettings
        {
            CustomRealms = [impostor, CustomRealm, claimsBuiltIn, duplicate, noHost, noPort, badPort],
        }));

        Assert.Equal("login.us.wizard101.com", catalog.Find("w101-us")!.LoginHost);
        Assert.Equal("My realm", catalog.Find("my-realm")!.DisplayName);
        Assert.False(catalog.Find("another")!.IsBuiltIn);
        Assert.Null(catalog.Find("no-host"));
        Assert.Null(catalog.Find("no-port"));
        Assert.Null(catalog.Find("bad-port"));
        Assert.Equal(6, catalog.GetAll().Count);
    }

    [Fact]
    public void Custom_realms_reflect_settings_changes()
    {
        var settings = new FakeSettingsStore();
        var catalog = new RealmCatalog(settings);
        Assert.Null(catalog.Find("my-realm"));

        settings.Update(current => current with { CustomRealms = [CustomRealm] });

        Assert.NotNull(catalog.Find("my-realm"));
    }

    [Fact]
    public void Defaults_per_game()
    {
        var catalog = new RealmCatalog(new FakeSettingsStore());

        Assert.Equal("w101-us", catalog.DefaultFor(GameKind.Wizard101).Id);
        Assert.Equal("p101-us", catalog.DefaultFor(GameKind.Pirate101).Id);
        Assert.Equal(new[] { "p101-us" }, catalog.ForGame(GameKind.Pirate101).Select(realm => realm.Id).ToArray());
    }
}

public sealed class InstallCatalogTests
{
    private readonly FakeInstallLocator _locator = new();

    [Fact]
    public void Discovery_is_cached_until_refresh()
    {
        _locator.Installs.Add(Install("standalone-wizard101", InstallSource.Standalone));
        using var catalog = CreateCatalog(new FakeSettingsStore());
        var changed = 0;
        catalog.Changed += (_, _) => changed++;

        Assert.Single(catalog.GetAll());
        Assert.Single(catalog.GetAll());
        Assert.Equal(1, _locator.DiscoverCalls);

        _locator.Installs.Add(Install("steam-wizard101-799960", InstallSource.Steam));
        catalog.Refresh();

        Assert.Equal(2, _locator.DiscoverCalls);
        Assert.Equal(2, catalog.GetAll().Count);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Lists_standalone_then_steam_then_custom_and_skips_duplicate_ids()
    {
        _locator.Installs.Add(Install("steam-wizard101-799960", InstallSource.Steam));
        _locator.Installs.Add(Install("standalone-wizard101", InstallSource.Standalone));
        var custom = Install("custom-1", InstallSource.Custom);
        var clash = Install("STANDALONE-WIZARD101", InstallSource.Custom);
        using var catalog = CreateCatalog(new FakeSettingsStore(new AppSettings { CustomInstalls = [custom, clash] }));

        Assert.Equal(
            new[] { "standalone-wizard101", "steam-wizard101-799960", "custom-1" },
            catalog.GetAll().Select(install => install.Id).ToArray());
        Assert.Equal(custom, catalog.Find("CUSTOM-1"));
        Assert.Null(catalog.Find("missing"));
    }

    [Fact]
    public void Resolve_prefers_the_accounts_own_install()
    {
        _locator.Installs.Add(Install("standalone-wizard101", InstallSource.Standalone));
        _locator.Installs.Add(Install("steam-wizard101-799960", InstallSource.Steam));
        using var catalog = CreateCatalog(new FakeSettingsStore(PreferredWizardInstall("standalone-wizard101")));

        Assert.Equal("steam-wizard101-799960", catalog.Resolve(CreateAccount(installId: "steam-wizard101-799960"))?.Id);
    }

    [Fact]
    public void Resolve_uses_the_preferred_install_when_the_account_has_none_or_it_is_gone()
    {
        _locator.Installs.Add(Install("standalone-wizard101", InstallSource.Standalone));
        _locator.Installs.Add(Install("steam-wizard101-799960", InstallSource.Steam));
        using var catalog = CreateCatalog(new FakeSettingsStore(PreferredWizardInstall("steam-wizard101-799960")));

        Assert.Equal("steam-wizard101-799960", catalog.Resolve(CreateAccount())?.Id);
        Assert.Equal("steam-wizard101-799960", catalog.Resolve(CreateAccount(installId: "deleted-install"))?.Id);
    }

    [Fact]
    public void Resolve_falls_back_to_standalone_before_steam_before_custom()
    {
        _locator.Installs.Add(Install("steam-wizard101-799960", InstallSource.Steam));
        var custom = Install("custom-1", InstallSource.Custom);
        using (var catalog = CreateCatalog(new FakeSettingsStore(new AppSettings { CustomInstalls = [custom] })))
        {
            Assert.Equal("steam-wizard101-799960", catalog.Resolve(CreateAccount())?.Id);
        }

        _locator.Installs.Add(Install("standalone-wizard101", InstallSource.Standalone));
        using (var catalog = CreateCatalog(new FakeSettingsStore(PreferredWizardInstall("missing") with { CustomInstalls = [custom] })))
        {
            Assert.Equal("standalone-wizard101", catalog.Resolve(CreateAccount())?.Id);
        }

        _locator.Installs.Clear();
        using (var catalog = CreateCatalog(new FakeSettingsStore(new AppSettings { CustomInstalls = [custom] })))
        {
            Assert.Equal("custom-1", catalog.Resolve(CreateAccount())?.Id);
        }
    }

    [Fact]
    public void Resolve_ignores_installs_of_the_other_game()
    {
        _locator.Installs.Add(Install("standalone-wizard101", InstallSource.Standalone));
        _locator.Installs.Add(Install("standalone-pirate101", InstallSource.Standalone, GameKind.Pirate101));
        using var catalog = CreateCatalog(new FakeSettingsStore());

        var pirate = CreateAccount(installId: "standalone-wizard101") with { Game = GameKind.Pirate101 };

        Assert.Equal("standalone-pirate101", catalog.Resolve(pirate)?.Id);
        Assert.Single(catalog.ForGame(GameKind.Pirate101));
    }

    [Fact]
    public void Resolve_returns_null_when_nothing_is_installed()
    {
        using var catalog = CreateCatalog(new FakeSettingsStore());

        Assert.Null(catalog.Resolve(CreateAccount()));
    }

    [Fact]
    public void Changing_custom_installs_raises_changed()
    {
        var settings = new FakeSettingsStore();
        using var catalog = CreateCatalog(settings);
        var changed = 0;
        catalog.Changed += (_, _) => changed++;

        settings.Update(current => current with { Audio = current.Audio with { Enabled = false } });
        Assert.Equal(0, changed);

        settings.Update(current => current with { CustomInstalls = [Install("custom-1", InstallSource.Custom)] });
        Assert.Equal(1, changed);
        Assert.Equal("custom-1", Assert.Single(catalog.GetAll()).Id);
    }

    private InstallCatalog CreateCatalog(FakeSettingsStore settings) =>
        new(_locator, settings, NullLogger<InstallCatalog>.Instance);

    private static AppSettings PreferredWizardInstall(string installId) =>
        new() { PreferredInstallIds = new Dictionary<GameKind, string> { [GameKind.Wizard101] = installId } };

    private static GameInstall Install(string id, InstallSource source, GameKind game = GameKind.Wizard101) => new()
    {
        Id = id,
        Game = game,
        Source = source,
        RootPath = $@"D:\Games\{id}",
    };

    private static Account CreateAccount(string? installId = null) => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = "Tester",
        Username = "tester",
        InstallId = installId,
    };
}
