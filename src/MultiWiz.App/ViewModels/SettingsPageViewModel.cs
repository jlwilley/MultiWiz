using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MultiWiz.App.Services;
using MultiWiz.Core.Accounts;
using MultiWiz.Core.Games;
using MultiWiz.Core.Hotkeys;
using MultiWiz.Core.Settings;

namespace MultiWiz.App.ViewModels;

/// <summary>
/// The Settings page. Simple preferences are two-way bound and saved together after a short pause (so dragging a
/// slider writes the file once); hotkeys, installs and realms are saved immediately.
/// </summary>
public sealed partial class SettingsPageViewModel : ObservableObject, IDisposable
{
    private static readonly HashSet<string> DebouncedProperties =
    [
        nameof(SelectedTheme), nameof(MinimizeToTray), nameof(CloseToTray), nameof(CloseGamesOnExit),
        nameof(DetectExternalClients),
        nameof(CheckForUpdates), nameof(SelectedChannel),
        nameof(AutoLogin), nameof(ReadyDelaySeconds), nameof(WindowTimeoutSeconds), nameof(KeystrokeDelayMs),
        nameof(StaggerSeconds), nameof(RefocusAfterLogin),
        nameof(AudioEnabled), nameof(FocusedVolume), nameof(UnfocusedVolume),
        nameof(SwitcherOpacity), nameof(ShowSwitcherOnTeamLaunch), nameof(DoNotStealFocus), nameof(SelectedSwitcherView), nameof(LargePreviewScale),
        nameof(ShowNameBadges),
        nameof(EfficiencyMode), nameof(LowerPriority),
        nameof(HotkeysEnabled),
    ];

    private readonly ISettingsStore _settings;
    private readonly IInstallCatalog _installs;
    private readonly IAccountStore _accounts;
    private readonly IHotkeyCoordinator _hotkeys;
    private readonly IDialogService _dialogs;
    private readonly ClientActions _actions;
    private readonly StatusService _status;
    private readonly ILogger<SettingsPageViewModel> _logger;
    private readonly UiDebouncer _saveDebouncer = new(TimeSpan.FromMilliseconds(350));
    private readonly UiCoalescer _settingsRefresh;
    private readonly UiCoalescer _installsRefresh;
    private readonly UiCoalescer _hotkeyStatusRefresh;
    private HotkeyRowViewModel? _capturingRow;
    private bool _loading;
    private int _installSearches;

    public SettingsPageViewModel(
        ISettingsStore settings,
        IInstallCatalog installs,
        IAccountStore accounts,
        IHotkeyCoordinator hotkeys,
        IDialogService dialogs,
        ClientActions actions,
        StatusService status,
        UpdateService updates,
        GameFilesViewModel gameFiles,
        ILogger<SettingsPageViewModel> logger)
    {
        _settings = settings;
        _installs = installs;
        _accounts = accounts;
        _hotkeys = hotkeys;
        _dialogs = dialogs;
        _actions = actions;
        _status = status;
        _logger = logger;
        Updates = updates;
        GameFiles = gameFiles;

        HotkeyRows = Enum.GetValues<HotkeyAction>().Select(action => new HotkeyRowViewModel(this, action)).ToArray();
        PreferredInstalls = GameOption.All
            .Select(option => new PreferredInstallViewModel(option.Game, SetPreferredInstall))
            .ToArray();

        _settingsRefresh = new UiCoalescer(OnSettingsChangedElsewhere);
        _installsRefresh = new UiCoalescer(RefreshInstalls);
        _hotkeyStatusRefresh = new UiCoalescer(RefreshHotkeyStatus);

        Load(_settings.Current);
        RefreshHotkeyStatus();

        _settings.Changed += OnSettingsChanged;
        _installs.Changed += OnInstallsChanged;
        _hotkeys.RegistrationsChanged += OnHotkeyRegistrationsChanged;

        // This runs while the main window is being created; the first discovery must not hold it up.
        _ = SearchInstallsAsync(rescan: false);
    }

    public UpdateService Updates { get; }

    /// <summary>Settings → Games → Game files (full download / update per install).</summary>
    public GameFilesViewModel GameFiles { get; }

    public string VersionText => $"MultiWiz {Updates.CurrentVersion}";

    // General

