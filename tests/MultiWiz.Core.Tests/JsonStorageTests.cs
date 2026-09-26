using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using MultiWiz.Core.Accounts;
using MultiWiz.Core.Games;
using MultiWiz.Core.Hotkeys;
using MultiWiz.Core.Settings;
using MultiWiz.Core.Storage;
using MultiWiz.Core.Teams;
using MultiWiz.Core.Tests.Support;

namespace MultiWiz.Core.Tests;

public sealed class JsonFileStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Load_returns_null_for_a_missing_file()
    {
        Assert.Null(JsonFileStore.Load(_temp.Combine("missing.json"), CoreJsonContext.Default.AccountsDocument));
    }

    [Fact]
    public void Save_creates_the_folder_and_round_trips()
    {
        var path = _temp.Combine("nested", "folder", "accounts.json");
        var account = new Account { Id = Guid.NewGuid(), DisplayName = "Storm", Username = "storm_login", Notes = "Main" };

        JsonFileStore.Save(path, new AccountsDocument { Accounts = [account] }, CoreJsonContext.Default.AccountsDocument);
        var loaded = JsonFileStore.Load(path, CoreJsonContext.Default.AccountsDocument);

        Assert.NotNull(loaded);
        Assert.Equal(AccountsDocument.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal(account, Assert.Single(loaded.Accounts));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Save_replaces_an_existing_file_completely()
    {
        var path = _temp.Combine("teams.json");
        var first = new Team { Id = Guid.NewGuid(), Name = "First" };
        var second = new Team { Id = Guid.NewGuid(), Name = "Second" };
        JsonFileStore.Save(path, new TeamsDocument { Teams = [first, second] }, CoreJsonContext.Default.TeamsDocument);

        JsonFileStore.Save(path, new TeamsDocument { Teams = [second] }, CoreJsonContext.Default.TeamsDocument);

        var loaded = JsonFileStore.Load(path, CoreJsonContext.Default.TeamsDocument);
        Assert.Equal("Second", Assert.Single(loaded!.Teams).Name);
        Assert.Equal(new[] { "teams.json" }, Directory.GetFiles(_temp.Root).Select(file => Path.GetFileName(file)).ToArray());
    }

    [Fact]
    public void Corrupt_json_is_moved_aside_and_treated_as_missing()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero));
        var path = _temp.Combine("settings.json");
        File.WriteAllText(path, "{ \"general\": { \"theme\": ");

        string? reported = null;

        var loaded = JsonFileStore.Load(
            path, CoreJsonContext.Default.AppSettings, NullLogger.Instance, time, onQuarantined: target => reported = target);

        Assert.Null(loaded);
        Assert.False(File.Exists(path));
        var expected = $"{path}.corrupt-{time.GetLocalNow().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}";
        Assert.True(File.Exists(expected), $"Expected {expected}");
        Assert.Equal("{ \"general\": { \"theme\": ", File.ReadAllText(expected));
        Assert.Equal(expected, reported);
    }

    [Fact]
    public void A_second_corrupt_file_in_the_same_second_gets_its_own_name()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero));
        var path = _temp.Combine("accounts.json");

        File.WriteAllText(path, "not json");
        JsonFileStore.Load(path, CoreJsonContext.Default.AccountsDocument, NullLogger.Instance, time);
        File.WriteAllText(path, "still not json");
        JsonFileStore.Load(path, CoreJsonContext.Default.AccountsDocument, NullLogger.Instance, time);

        Assert.Equal(2, Directory.GetFiles(_temp.Root, "accounts.json.corrupt-*").Length);
    }

    [Fact]
    public void Defaults_fill_in_missing_properties_but_never_override_the_file()
    {
        var path = _temp.Combine("settings.json");
        File.WriteAllText(path, """
            {
              "Login": { "AutoLogin": false, "readyDelaySeconds": 7, "readyDelaySeconds": 8 },
              "hotkeys": { "bindings": { "NextClient": "F2" } },
              "audio": null,
            }
            """);

        var loaded = JsonFileStore.Load(
            path, CoreJsonContext.Default.AppSettings, defaults: JsonDefaults.From(new AppSettings(), CoreJsonContext.Default.AppSettings));

        Assert.NotNull(loaded);
        Assert.Equal(new LoginSettings() with { AutoLogin = false, ReadyDelaySeconds = 8 }, loaded.Login);
        Assert.True(loaded.Hotkeys.Enabled);
        Assert.Equal("F2", Assert.Single(loaded.Hotkeys.Bindings).Value);
        Assert.Equal(new GeneralSettings(), loaded.General);
        Assert.Null(loaded.Audio);
        Assert.Equal(AppSettings.CurrentSchemaVersion, loaded.SchemaVersion);
    }

    [Fact]
    public void A_corrupt_file_held_open_by_another_program_is_still_set_aside()
    {
        var path = _temp.Combine("accounts.json");
        File.WriteAllText(path, "not json");
        string? reported = null;

        // Like a scanner or sync tool that opened the file without FILE_SHARE_DELETE: on Windows the rename fails and
        // a copy is kept instead.
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Null(JsonFileStore.Load(
                path, CoreJsonContext.Default.AccountsDocument, NullLogger.Instance, onQuarantined: target => reported = target));
        }

        Assert.NotNull(reported);
        Assert.Equal("not json", File.ReadAllText(reported));
    }

    [Fact]
    public void Json_null_loads_as_null_without_quarantining()
    {
        var path = _temp.Combine("accounts.json");
        File.WriteAllText(path, "null");

        Assert.Null(JsonFileStore.Load(path, CoreJsonContext.Default.AccountsDocument));
        Assert.True(File.Exists(path));
    }
}

