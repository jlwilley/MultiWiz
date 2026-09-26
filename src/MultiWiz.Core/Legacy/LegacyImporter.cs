using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using MultiWiz.Core.Accounts;
using MultiWiz.Core.Games;
using MultiWiz.Core.Security;
using MultiWiz.Core.Settings;
using MultiWiz.Core.Storage;

namespace MultiWiz.Core.Legacy;

/// <summary>
/// Imports MultiWiz 3 data: <c>config.txt</c> (one account per line, <c>name,user,pass[,Server]</c>, user and pass as
/// Base64 DPAPI blobs or, in old files, plain text) and <c>settings.txt</c> (<c>key=value</c> lines).
/// Only reads the v3 files; never changes or deletes them.
/// </summary>
public sealed class LegacyImporter : ILegacyImporter
{
    private const int MinimumReadyDelaySeconds = 2;
    private const int MaximumReadyDelaySeconds = 30;

    private readonly AppPaths _paths;
    private readonly ISecretProtector _protector;
    private readonly IAccountStore _accounts;
    private readonly ICredentialVault _vault;
    private readonly ISettingsStore _settings;
    private readonly ILogger<LegacyImporter> _logger;

    public LegacyImporter(
        AppPaths paths,
        ISecretProtector protector,
        IAccountStore accounts,
        ICredentialVault vault,
        ISettingsStore settings,
        ILogger<LegacyImporter> logger)
    {
        _paths = paths;
        _protector = protector;
        _accounts = accounts;
        _vault = vault;
        _settings = settings;
        _logger = logger;
    }

