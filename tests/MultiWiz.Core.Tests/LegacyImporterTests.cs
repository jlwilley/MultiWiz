using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using MultiWiz.Core.Accounts;
using MultiWiz.Core.Games;
using MultiWiz.Core.Legacy;
using MultiWiz.Core.Settings;
using MultiWiz.Core.Storage;
using MultiWiz.Core.Tests.Fakes;
using MultiWiz.Core.Tests.Support;

namespace MultiWiz.Core.Tests;

public sealed class LegacyImporterTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly AppPaths _paths;
    private readonly FakeSecretProtector _protector = new();
    private readonly FakeAccountStore _accounts = new();
    private readonly FakeCredentialVault _vault = new();
    private readonly FakeSettingsStore _settings = new();
    private readonly LegacyImporter _importer;

    public LegacyImporterTests()
    {
        _paths = _temp.CreateAppPaths();
        Directory.CreateDirectory(_paths.RoamingRoot);
        _importer = new LegacyImporter(_paths, _protector, _accounts, _vault, _settings, NullLogger<LegacyImporter>.Instance);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Returns_null_when_there_is_no_v3_config()
    {
        Assert.Null(_importer.ReadPreview());
    }

    [Fact]
    public void Reads_encrypted_accounts_and_maps_servers()
    {
        WriteConfig(
            Line("Storm Main", "stormuser", "st0rm!pass", "Wizard101_US"),
            Line("Euro Life", "eurouser", "europass", "Wizard101_EU"),
            Line("Pirate", "pirateuser", "arr", "Pirate101_US"));

        var preview = _importer.ReadPreview();

        Assert.NotNull(preview);
        Assert.Collection(
            preview.Accounts,
            account => Assert.Equal(new LegacyAccount("Storm Main", "stormuser", "st0rm!pass", GameKind.Wizard101, "w101-us", false), account),
            account => Assert.Equal(new LegacyAccount("Euro Life", "eurouser", "europass", GameKind.Wizard101, "w101-eu", false), account),
            account => Assert.Equal(new LegacyAccount("Pirate", "pirateuser", "arr", GameKind.Pirate101, "p101-us", false), account));
    }

    [Fact]
    public void Falls_back_to_plain_text_like_v3_did()
    {
        WriteConfig(
            "Old Format,plainuser,plainpass",
            // Valid Base64 that is not a DPAPI blob of this user is also taken literally.
            $"Base64 Lookalike,abcd1234,{_protector.ProtectToBase64("secret")}",
            "Unknown Server,someone,pw,Toontown_US",
            string.Empty,
            "   ",
            "Just a name");

        var preview = _importer.ReadPreview();

        Assert.NotNull(preview);
        Assert.Equal(3, preview.Accounts.Count);
        Assert.Equal(new LegacyAccount("Old Format", "plainuser", "plainpass", GameKind.Wizard101, "w101-us", true), preview.Accounts[0]);
        Assert.Equal(new LegacyAccount("Base64 Lookalike", "abcd1234", "secret", GameKind.Wizard101, "w101-us", true), preview.Accounts[1]);
        Assert.Equal("w101-us", preview.Accounts[2].RealmId);
        Assert.Equal(GameKind.Wizard101, preview.Accounts[2].Game);
    }

    [Fact]
    public void Reads_v3_settings()
    {
        WriteConfig(Line("A", "a", "a"));
        WriteSettings("IsDarkModeEnabled=False", "Wait=45", "_muteWhenNotInFocus=True", "_unmuteVolume=80", "_switcherOpacity=0.5", "Unrelated=1", "garbage");

        var settings = _importer.ReadPreview()!.Settings;

        Assert.Equal(new LegacySettings(false, 45, true, 80, 0.5), settings);
    }

    [Fact]
    public void Missing_settings_file_leaves_everything_unset()
    {
        WriteConfig(Line("A", "a", "a"));

        Assert.Equal(new LegacySettings(null, null, null, null, null), _importer.ReadPreview()!.Settings);
    }

    [Fact]
    public void Reads_opacity_written_with_a_decimal_comma()
    {
        WriteConfig(Line("A", "a", "a"));
        WriteSettings("_switcherOpacity=0,75");
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            Assert.Equal(0.75, _importer.ReadPreview()!.Settings.SwitcherOpacity);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Apply_adds_accounts_saves_passwords_and_maps_settings()
    {
        var existing = _accounts.Add("Existing", realmId: "w101-us");
        WriteConfig(
            Line("Storm Main", "stormuser", "p1", "Wizard101_US"),
            Line("Duplicate of existing", existing.Username.ToUpperInvariant(), "p2"),
            Line("Pirate", "pirateuser", "p3", "Pirate101_US"),
            Line("Same user other realm", "stormuser", "p4", "Wizard101_EU"),
            Line("Duplicate in file", "stormuser", "p5", "Wizard101_US"));
        WriteSettings("IsDarkModeEnabled=False", "Wait=1", "_muteWhenNotInFocus=False", "_unmuteVolume=150", "_switcherOpacity=0.05");
        var configBefore = File.ReadAllBytes(_paths.LegacyConfigFile);
        var settingsBefore = File.ReadAllBytes(_paths.LegacySettingsFile);

        var added = _importer.Apply(_importer.ReadPreview()!);

        Assert.Equal(3, added);
        var all = _accounts.GetAll();
        Assert.Equal(new[] { "Existing", "Storm Main", "Pirate", "Same user other realm" }, all.Select(account => account.DisplayName).ToArray());
        var pirate = all[2];
        Assert.Equal(GameKind.Pirate101, pirate.Game);
        Assert.Equal("p101-us", pirate.RealmId);
        Assert.Equal("p3", _vault.GetPassword(pirate.Id));
        Assert.Equal("pirateuser", _vault.GetUsername(pirate.Id));
        Assert.Equal("p1", _vault.GetPassword(all[1].Id));
        Assert.Equal("p4", _vault.GetPassword(all[3].Id));
        Assert.Null(_vault.GetPassword(existing.Id));

        var settings = _settings.Current;
        Assert.True(settings.LegacyImportHandled);
        Assert.Equal(ThemePreference.Light, settings.General.Theme);
        Assert.Equal(2, settings.Login.ReadyDelaySeconds);
        Assert.False(settings.Audio.Enabled);
        Assert.Equal(100, settings.Audio.FocusedVolumePercent);
        Assert.Equal(JsonSettingsStore.MinimumSwitcherOpacity, settings.Switcher.Opacity);

        // The v3 files are never modified.
        Assert.Equal(configBefore, File.ReadAllBytes(_paths.LegacyConfigFile));
        Assert.Equal(settingsBefore, File.ReadAllBytes(_paths.LegacySettingsFile));
    }

    [Fact]
    public void Apply_keeps_current_settings_that_v3_did_not_have()
    {
        _settings.Update(settings => settings with { Login = settings.Login with { StaggerSeconds = 9 } });
        WriteConfig(Line("A", "a", "a"));
        WriteSettings("Wait=45");

        _importer.Apply(_importer.ReadPreview()!);

        Assert.Equal(30, _settings.Current.Login.ReadyDelaySeconds);
        Assert.Equal(9, _settings.Current.Login.StaggerSeconds);
        Assert.Equal(new AppSettings().Audio, _settings.Current.Audio);
    }

    [Fact]
    public void Decline_only_marks_the_import_as_handled()
    {
        WriteConfig(Line("A", "a", "a"));

        _importer.Decline();

        Assert.True(_settings.Current.LegacyImportHandled);
        Assert.Empty(_accounts.GetAll());
        Assert.True(File.Exists(_paths.LegacyConfigFile));
    }

    private string Line(string name, string username, string password, string? server = null)
    {
        var line = $"{name},{_protector.ProtectToBase64(username)},{_protector.ProtectToBase64(password)}";
        return server is null ? line : $"{line},{server}";
    }

    private void WriteConfig(params string[] lines) => File.WriteAllLines(_paths.LegacyConfigFile, lines);

    private void WriteSettings(params string[] lines) => File.WriteAllLines(_paths.LegacySettingsFile, lines);
}