public sealed class JsonAccountStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly AppPaths _paths;

    public JsonAccountStoreTests()
    {
        _paths = _temp.CreateAppPaths();
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Starts_empty_without_a_file()
    {
        var store = CreateStore();

        Assert.Empty(store.GetAll());
        Assert.Null(store.RecoveredFromCorruptFile);
        Assert.False(File.Exists(_paths.AccountsFile));
    }

    [Fact]
    public void Upsert_saves_and_a_new_store_reads_it_back()
    {
        var store = CreateStore();
        var changed = 0;
        store.Changed += (_, _) =>
        {
            // Changed is raised after the file was written.
            Assert.True(File.Exists(_paths.AccountsFile));
            changed++;
        };

        var storm = NewAccount("Storm");
        var fire = NewAccount("Fire") with { Game = GameKind.Pirate101, RealmId = "p101-us", AccentColor = "#FF8800", Notes = "Alt" };
        store.Upsert(storm);
        store.Upsert(fire);

        Assert.Equal(2, changed);
        var reloaded = CreateStore().GetAll();
        Assert.Equal(new[] { storm.Id, fire.Id }, reloaded.Select(account => account.Id).ToArray());
        Assert.Equal(new[] { 0, 1 }, reloaded.Select(account => account.SortOrder).ToArray());
        Assert.Equal(fire with { SortOrder = 1 }, reloaded[1]);
    }

    [Fact]
    public void Upsert_of_an_existing_account_keeps_its_position()
    {
        var store = CreateStore();
        var first = NewAccount("First");
        var second = NewAccount("Second");
        store.Upsert(first);
        store.Upsert(second);

        store.Upsert(first with { DisplayName = "Renamed", SortOrder = 99 });

        var all = store.GetAll();
        Assert.Equal(new[] { "Renamed", "Second" }, all.Select(account => account.DisplayName).ToArray());
        Assert.Equal(0, all[0].SortOrder);
        Assert.Equal("Renamed", store.Find(first.Id)?.DisplayName);
    }

    [Fact]
    public void Reorder_puts_listed_accounts_first_and_keeps_the_rest_in_order()
    {
        var store = CreateStore();
        var accounts = new[] { NewAccount("A"), NewAccount("B"), NewAccount("C"), NewAccount("D") };
        foreach (var account in accounts)
        {
            store.Upsert(account);
        }

        store.Reorder([accounts[2].Id, Guid.NewGuid(), accounts[0].Id]);

        var names = CreateStore().GetAll().Select(account => account.DisplayName).ToArray();
        Assert.Equal(new[] { "C", "A", "B", "D" }, names);
        Assert.Equal(new[] { 0, 1, 2, 3 }, store.GetAll().Select(account => account.SortOrder).ToArray());
    }

    [Fact]
    public void Reorder_without_a_change_does_not_save_or_notify()
    {
        var store = CreateStore();
        var account = NewAccount("Only");
        store.Upsert(account);
        var changed = 0;
        store.Changed += (_, _) => changed++;

        store.Reorder([account.Id]);

        Assert.Equal(0, changed);
    }

    [Fact]
    public void Remove_deletes_and_renumbers()
    {
        var store = CreateStore();
        var first = NewAccount("First");
        var second = NewAccount("Second");
        store.Upsert(first);
        store.Upsert(second);

        Assert.True(store.Remove(first.Id));
        Assert.False(store.Remove(first.Id));

        var remaining = Assert.Single(CreateStore().GetAll());
        Assert.Equal(second.Id, remaining.Id);
        Assert.Equal(0, remaining.SortOrder);
        Assert.Null(store.Find(first.Id));
    }

    [Fact]
    public void Corrupt_file_is_kept_aside_and_the_store_starts_empty()
    {
        Directory.CreateDirectory(_paths.DataDirectory);
        File.WriteAllText(_paths.AccountsFile, "{ \"accounts\": [ { \"id\": ");
        var store = CreateStore();

        Assert.Empty(store.GetAll());
        var quarantined = Assert.Single(Directory.GetFiles(_paths.DataDirectory, "accounts.json.corrupt-*"));
        Assert.Equal(quarantined, store.RecoveredFromCorruptFile);

        store.Upsert(NewAccount("Fresh"));
        Assert.Single(CreateStore().GetAll());
    }

    [Fact]
    public void Tolerates_hand_edited_files()
    {
        Directory.CreateDirectory(_paths.DataDirectory);
        var id = Guid.NewGuid();
        File.WriteAllText(_paths.AccountsFile, $$"""
            {
              // Comments and trailing commas are allowed.
              "schemaVersion": 1,
              "accounts": [
                { "id": "{{id}}", "displayName": "Second", "username": "second", "sortOrder": 5, },
                { "id": "{{Guid.NewGuid()}}", "displayName": "First", "username": "first", "game": "Pirate101", "realmId": "p101-us", "sortOrder": 2 },
                { "id": "{{id}}", "displayName": "Duplicate", "username": "dup", "sortOrder": 9 },
              ],
            }
            """);

        var all = CreateStore().GetAll();

        Assert.Equal(new[] { "First", "Second" }, all.Select(account => account.DisplayName).ToArray());
        Assert.Equal(GameKind.Pirate101, all[0].Game);
        Assert.Equal("w101-us", all[1].RealmId);
    }

    [Fact]
    public void Properties_written_by_a_newer_version_survive_a_save()
    {
        Directory.CreateDirectory(_paths.DataDirectory);
        var id = Guid.NewGuid();
        File.WriteAllText(_paths.AccountsFile, $$"""
            {
              "schemaVersion": 1,
              "futureDocumentSetting": "kept",
              "accounts": [ { "id": "{{id}}", "displayName": "Storm", "username": "storm", "futureOptIn": true } ]
            }
            """);
        var store = CreateStore();

        store.Upsert(NewAccount("Fresh"));

        using var saved = JsonDocument.Parse(File.ReadAllText(_paths.AccountsFile));
        Assert.Equal("kept", saved.RootElement.GetProperty("futureDocumentSetting").GetString());
        var first = saved.RootElement.GetProperty("accounts")[0];
        Assert.Equal(id, first.GetProperty("id").GetGuid());
        Assert.True(first.GetProperty("futureOptIn").GetBoolean());
    }

    private JsonAccountStore CreateStore() => new(_paths, TimeProvider.System, NullLogger<JsonAccountStore>.Instance);

    private static Account NewAccount(string name) => new() { Id = Guid.NewGuid(), DisplayName = name, Username = name.ToLowerInvariant() };
}