    public IReadOnlyList<ThemeOption> ThemeOptions => ThemeOption.All;

    [ObservableProperty]
    public partial ThemeOption? SelectedTheme { get; set; }

    [ObservableProperty]
    public partial bool MinimizeToTray { get; set; }

    [ObservableProperty]
    public partial bool CloseToTray { get; set; }

    [ObservableProperty]
    public partial bool CloseGamesOnExit { get; set; }

    [ObservableProperty]
    public partial bool DetectExternalClients { get; set; }

    [ObservableProperty]
    public partial bool CheckForUpdates { get; set; }

    public IReadOnlyList<ChannelOption> ChannelOptions => ChannelOption.All;

    [ObservableProperty]
    public partial ChannelOption? SelectedChannel { get; set; }

    // Login

    [ObservableProperty]
    public partial bool AutoLogin { get; set; }

    [ObservableProperty]
    public partial decimal? ReadyDelaySeconds { get; set; }

    [ObservableProperty]
    public partial decimal? WindowTimeoutSeconds { get; set; }

    [ObservableProperty]
    public partial decimal? KeystrokeDelayMs { get; set; }

    [ObservableProperty]
    public partial decimal? StaggerSeconds { get; set; }

    [ObservableProperty]
    public partial bool RefocusAfterLogin { get; set; }

    // Audio

    [ObservableProperty]
    public partial bool AudioEnabled { get; set; }

    [ObservableProperty]
    public partial double FocusedVolume { get; set; }

    [ObservableProperty]
    public partial double UnfocusedVolume { get; set; }

    // Switcher and overlays

    [ObservableProperty]
    public partial double SwitcherOpacity { get; set; }

    [ObservableProperty]
    public partial bool ShowSwitcherOnTeamLaunch { get; set; }

    [ObservableProperty]
    public partial bool DoNotStealFocus { get; set; }

    public IReadOnlyList<SwitcherViewOption> SwitcherViewOptions => SwitcherViewOption.All;

    [ObservableProperty]
    public partial SwitcherViewOption? SelectedSwitcherView { get; set; }

    /// <summary>Large switcher preview size in percent (50-200).</summary>
    [ObservableProperty]
    public partial double LargePreviewScale { get; set; }

    [ObservableProperty]
    public partial bool ShowNameBadges { get; set; }

    // Performance

    [ObservableProperty]
    public partial bool EfficiencyMode { get; set; }

    [ObservableProperty]
    public partial bool LowerPriority { get; set; }

    // Hotkeys

    [ObservableProperty]
    public partial bool HotkeysEnabled { get; set; }

    public IReadOnlyList<HotkeyRowViewModel> HotkeyRows { get; }

    [ObservableProperty]
    public partial bool HasHotkeyFailures { get; private set; }

    // Games

