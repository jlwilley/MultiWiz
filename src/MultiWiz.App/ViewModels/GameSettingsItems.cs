using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MultiWiz.Core.Games;

namespace MultiWiz.App.ViewModels;

/// <summary>A discovered or custom game install in Settings → Games.</summary>
public sealed partial class InstallItemViewModel
{
    private readonly Action<InstallItemViewModel> _remove;

    public InstallItemViewModel(GameInstall install, Action<InstallItemViewModel> remove)
    {
        Install = install;
        _remove = remove;
    }

    public GameInstall Install { get; }

    public string Title => string.IsNullOrWhiteSpace(Install.DisplayName) ? Install.Game.ToString() : Install.DisplayName;

    public string Source => InstallLabels.SourceName(Install.Source);

    public string RootPath => Install.RootPath;

    public bool IsCustom => Install.Source == InstallSource.Custom;

    [RelayCommand]
    private void Remove() => _remove(this);
}

/// <summary>The "preferred install" picker for one game (used by accounts set to Automatic).</summary>
public sealed partial class PreferredInstallViewModel : ObservableObject
{
    private readonly Action<GameKind, string?> _changed;
    private bool _updating;

    public PreferredInstallViewModel(GameKind game, Action<GameKind, string?> changed)
    {
        Game = game;
        Label = GameOption.For(game).Label;
        _changed = changed;
    }

    public GameKind Game { get; }

    public string Label { get; }

    [ObservableProperty]
    public partial IReadOnlyList<InstallOption> Options { get; private set; } = [InstallOption.Automatic];

    [ObservableProperty]
    public partial InstallOption? Selected { get; set; }

    [ObservableProperty]
    public partial bool HasChoices { get; private set; }

    internal void Refresh(IReadOnlyList<GameInstall> installs, string? preferredInstallId)
    {
        _updating = true;
        try
        {
            InstallOption[] options = [InstallOption.Automatic, .. installs.Where(install => install.Game == Game).Select(InstallOption.From)];
            Options = options;
            Selected = options.FirstOrDefault(option => option.InstallId == preferredInstallId) ?? InstallOption.Automatic;
            HasChoices = options.Length > 1;
        }
        finally
        {
            _updating = false;
        }
    }

    partial void OnSelectedChanged(InstallOption? value)
    {
        if (!_updating && value is not null)
        {
            _changed(Game, value.InstallId);
        }
    }
}

/// <summary>A user-defined login server in Settings → Games.</summary>
public sealed partial class RealmItemViewModel
{
    private readonly Func<RealmItemViewModel, Task> _remove;

    public RealmItemViewModel(Realm realm, Func<RealmItemViewModel, Task> remove)
    {
        Realm = realm;
        _remove = remove;
    }

    public Realm Realm { get; }

    public string Name => Realm.DisplayName;

    public string Detail => $"{GameOption.For(Realm.Game).Label} · {Realm.LoginHost}:{Realm.LoginPort}";

    [RelayCommand]
    private Task RemoveAsync() => _remove(this);
}