public sealed class JsonTeamStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly AppPaths _paths;

    public JsonTeamStoreTests()
    {
        _paths = _temp.CreateAppPaths();
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Teams_round_trip_with_their_slots_and_layout()
    {
        var store = CreateStore();
        var team = new Team
        {
            Id = Guid.NewGuid(),
            Name = "Farm",
            AccountIds = [Guid.NewGuid(), Guid.NewGuid()],
            LayoutId = BuiltInLayouts.Grid2x2.Id,
            ResizeWindows = false,
        };

        store.Upsert(team);

        var loaded = Assert.Single(CreateStore().GetAll());
        Assert.Equal(team.Id, loaded.Id);
        Assert.Equal("Farm", loaded.Name);
        Assert.Equal(team.AccountIds.ToArray(), loaded.AccountIds.ToArray());
        Assert.Equal("grid-2x2", loaded.LayoutId);
        Assert.False(loaded.ResizeWindows);
    }

    [Fact]
    public void Teams_missing_optional_properties_get_their_defaults()
    {
        Directory.CreateDirectory(_paths.DataDirectory);
        var id = Guid.NewGuid();
        File.WriteAllText(_paths.TeamsFile, $$"""{ "teams": [ { "id": "{{id}}", "name": "From an older version" } ] }""");

        var team = Assert.Single(CreateStore().GetAll());

        Assert.Equal(id, team.Id);
        Assert.True(team.ResizeWindows);
        Assert.Equal(BuiltInLayouts.NoneId, team.LayoutId);
        Assert.Empty(team.AccountIds);
    }

    [Fact]
    public void RemoveAccountEverywhere_removes_the_slot_from_every_team()
    {
        var store = CreateStore();
        Guid shared = Guid.NewGuid(), other = Guid.NewGuid();
        store.Upsert(new Team { Id = Guid.NewGuid(), Name = "One", AccountIds = [shared, other] });
        store.Upsert(new Team { Id = Guid.NewGuid(), Name = "Two", AccountIds = [other, shared] });
        store.Upsert(new Team { Id = Guid.NewGuid(), Name = "Three", AccountIds = [other] });
        var changed = 0;
        store.Changed += (_, _) => changed++;

        store.RemoveAccountEverywhere(shared);
        store.RemoveAccountEverywhere(Guid.NewGuid());

        Assert.Equal(1, changed);
        Assert.All(CreateStore().GetAll(), team => Assert.Equal(new[] { other }, team.AccountIds.ToArray()));
    }

    [Fact]
    public void Remove_and_find()
    {
        var store = CreateStore();
        var team = new Team { Id = Guid.NewGuid(), Name = "Temp" };
        store.Upsert(team);

        Assert.NotNull(store.Find(team.Id));
        Assert.True(store.Remove(team.Id));
        Assert.Null(store.Find(team.Id));
        Assert.Empty(CreateStore().GetAll());
    }

    private JsonTeamStore CreateStore() => new(_paths, TimeProvider.System, NullLogger<JsonTeamStore>.Instance);
}

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly AppPaths _paths;

    public JsonSettingsStoreTests()
    {
        _paths = _temp.CreateAppPaths();
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Defaults_when_there_is_no_file()
    {
        var settings = CreateStore().Current;

        Assert.Equal(new AppSettings().Login, settings.Login);
        Assert.Equal(ThemePreference.Dark, settings.General.Theme);
        Assert.False(File.Exists(_paths.SettingsFile));
    }

    [Fact]
    public void Update_saves_and_raises_changed_with_the_new_settings()
    {
        var store = CreateStore();
        AppSettings? raised = null;
        store.Changed += (_, settings) => raised = settings;

        store.Update(settings => settings with { General = settings.General with { Theme = ThemePreference.Light } });

        Assert.Equal(ThemePreference.Light, raised?.General.Theme);
        Assert.Equal(ThemePreference.Light, CreateStore().Current.General.Theme);
    }

    [Fact]
    public void Update_without_a_change_does_not_save_or_notify()
    {
        var store = CreateStore();
        var changed = 0;
        store.Changed += (_, _) => changed++;

        store.Update(settings => settings);
        store.Update(settings => settings with { Login = settings.Login with { } });

        Assert.Equal(0, changed);
        Assert.False(File.Exists(_paths.SettingsFile));
    }

    [Fact]
    public void Invalid_values_are_clamped_on_load()
    {
        Directory.CreateDirectory(_paths.DataDirectory);
        File.WriteAllText(_paths.SettingsFile, """
            {
              "login": { "readyDelaySeconds": -3, "windowTimeoutSeconds": 0, "keystrokeDelayMs": -1, "staggerSeconds": -2 },
              "audio": { "enabled": true, "focusedVolumePercent": 150, "unfocusedVolumePercent": -5 },
              "switcher": { "opacity": 5 }
            }
            """);

        var settings = CreateStore().Current;

        Assert.Equal(0, settings.Login.ReadyDelaySeconds);
        Assert.Equal(1, settings.Login.WindowTimeoutSeconds);
        Assert.Equal(0, settings.Login.KeystrokeDelayMs);
        Assert.Equal(0, settings.Login.StaggerSeconds);
        Assert.Equal(100, settings.Audio.FocusedVolumePercent);
        Assert.Equal(0, settings.Audio.UnfocusedVolumePercent);
        Assert.Equal(1.0, settings.Switcher.Opacity);
        Assert.NotNull(settings.General);
        Assert.NotNull(settings.Hotkeys.Bindings);
        Assert.NotNull(settings.CustomInstalls);
        Assert.NotNull(settings.PreferredInstallIds);
    }

    [Theory]
    [InlineData(2, 8, 4)]
    [InlineData(2, 12, 12)]
    [InlineData(1, 4, 4)]
    [InlineData(1, 8, 8)]
    public void Older_settings_return_to_the_4_second_login_delay_but_keep_custom_values(int schema, int stored, int expected)
    {
        Directory.CreateDirectory(_paths.DataDirectory);
        File.WriteAllText(_paths.SettingsFile, $$"""
            { "schemaVersion": {{schema}}, "login": { "readyDelaySeconds": {{stored}} } }
            """);

        var settings = CreateStore().Current;

        Assert.Equal(AppSettings.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.Equal(expected, settings.Login.ReadyDelaySeconds);
    }

    [Fact]
    public void Properties_missing_from_the_file_keep_their_defaults()
    {
        Directory.CreateDirectory(_paths.DataDirectory);
        File.WriteAllText(_paths.SettingsFile, """
            {
              "general": { "theme": "Light" },
              "Login": { "ReadyDelaySeconds": 7, "AutoLogin": false },
              "hotkeys": {},
              "customRealms": [ { "id": "private", "game": "Wizard101", "displayName": "Private", "loginHost": "login.example.test" } ]
            }
            """);

        var settings = CreateStore().Current;
        var defaults = new AppSettings();

        Assert.Equal(AppSettings.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.Equal(defaults.General with { Theme = ThemePreference.Light }, settings.General);
        Assert.Equal(defaults.Login with { ReadyDelaySeconds = 7, AutoLogin = false }, settings.Login);
        Assert.True(settings.Hotkeys.Enabled);
        Assert.Equal(defaults.Audio, settings.Audio);
        Assert.Equal(defaults.Switcher, settings.Switcher);
        Assert.Equal(12000, Assert.Single(settings.CustomRealms).LoginPort);
    }

    [Fact]
    public void Opacity_below_the_minimum_is_raised()
    {
        var store = CreateStore();

        store.Update(settings => settings with { Switcher = settings.Switcher with { Opacity = 0.01 } });

        Assert.Equal(JsonSettingsStore.MinimumSwitcherOpacity, store.Current.Switcher.Opacity);
    }

    [Fact]
    public void Corrupt_settings_fall_back_to_defaults()
    {
        Directory.CreateDirectory(_paths.DataDirectory);
        File.WriteAllText(_paths.SettingsFile, "][");
        var store = CreateStore();

        Assert.Equal(new AppSettings().Audio, store.Current.Audio);
        var quarantined = Assert.Single(Directory.GetFiles(_paths.DataDirectory, "settings.json.corrupt-*"));
        Assert.Equal(quarantined, store.RecoveredFromCorruptFile);
    }

    [Fact]
    public void Enum_names_from_a_newer_version_do_not_throw_the_settings_away()
    {
        Directory.CreateDirectory(_paths.DataDirectory);
        File.WriteAllText(_paths.SettingsFile, """
            {
              "general": { "theme": "HighContrast", "updateChannel": "Nightly", "closeGamesOnExit": true },
              "login": { "autoLogin": false },
              "hotkeys": { "bindings": { "Screenshot": "Ctrl+Alt+P", "NextClient": "F2", "PreviousClient": null } },
              "preferredInstallIds": { "Wizard102": "custom-2", "Pirate101": "custom-1" },
              "customInstalls": [
                { "id": "custom-1", "game": "Pirate101", "source": "Custom", "rootPath": "E:\\Pirate101" },
                { "id": "custom-2", "game": "Wizard102", "source": "Custom", "rootPath": "E:\\Wizard102" },
                { "id": "custom-3", "game": "Wizard101", "source": "Cloud", "rootPath": "E:\\Cloud" }
              ],
              "customRealms": [
                { "id": "private", "game": "Wizard101", "displayName": "Private", "loginHost": "login.example.test" },
                { "id": "future", "game": "Wizard102", "displayName": "Future", "loginHost": "login.example.test" }
              ],
              "legacyImportHandled": true
            }
            """);
        var store = CreateStore();

        var settings = store.Current;

        Assert.Null(store.RecoveredFromCorruptFile);
        Assert.Empty(Directory.GetFiles(_paths.DataDirectory, "settings.json.corrupt-*"));
        Assert.Equal(ThemePreference.Dark, settings.General.Theme);
        Assert.Equal(UpdateChannel.Stable, settings.General.UpdateChannel);
        Assert.True(settings.General.CloseGamesOnExit);
        Assert.False(settings.Login.AutoLogin);
        var binding = Assert.Single(settings.Hotkeys.Bindings);
        Assert.Equal(HotkeyAction.NextClient, binding.Key);
        Assert.Equal("F2", binding.Value);
        Assert.Equal(new[] { new KeyValuePair<GameKind, string>(GameKind.Pirate101, "custom-1") }, settings.PreferredInstallIds.ToArray());
        Assert.Equal("custom-1", Assert.Single(settings.CustomInstalls).Id);
        Assert.Equal("private", Assert.Single(settings.CustomRealms).Id);
        Assert.True(settings.LegacyImportHandled);
    }

    [Fact]
    public void Settings_written_by_a_newer_version_survive_a_save()
    {
        Directory.CreateDirectory(_paths.DataDirectory);
        File.WriteAllText(_paths.SettingsFile, """
            {
              "futureTopLevel": { "nested": [1, 2] },
              "audio": { "enabled": true, "futureAudioOption": 7 }
            }
            """);
        var store = CreateStore();

        store.Update(settings => settings with { Audio = settings.Audio with { UnfocusedVolumePercent = 25 } });

        using var saved = JsonDocument.Parse(File.ReadAllText(_paths.SettingsFile));
        Assert.Equal(2, saved.RootElement.GetProperty("futureTopLevel").GetProperty("nested")[1].GetInt32());
        var audio = saved.RootElement.GetProperty("audio");
        Assert.Equal(7, audio.GetProperty("futureAudioOption").GetInt32());
        Assert.Equal(25, audio.GetProperty("unfocusedVolumePercent").GetInt32());
        Assert.Equal(25, CreateStore().Current.Audio.UnfocusedVolumePercent);
    }

    private JsonSettingsStore CreateStore() => new(_paths, TimeProvider.System, NullLogger<JsonSettingsStore>.Instance);
}

public sealed class AppSettingsJsonTests
{
    [Fact]
    public void Every_setting_round_trips()
    {
        var teamId = Guid.NewGuid();
        var original = new AppSettings
        {
            General = new GeneralSettings
            {
                Theme = ThemePreference.Light, MinimizeToTray = false, CloseToTray = true, CloseGamesOnExit = true,
                CheckForUpdates = false, UpdateChannel = UpdateChannel.Beta,
            },
            Login = new LoginSettings
            {
                AutoLogin = false, ReadyDelaySeconds = 7, WindowTimeoutSeconds = 45, KeystrokeDelayMs = 30, StaggerSeconds = 5,
                RefocusAfterLogin = true,
            },
            Audio = new AudioSettings { Enabled = false, FocusedVolumePercent = 80, UnfocusedVolumePercent = 20 },
            Switcher = new SwitcherSettings { Opacity = 0.5, ShowOnTeamLaunch = true, DoNotStealFocus = false, Left = -1200, Top = 300 },
            Overlays = new OverlaySettings { ShowNameBadges = true },
            Performance = new PerformanceSettings { EfficiencyModeForBackground = true, LowerBackgroundPriority = true },
            Hotkeys = new HotkeySettings
            {
                Enabled = false,
                Bindings = new Dictionary<HotkeyAction, string>
                {
                    [HotkeyAction.NextClient] = "F2",
                    [HotkeyAction.ShowMainWindow] = string.Empty,
                },
            },
            CustomInstalls =
            [
                new GameInstall
                {
                    Id = "custom-1", Game = GameKind.Pirate101, Source = InstallSource.Custom, RootPath = @"E:\Games\Pirate101",
                    DisplayName = "My Pirate", ExtraArguments = "-windowed", SteamAppId = null,
                },
            ],
            PreferredInstallIds = new Dictionary<GameKind, string>
            {
                [GameKind.Wizard101] = "steam-wizard101-799960",
                [GameKind.Pirate101] = "custom-1",
            },
            CustomRealms =
            [
                new Realm { Id = "private", Game = GameKind.Wizard101, DisplayName = "Private", LoginHost = "login.example.test", LoginPort = 12500 },
            ],
            LegacyImportHandled = true,
            LastTeamId = teamId,
        };

        var json = JsonSerializer.Serialize(original, CoreJsonContext.Default.AppSettings);
        var copy = JsonSerializer.Deserialize(json, CoreJsonContext.Default.AppSettings);

        Assert.NotNull(copy);
        Assert.Equal(original.SchemaVersion, copy.SchemaVersion);
        Assert.Equal(original.General, copy.General);
        Assert.Equal(original.Login, copy.Login);
        Assert.Equal(original.Audio, copy.Audio);
        Assert.Equal(original.Overlays, copy.Overlays);
        Assert.Equal(original.Performance, copy.Performance);
        Assert.Equal(original.Switcher, copy.Switcher);
        Assert.Equal(original.Hotkeys.Enabled, copy.Hotkeys.Enabled);
        Assert.Equal(
            original.Hotkeys.Bindings.OrderBy(pair => pair.Key).ToArray(),
            copy.Hotkeys.Bindings.OrderBy(pair => pair.Key).ToArray());
        Assert.Equal(original.CustomInstalls.ToArray(), copy.CustomInstalls.ToArray());
        Assert.Equal(
            original.PreferredInstallIds.OrderBy(pair => pair.Key).ToArray(),
            copy.PreferredInstallIds.OrderBy(pair => pair.Key).ToArray());
        Assert.Equal(original.CustomRealms.ToArray(), copy.CustomRealms.ToArray());
        Assert.True(copy.LegacyImportHandled);
        Assert.Equal(teamId, copy.LastTeamId);
    }

    [Fact]
    public void Uses_camel_case_names_and_enum_names()
    {
        var settings = new AppSettings
        {
            General = new GeneralSettings { Theme = ThemePreference.Light },
            PreferredInstallIds = new Dictionary<GameKind, string> { [GameKind.Pirate101] = "custom-1" },
            Hotkeys = new HotkeySettings { Bindings = new Dictionary<HotkeyAction, string> { [HotkeyAction.ToggleSwitcher] = "F9" } },
            CustomInstalls = [new GameInstall { Id = "custom-1", Game = GameKind.Pirate101, Source = InstallSource.Custom, RootPath = @"E:\Pirate101" }],
        };

        var json = JsonSerializer.Serialize(settings, CoreJsonContext.Default.AppSettings);

        Assert.Contains($"\"schemaVersion\": {AppSettings.CurrentSchemaVersion}", json);
        Assert.Contains("\"theme\": \"Light\"", json);
        Assert.Contains("\"updateChannel\": \"Stable\"", json);
        Assert.Contains("\"preferredInstallIds\"", json);
        Assert.Contains("\"Pirate101\": \"custom-1\"", json);
        Assert.Contains("\"ToggleSwitcher\": \"F9\"", json);
        Assert.Contains("\"source\": \"Custom\"", json);
        Assert.Contains("\"rootPath\"", json);
        Assert.DoesNotContain("binPath", json);
        Assert.DoesNotContain("executablePath", json);
    }

    [Fact]
    public void Reads_enum_names_and_numbers()
    {
        const string json = """{ "general": { "theme": "Light", "updateChannel": 1 } }""";

        var settings = JsonSerializer.Deserialize(json, CoreJsonContext.Default.AppSettings);

        Assert.Equal(ThemePreference.Light, settings?.General.Theme);
        Assert.Equal(UpdateChannel.Beta, settings?.General.UpdateChannel);
    }

    [Fact]
    public void Accounts_and_teams_documents_round_trip()
    {
        var account = new Account
        {
            Id = Guid.NewGuid(), DisplayName = "Myth", Username = "myth_login", Game = GameKind.Wizard101, RealmId = "w101-eu",
            InstallId = "standalone-wizard101", AccentColor = "#8839EF", Notes = "Farmer", SortOrder = 3,
        };
        var team = new Team
        {
            Id = Guid.NewGuid(), Name = "Quad", AccountIds = [account.Id], LayoutId = "grid-2x2", ResizeWindows = false, SortOrder = 1,
        };

        var accounts = JsonSerializer.Deserialize(
            JsonSerializer.Serialize(new AccountsDocument { Accounts = [account] }, CoreJsonContext.Default.AccountsDocument),
            CoreJsonContext.Default.AccountsDocument);
        var teams = JsonSerializer.Deserialize(
            JsonSerializer.Serialize(new TeamsDocument { Teams = [team] }, CoreJsonContext.Default.TeamsDocument),
            CoreJsonContext.Default.TeamsDocument);

        Assert.Equal(account, Assert.Single(accounts!.Accounts));
        var loadedTeam = Assert.Single(teams!.Teams);
        Assert.Equal(team.Id, loadedTeam.Id);
        Assert.Equal(team.Name, loadedTeam.Name);
        Assert.Equal(team.LayoutId, loadedTeam.LayoutId);
        Assert.Equal(team.ResizeWindows, loadedTeam.ResizeWindows);
        Assert.Equal(team.SortOrder, loadedTeam.SortOrder);
        Assert.Equal(team.AccountIds.ToArray(), loadedTeam.AccountIds.ToArray());
    }

    [Fact]
    public void Accounts_missing_required_properties_are_rejected()
    {
        const string json = """{ "accounts": [ { "displayName": "No id or username" } ] }""";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, CoreJsonContext.Default.AccountsDocument));
    }
}