    public ObservableCollection<InstallItemViewModel> InstallItems { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoInstallsWarning))]
    public partial bool HasInstalls { get; private set; }

    /// <summary>True while the registry and Steam libraries are being searched for game installs.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoInstallsWarning))]
    public partial bool IsSearchingInstalls { get; private set; } = true;

    public bool ShowNoInstallsWarning => !HasInstalls && !IsSearchingInstalls;

    public IReadOnlyList<PreferredInstallViewModel> PreferredInstalls { get; }

    public ObservableCollection<RealmItemViewModel> CustomRealms { get; } = [];

    [ObservableProperty]
    public partial bool HasCustomRealms { get; private set; }

    public IReadOnlyList<GameOption> Games => GameOption.All;

    [ObservableProperty]
    public partial string NewRealmName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial GameOption? NewRealmGame { get; set; } = GameOption.All[0];

    [ObservableProperty]
    public partial string NewRealmHost { get; set; } = string.Empty;

    [ObservableProperty]
    public partial decimal? NewRealmPort { get; set; } = 12000;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRealmError))]
    public partial string? RealmError { get; private set; }

    public bool HasRealmError => !string.IsNullOrEmpty(RealmError);

    /// <summary>Saves edits still waiting in the debounce window (on shutdown).</summary>
    public void FlushPendingChanges() => _saveDebouncer.Flush();

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        _installs.Changed -= OnInstallsChanged;
        _hotkeys.RegistrationsChanged -= OnHotkeyRegistrationsChanged;
        _saveDebouncer.Dispose();
        GameFiles.CancelAll();
    }

    /// <summary>Starts capturing a new combination for <paramref name="row"/>. Global hotkeys pause meanwhile.</summary>
    internal void BeginCapture(HotkeyRowViewModel row)
    {
        if (_capturingRow is not null && _capturingRow != row)
        {
            _capturingRow.SetCapturing(false);
        }

        _capturingRow = row;
        row.SetCapturing(true);

        // Registered hotkeys are swallowed by Windows before the app sees them, so pause them while capturing.
        _hotkeys.Stop();
    }

    /// <summary>Finishes a capture. <paramref name="newBinding"/>: null cancels, empty unbinds, otherwise the new text.</summary>
    internal void EndCapture(HotkeyRowViewModel row, string? newBinding)
    {
        row.SetCapturing(false);
        if (_capturingRow == row)
        {
            _capturingRow = null;
        }

        try
        {
            if (newBinding is not null)
            {
                SaveHotkey(row.Action, newBinding);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not save the hotkey for {Action}", row.Action);
            _status.Show("The hotkey could not be saved. Check that MultiWiz can write to its data folder.", isError: true);
        }
        finally
        {
            // BeginCapture paused every global hotkey; a failed save must not leave them off for the rest of the session.
            if (_capturingRow is null)
            {
                _hotkeys.Start();
            }
        }
    }

    /// <summary>Cancels any capture in progress (the capture box lost focus).</summary>
    public void CancelCapture()
    {
        if (_capturingRow is { } row)
        {
            EndCapture(row, newBinding: null);
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_loading && e.PropertyName is { } name && DebouncedProperties.Contains(name))
        {
            _saveDebouncer.Schedule(SavePreferences);
        }
    }

    [RelayCommand]
    private Task CheckForUpdatesNowAsync() => Updates.CheckNowAsync();

    [RelayCommand]
    private async Task RestartToUpdateAsync()
    {
        // Ask before RestartToApply: once it runs, MultiWiz has 60 seconds to exit.
        if (await _actions.ConfirmExitAsync("Restart to update"))
        {
            Updates.RestartToApply();
        }
    }

    [RelayCommand]
    private void ResetHotkeys()
    {
        CancelCapture();
        _settings.Update(settings => settings with
        {
            Hotkeys = settings.Hotkeys with { Bindings = new Dictionary<HotkeyAction, string>() },
        });
        _status.Show("Hotkeys were reset to their defaults.");
    }

    [RelayCommand]
    private async Task AddGameFolderAsync()
    {
        var picked = await _dialogs.PickFolderAsync("Choose your Wizard101 or Pirate101 folder");
        if (picked is null)
        {
            return;
        }

        if (!TryDetectInstall(picked, out var root, out var game))
        {
            await _dialogs.ShowErrorAsync(
                "That isn't a game folder",
                "MultiWiz couldn't find Bin\\WizardGraphicalClient.exe or Bin\\PirateGraphicalClient.exe in that folder. " +
                "Choose the folder that contains the game's Bin folder.");
            return;
        }

        if (_installs.GetAll().Any(install => SamePath(install.RootPath, root)))
        {
            _status.Show("That game folder is already listed.");
            return;
        }

        var install = new GameInstall
        {
            Id = $"custom-{Guid.NewGuid():N}",
            Game = game,
            Source = InstallSource.Custom,
            RootPath = root,
            DisplayName = $"{GameOption.For(game).Label} ({Path.GetFileName(root)})",
        };

        _settings.Update(settings => settings with { CustomInstalls = [.. settings.CustomInstalls, install] });
        _logger.LogInformation("Added custom {Game} install at {Path}", game, root);
        RefreshInstalls();
        _status.Show($"Added {install.DisplayName}.");
    }

    [RelayCommand]
    private async Task RescanInstallsAsync()
    {
        await SearchInstallsAsync(rescan: true);
        if (!IsSearchingInstalls)
        {
            _status.Show(InstallItems.Count == 1 ? "Found 1 game install." : $"Found {InstallItems.Count} game installs.");
        }
    }

    /// <summary>
    /// Runs install discovery off the UI thread: it reads the registry and every Steam library, and a library on a
    /// sleeping or unreachable drive can take many seconds. The first <see cref="IInstallCatalog.GetAll"/> discovers and
    /// caches; <paramref name="rescan"/> discovers again. The install lists are refreshed once no search is running.
    /// </summary>
    private async Task SearchInstallsAsync(bool rescan)
    {
        _installSearches++;
        IsSearchingInstalls = true;
        try
        {
            await Task.Run(() =>
            {
                if (rescan)
                {
                    _installs.Refresh();
                }
                else
                {
                    _ = _installs.GetAll();
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Searching for game installs failed");
        }
        finally
        {
            _installSearches--;
            IsSearchingInstalls = _installSearches > 0;
        }

        RefreshInstalls();
    }

    [RelayCommand]
    private void AddRealm()
    {
        RealmError = null;
        var name = NewRealmName.Trim();
        var host = NewRealmHost.Trim();
        var port = ToInt(NewRealmPort, fallback: 0);

        if (name.Length == 0)
        {
            RealmError = "Give the realm a name.";
            return;
        }

        if (host.Length == 0 || host.Any(char.IsWhiteSpace) || Uri.CheckHostName(host) == UriHostNameType.Unknown)
        {
            RealmError = "Enter a valid login server host name, like login.us.wizard101.com.";
            return;
        }

        if (port is < 1 or > 65535)
        {
            RealmError = "The port must be between 1 and 65535.";
            return;
        }

        var realm = new Realm
        {
            Id = $"custom-{Guid.NewGuid():N}",
            Game = NewRealmGame?.Game ?? GameKind.Wizard101,
            DisplayName = name,
            LoginHost = host,
            LoginPort = port,
            IsBuiltIn = false,
        };

        _settings.Update(settings => settings with { CustomRealms = [.. settings.CustomRealms, realm] });
        NewRealmName = string.Empty;
        NewRealmHost = string.Empty;
        NewRealmPort = 12000;
        RefreshRealms(_settings.Current);
        _status.Show($"Added the realm {name}.");
    }

    private async Task RemoveRealmAsync(RealmItemViewModel item)
    {
        var users = _accounts.GetAll().Count(account => account.RealmId == item.Realm.Id);
        if (users > 0)
        {
            var confirmed = await _dialogs.ConfirmAsync(
                $"Remove {item.Name}?",
                users == 1
                    ? "1 account uses this realm. It will log in to the game's default realm instead."
                    : $"{users} accounts use this realm. They will log in to the game's default realm instead.",
                "Remove realm",
                isDestructive: true);
            if (!confirmed)
            {
                return;
            }
        }

        _settings.Update(settings => settings with
        {
            CustomRealms = settings.CustomRealms.Where(realm => realm.Id != item.Realm.Id).ToArray(),
        });
        RefreshRealms(_settings.Current);
    }

    private void RemoveCustomInstall(InstallItemViewModel item)
    {
        var id = item.Install.Id;
        _settings.Update(settings => settings with
        {
            CustomInstalls = settings.CustomInstalls.Where(install => install.Id != id).ToArray(),
            PreferredInstallIds = settings.PreferredInstallIds
                .Where(pair => pair.Value != id)
                .ToDictionary(pair => pair.Key, pair => pair.Value),
        });
        RefreshInstalls();
        _status.Show($"Removed {item.Title}.");
    }

    private void SetPreferredInstall(GameKind game, string? installId) =>
        _settings.Update(settings =>
        {
            var preferred = new Dictionary<GameKind, string>(settings.PreferredInstallIds);
            if (installId is null)
            {
                preferred.Remove(game);
            }
            else
            {
                preferred[game] = installId;
            }

            return settings with { PreferredInstallIds = preferred };
        });

    private void SaveHotkey(HotkeyAction action, string bindingText)
    {
        var current = _settings.Current.Hotkeys.Bindings;
        var cleared = new List<HotkeyAction>();
        if (HotkeyBinding.TryParse(bindingText, out var binding))
        {
            foreach (var other in Enum.GetValues<HotkeyAction>())
            {
                if (other != action
                    && HotkeyBinding.TryParse(DefaultHotkeys.Resolve(current, other), out var otherBinding)
                    && otherBinding == binding)
                {
                    cleared.Add(other);
                }
            }
        }

        _settings.Update(settings =>
        {
            var bindings = new Dictionary<HotkeyAction, string>(settings.Hotkeys.Bindings) { [action] = bindingText };
            foreach (var other in cleared)
            {
                bindings[other] = string.Empty;
            }

            return settings with { Hotkeys = settings.Hotkeys with { Bindings = bindings } };
        });

        _logger.LogInformation("Hotkey for {Action} set to '{Binding}'", action, bindingText);
        if (cleared.Count > 0)
        {
            var moved = HotkeyRows.First(row => row.Action == cleared[0]).Label;
            _status.Show($"{bindingText} was taken from \"{moved}\", which is now unbound.");
        }
    }

    private void SavePreferences()
    {
        _settings.Update(settings => settings with
        {
            General = settings.General with
            {
                Theme = SelectedTheme?.Value ?? settings.General.Theme,
                MinimizeToTray = MinimizeToTray,
                CloseToTray = CloseToTray,
                CloseGamesOnExit = CloseGamesOnExit,
                DetectExternalClients = DetectExternalClients,
                CheckForUpdates = CheckForUpdates,
                UpdateChannel = SelectedChannel?.Value ?? settings.General.UpdateChannel,
            },
            Login = settings.Login with
            {
                AutoLogin = AutoLogin,
                ReadyDelaySeconds = ToInt(ReadyDelaySeconds, settings.Login.ReadyDelaySeconds),
                WindowTimeoutSeconds = ToInt(WindowTimeoutSeconds, settings.Login.WindowTimeoutSeconds),
                KeystrokeDelayMs = ToInt(KeystrokeDelayMs, settings.Login.KeystrokeDelayMs),
                StaggerSeconds = ToInt(StaggerSeconds, settings.Login.StaggerSeconds),
                RefocusAfterLogin = RefocusAfterLogin,
            },
            Audio = settings.Audio with
            {
                Enabled = AudioEnabled,
                FocusedVolumePercent = (int)Math.Round(FocusedVolume),
                UnfocusedVolumePercent = (int)Math.Round(UnfocusedVolume),
            },
            Switcher = settings.Switcher with
            {
                Opacity = Math.Round(SwitcherOpacity, 2),
                ShowOnTeamLaunch = ShowSwitcherOnTeamLaunch,
                DoNotStealFocus = DoNotStealFocus,
                ViewMode = SelectedSwitcherView?.Value ?? settings.Switcher.ViewMode,
                LargePreviewScalePercent = (int)Math.Round(LargePreviewScale),
            },
            Overlays = settings.Overlays with { ShowNameBadges = ShowNameBadges },
            Performance = settings.Performance with
            {
                EfficiencyModeForBackground = EfficiencyMode,
                LowerBackgroundPriority = LowerPriority,
            },
            Hotkeys = settings.Hotkeys with { Enabled = HotkeysEnabled },
        });
    }

    private void Load(AppSettings settings)
    {
        _loading = true;
        try
        {
            SelectedTheme = ThemeOption.All.FirstOrDefault(option => option.Value == settings.General.Theme) ?? ThemeOption.All[0];
            MinimizeToTray = settings.General.MinimizeToTray;
            CloseToTray = settings.General.CloseToTray;
            CloseGamesOnExit = settings.General.CloseGamesOnExit;
            DetectExternalClients = settings.General.DetectExternalClients;
            CheckForUpdates = settings.General.CheckForUpdates;
            SelectedChannel = ChannelOption.All.FirstOrDefault(option => option.Value == settings.General.UpdateChannel) ?? ChannelOption.All[0];

            AutoLogin = settings.Login.AutoLogin;
            ReadyDelaySeconds = settings.Login.ReadyDelaySeconds;
            WindowTimeoutSeconds = settings.Login.WindowTimeoutSeconds;
            KeystrokeDelayMs = settings.Login.KeystrokeDelayMs;
            StaggerSeconds = settings.Login.StaggerSeconds;
            RefocusAfterLogin = settings.Login.RefocusAfterLogin;

            AudioEnabled = settings.Audio.Enabled;
            FocusedVolume = settings.Audio.FocusedVolumePercent;
            UnfocusedVolume = settings.Audio.UnfocusedVolumePercent;

            SwitcherOpacity = settings.Switcher.Opacity;
            ShowSwitcherOnTeamLaunch = settings.Switcher.ShowOnTeamLaunch;
            DoNotStealFocus = settings.Switcher.DoNotStealFocus;
            SelectedSwitcherView = SwitcherViewOption.All.FirstOrDefault(option => option.Value == settings.Switcher.ViewMode)
                ?? SwitcherViewOption.All[1];
            LargePreviewScale = settings.Switcher.LargePreviewScalePercent;
            ShowNameBadges = settings.Overlays.ShowNameBadges;

            EfficiencyMode = settings.Performance.EfficiencyModeForBackground;
            LowerPriority = settings.Performance.LowerBackgroundPriority;

            HotkeysEnabled = settings.Hotkeys.Enabled;
            foreach (var row in HotkeyRows)
            {
                row.SetBindingText(DefaultHotkeys.Resolve(settings.Hotkeys.Bindings, row.Action));
            }

            RefreshRealms(settings);
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnSettingsChanged(object? sender, AppSettings settings) => _settingsRefresh.Request();

    private void OnInstallsChanged(object? sender, EventArgs e) => _installsRefresh.Request();

    private void OnHotkeyRegistrationsChanged(object? sender, EventArgs e) => _hotkeyStatusRefresh.Request();

    private void OnSettingsChangedElsewhere()
    {
        // Don't overwrite edits that are about to be saved; the save will raise another change.
        if (!_saveDebouncer.HasPending)
        {
            Load(_settings.Current);
        }

        RefreshPreferredInstalls();
    }

    private void RefreshInstalls()
    {
        // While a search runs, GetAll would wait for it on the UI thread; the search refreshes when it ends.
        if (IsSearchingInstalls)
        {
            return;
        }

        var installs = _installs.GetAll();
        InstallItems.Clear();
        foreach (var install in installs.OrderBy(install => install.Game).ThenBy(install => install.Source))
        {
            InstallItems.Add(new InstallItemViewModel(install, RemoveCustomInstall));
        }

        HasInstalls = InstallItems.Count > 0;
        GameFiles.Refresh(installs);
        RefreshPreferredInstalls();
    }

    private void RefreshPreferredInstalls()
    {
        if (IsSearchingInstalls)
        {
            return;
        }

        var installs = _installs.GetAll();
        var preferred = _settings.Current.PreferredInstallIds;
        foreach (var picker in PreferredInstalls)
        {
            picker.Refresh(installs, preferred.GetValueOrDefault(picker.Game));
        }
    }

    private void RefreshRealms(AppSettings settings)
    {
        CustomRealms.Clear();
        foreach (var realm in settings.CustomRealms)
        {
            CustomRealms.Add(new RealmItemViewModel(realm, RemoveRealmAsync));
        }

        HasCustomRealms = CustomRealms.Count > 0;
    }

    private void RefreshHotkeyStatus()
    {
        var failed = _hotkeys.FailedActions;
        foreach (var row in HotkeyRows)
        {
            row.HasFailed = failed.Contains(row.Action);
        }

        HasHotkeyFailures = HotkeyRows.Any(row => row.HasFailed);
    }

    private static int ToInt(decimal? value, int fallback) =>
        value is { } number ? (int)Math.Round(Math.Clamp(number, int.MinValue, int.MaxValue)) : fallback;

    private static bool SamePath(string left, string right) =>
        string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }

    /// <summary>Accepts the game root or its Bin folder and works out which game it holds.</summary>
    private static bool TryDetectInstall(string pickedPath, out string root, out GameKind game)
    {
        var folder = NormalizePath(pickedPath);
        List<string> candidates = [folder];
        if (string.Equals(Path.GetFileName(folder), "Bin", StringComparison.OrdinalIgnoreCase)
            && Path.GetDirectoryName(folder) is { Length: > 0 } parent)
        {
            candidates.Insert(0, parent);
        }

        foreach (var candidate in candidates)
        {
            foreach (var kind in Enum.GetValues<GameKind>())
            {
                if (File.Exists(Path.Combine(candidate, "Bin", GameExecutables.ClientExecutableName(kind))))
                {
                    root = candidate;
                    game = kind;
                    return true;
                }
            }
        }

        root = folder;
        game = GameKind.Wizard101;
        return false;
    }
}