    public LegacyImportPreview? ReadPreview()
    {
        if (!File.Exists(_paths.LegacyConfigFile))
        {
            return null;
        }

        try
        {
            var accounts = new List<LegacyAccount>();
            foreach (var line in File.ReadAllLines(_paths.LegacyConfigFile))
            {
                if (ParseAccount(line) is { } account)
                {
                    accounts.Add(account);
                }
            }

            return new LegacyImportPreview(accounts, ReadSettings());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "The MultiWiz 3 files could not be read");
            return null;
        }
    }

    public int Apply(LegacyImportPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var existing in _accounts.GetAll())
        {
            known.Add(DuplicateKey(existing.Username, existing.RealmId));
        }

        var added = 0;
        foreach (var legacy in preview.Accounts)
        {
            if (!known.Add(DuplicateKey(legacy.Username, legacy.RealmId)))
            {
                _logger.LogInformation("Skipped {DisplayName}: an account with that username and realm already exists", legacy.DisplayName);
                continue;
            }

            var account = new Account
            {
                Id = Guid.NewGuid(),
                DisplayName = legacy.DisplayName,
                Username = legacy.Username,
                Game = legacy.Game,
                RealmId = legacy.RealmId,
            };

            // Save the password first so the account never shows up without it.
            if (!string.IsNullOrEmpty(legacy.Password) && !_vault.Save(account.Id, legacy.Username, legacy.Password))
            {
                _logger.LogWarning("The password of {DisplayName} could not be saved", legacy.DisplayName);
            }

            _accounts.Upsert(account);
            added++;
        }

        _settings.Update(current => MapSettings(current, preview.Settings) with { LegacyImportHandled = true });
        _logger.LogInformation("Imported {Count} accounts from MultiWiz 3", added);
        return added;
    }

    public void Decline() => _settings.Update(current => current with { LegacyImportHandled = true });

    private LegacyAccount? ParseAccount(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var fields = line.Split(',');
        if (fields.Length < 2)
        {
            _logger.LogWarning("Skipped a MultiWiz 3 account line without a username");
            return null;
        }

        var username = Reveal(fields[1], out var usernameWasPlainText);
        if (string.IsNullOrWhiteSpace(username))
        {
            _logger.LogWarning("Skipped a MultiWiz 3 account line without a username");
            return null;
        }

        var password = string.Empty;
        var passwordWasPlainText = false;
        if (fields.Length > 2)
        {
            password = Reveal(fields[2], out passwordWasPlainText);
        }

        var (game, realmId) = MapServer(fields.Length > 3 ? fields[3].Trim() : null);
        var displayName = fields[0].Trim();
        return new LegacyAccount(
            displayName.Length > 0 ? displayName : username,
            username,
            password,
            game,
            realmId,
            usernameWasPlainText || passwordWasPlainText);
    }

    /// <summary>Decrypts a v3 field (Base64 of a CurrentUser DPAPI blob of UTF-8 text), falling back to plain text like v3 did.</summary>
    private string Reveal(string field, out bool wasPlainText)
    {
        if (field.Length == 0)
        {
            wasPlainText = false;
            return field;
        }

        byte[]? plain = null;
        try
        {
            plain = _protector.Unprotect(Convert.FromBase64String(field));
            wasPlainText = false;
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            wasPlainText = true;
            return field;
        }
        finally
        {
            if (plain is not null)
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }
    }

    private LegacySettings ReadSettings()
    {
        bool? darkMode = null;
        int? wait = null;
        bool? mute = null;
        int? unmuteVolume = null;
        double? opacity = null;

        if (File.Exists(_paths.LegacySettingsFile))
        {
            foreach (var line in File.ReadAllLines(_paths.LegacySettingsFile))
            {
                var separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                var value = line[(separator + 1)..].Trim();
                switch (line[..separator].Trim().ToUpperInvariant())
                {
                    case "ISDARKMODEENABLED":
                        darkMode = ParseBool(value) ?? darkMode;
                        break;
                    case "WAIT":
                        wait = ParseInt(value) ?? wait;
                        break;
                    case "_MUTEWHENNOTINFOCUS":
                        mute = ParseBool(value) ?? mute;
                        break;
                    case "_UNMUTEVOLUME":
                        unmuteVolume = ParseInt(value) ?? unmuteVolume;
                        break;
                    case "_SWITCHEROPACITY":
                        opacity = ParseDouble(value) ?? opacity;
                        break;
                }
            }
        }

        return new LegacySettings(darkMode, wait, mute, unmuteVolume, opacity);
    }

    private static AppSettings MapSettings(AppSettings settings, LegacySettings legacy)
    {
        var result = settings;
        if (legacy.DarkMode is { } darkMode)
        {
            result = result with { General = result.General with { Theme = darkMode ? ThemePreference.Dark : ThemePreference.Light } };
        }

        if (legacy.LoginWaitSeconds is { } wait)
        {
            var readyDelay = Math.Clamp(wait, MinimumReadyDelaySeconds, MaximumReadyDelaySeconds);
            result = result with { Login = result.Login with { ReadyDelaySeconds = readyDelay } };
        }

        if (legacy.MuteWhenUnfocused is { } mute)
        {
            result = result with { Audio = result.Audio with { Enabled = mute } };
        }

        if (legacy.UnmuteVolume is { } volume)
        {
            result = result with { Audio = result.Audio with { FocusedVolumePercent = Math.Clamp(volume, 0, 100) } };
        }

        if (legacy.SwitcherOpacity is { } opacity && double.IsFinite(opacity))
        {
            var clamped = Math.Clamp(opacity, JsonSettingsStore.MinimumSwitcherOpacity, 1.0);
            result = result with { Switcher = result.Switcher with { Opacity = clamped } };
        }

        return result;
    }

    private static (GameKind Game, string RealmId) MapServer(string? server) => server?.ToUpperInvariant() switch
    {
        "WIZARD101_EU" => (GameKind.Wizard101, BuiltInRealms.Wizard101EU.Id),
        "PIRATE101_US" => (GameKind.Pirate101, BuiltInRealms.Pirate101US.Id),
        _ => (GameKind.Wizard101, BuiltInRealms.Wizard101US.Id),
    };

    private static string DuplicateKey(string username, string realmId) => $"{username.Trim()}\n{realmId}";

    private static bool? ParseBool(string value) => bool.TryParse(value, out var result) ? result : null;

    private static int? ParseInt(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;

    private static double? ParseDouble(string value)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var invariant))
        {
            return invariant;
        }

        // MultiWiz 3 wrote this value with the user's culture (e.g. "0,95").
        return double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var local) ? local : null;
    }
}
