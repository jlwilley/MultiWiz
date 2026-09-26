using System.Security;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using MultiWiz.Core.Games;
using MultiWiz.Core.Platform;

namespace MultiWiz.Platform.Windows.Games;

/// <summary>
/// Finds Wizard101 / Pirate101 installs: the standalone installer's default folders, uninstall registry entries,
/// and Steam libraries. Only installs whose client executable exists are returned.
/// </summary>
internal sealed class InstallLocator : IInstallLocator
{
    private const string UninstallKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    private static readonly GameKind[] SupportedGames = [GameKind.Wizard101, GameKind.Pirate101];

    private static readonly (RegistryHive Hive, RegistryView View)[] UninstallRoots =
    [
        (RegistryHive.LocalMachine, RegistryView.Registry64),
        (RegistryHive.LocalMachine, RegistryView.Registry32),
        (RegistryHive.CurrentUser, RegistryView.Default),
    ];

    private readonly ILogger<InstallLocator> _logger;

    public InstallLocator(ILogger<InstallLocator> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<GameInstall> Discover()
    {
        var found = new InstallSet();

        // Order matters for de-duplication by folder: the well-known standalone folders and Steam libraries are
        // classified with certainty; uninstall entries only add folders not seen yet.
        Run("default standalone folders", () => AddDefaultStandaloneInstalls(found));
        Run("Steam libraries", () => AddSteamInstalls(found));
        Run("uninstall registry entries", () => AddUninstallRegistryInstalls(found));

        var installs = found.ToSortedList();
        _logger.LogInformation(
            "Discovered {Count} game install(s): {Installs}",
            installs.Count, string.Join(", ", installs.Select(install => $"{install.Id} = {install.RootPath}")));
        return installs;
    }

    private void Run(string source, Action discover)
    {
        try
        {
            discover();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not search {Source} for game installs.", source);
        }
    }

    private static void AddDefaultStandaloneInstalls(InstallSet found)
    {
        foreach (var game in SupportedGames)
        {
            found.TryAdd(CreateStandalone(game, GameExecutables.DefaultStandaloneRoot(game)));
        }
    }

    private void AddSteamInstalls(InstallSet found)
    {
        var steamPath = SteamRegistry.GetSteamPath();
        if (steamPath is null)
        {
            return;
        }

        var libraries = new List<string> { steamPath };
        var libraryFile = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (File.Exists(libraryFile))
        {
            string? libraryText = null;
            try
            {
                libraryText = File.ReadAllText(libraryFile);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug(ex, "Could not read the Steam library list {File}; searching only {SteamPath}.", libraryFile, steamPath);
            }

            if (libraryText is not null)
            {
                foreach (var library in SteamLibraryParser.ParseLibraryFolders(libraryText))
                {
                    if (InstallPaths.NormalizeDirectory(library) is { } normalized)
                    {
                        libraries.Add(normalized);
                    }
                }
            }
        }

        foreach (var library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            // One unreachable library (a disconnected network or removable drive) must not hide the others.
            try
            {
                AddSteamLibrary(found, library);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                _logger.LogDebug(ex, "Could not scan Steam library {Library}.", library);
            }
        }
    }

    private void AddSteamLibrary(InstallSet found, string library)
    {
        var steamApps = Path.Combine(library, "steamapps");
        if (!Directory.Exists(steamApps))
        {
            return;
        }

        foreach (var manifestPath in Directory.EnumerateFiles(steamApps, "appmanifest_*.acf"))
        {
            try
            {
                AddSteamManifest(found, steamApps, File.ReadAllText(manifestPath));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug(ex, "Could not read Steam manifest {Manifest}.", manifestPath);
            }
        }
    }

    private static void AddSteamManifest(InstallSet found, string steamApps, string manifestText)
    {
        if (SteamLibraryParser.ParseAppManifest(manifestText) is not { } manifest)
        {
            return;
        }

        foreach (var game in SupportedGames)
        {
            if (!string.Equals(manifest.InstallDir, GameExecutables.SteamInstallDirName(game), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            found.TryAdd(new GameInstall
            {
                Id = $"steam-{Slug(game)}-{manifest.AppId}",
                Game = game,
                Source = InstallSource.Steam,
                RootPath = Path.Combine(steamApps, "common", manifest.InstallDir),
                DisplayName = $"{game} (Steam)",
                SteamAppId = manifest.AppId,
            });
        }
    }

    private void AddUninstallRegistryInstalls(InstallSet found)
    {
        foreach (var (hive, view) in UninstallRoots)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = baseKey.OpenSubKey(UninstallKeyPath);
                if (uninstall is not null)
                {
                    AddUninstallEntries(found, uninstall);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                _logger.LogDebug(ex, "Could not read the uninstall entries in {Hive} ({View}).", hive, view);
            }
        }
    }

    private void AddUninstallEntries(InstallSet found, RegistryKey uninstall)
    {
        foreach (var entryName in uninstall.GetSubKeyNames())
        {
            // Steam registers its games here too ("Steam App <id>"); those are found through the Steam libraries.
            if (entryName.StartsWith("Steam App ", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                using var entry = uninstall.OpenSubKey(entryName);
                if (entry?.GetValue("DisplayName") is not string displayName
                    || entry.GetValue("InstallLocation") is not string installLocation)
                {
                    continue;
                }

                foreach (var game in SupportedGames)
                {
                    if (displayName.StartsWith(game.ToString(), StringComparison.OrdinalIgnoreCase)
                        && ResolveRoot(game, installLocation) is { } root)
                    {
                        found.TryAdd(CreateStandalone(game, root));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                _logger.LogDebug(ex, "Could not read uninstall entry {Entry}.", entryName);
            }
        }
    }

    /// <summary>The game root for an uninstall entry's InstallLocation, which may point at the root or at its Bin folder.</summary>
    private static string? ResolveRoot(GameKind game, string installLocation)
    {
        var location = InstallPaths.NormalizeDirectory(installLocation);
        if (location is null)
        {
            return null;
        }

        var executable = GameExecutables.ClientExecutableName(game);
        if (File.Exists(Path.Combine(location, "Bin", executable)))
        {
            return location;
        }

        return string.Equals(Path.GetFileName(location), "Bin", StringComparison.OrdinalIgnoreCase)
               && File.Exists(Path.Combine(location, executable))
            ? Path.GetDirectoryName(location)
            : null;
    }

    private static GameInstall CreateStandalone(GameKind game, string root)
    {
        var normalizedRoot = InstallPaths.NormalizeDirectory(root) ?? root;
        var defaultRoot = InstallPaths.NormalizeDirectory(GameExecutables.DefaultStandaloneRoot(game));
        var id = string.Equals(normalizedRoot, defaultRoot, StringComparison.OrdinalIgnoreCase)
            ? $"standalone-{Slug(game)}"
            : $"standalone-{Slug(game)}-{ShortHash(normalizedRoot)}";

        return new GameInstall
        {
            Id = id,
            Game = game,
            Source = InstallSource.Standalone,
            RootPath = normalizedRoot,
            DisplayName = $"{game} (standalone)",
        };
    }

    private static string Slug(GameKind game) => game.ToString().ToLowerInvariant();

    /// <summary>First 8 hex characters of the SHA-256 of the lower-cased root, so ids stay stable across runs.</summary>
    private static string ShortHash(string root)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(root.ToLowerInvariant()));
        return Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
    }

    /// <summary>Discovered installs, de-duplicated by root folder (case-insensitive) and id; first one wins.</summary>
    private sealed class InstallSet
    {
        private readonly List<GameInstall> _installs = new();
        private readonly HashSet<string> _roots = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _ids = new(StringComparer.Ordinal);

        public void TryAdd(GameInstall install)
        {
            var root = InstallPaths.NormalizeDirectory(install.RootPath);
            if (root is null || !File.Exists(install.ExecutablePath) || _ids.Contains(install.Id) || !_roots.Add(root))
            {
                return;
            }

            _ids.Add(install.Id);
            _installs.Add(install);
        }

        public List<GameInstall> ToSortedList() =>
            _installs
                .OrderBy(install => install.Game)
                .ThenBy(install => install.Source)
                .ThenBy(install => install.Id, StringComparer.Ordinal)
                .ToList();
    }
}
