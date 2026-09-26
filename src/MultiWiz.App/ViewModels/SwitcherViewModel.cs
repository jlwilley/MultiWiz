using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MultiWiz.App.Services;
using MultiWiz.Core.Accounts;
using MultiWiz.Core.Hotkeys;
using MultiWiz.Core.Settings;
using MultiWiz.Core.Switching;
using MultiWiz.Core.Teams;

namespace MultiWiz.App.ViewModels;

/// <summary>A client in the switcher.</summary>
public sealed partial class SwitcherEntryViewModel : ObservableObject
{
    private readonly Action<Guid> _focus;

    public SwitcherEntryViewModel(Guid accountId, Action<Guid> focus)
    {
        AccountId = accountId;
        _focus = focus;
    }

    public Guid AccountId { get; }

    [ObservableProperty]
    public partial int Slot { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? AccentColor { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHotkeyHint))]
    public partial string? HotkeyHint { get; set; }

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    /// <summary>The client's game window (0 while it has none); the switcher window draws its live preview.</summary>
    [ObservableProperty]
    public partial nint WindowHandle { get; set; }

    /// <summary>Row layout with a small preview (Previews mode, or a Large-mode client that didn't fit).</summary>
    [ObservableProperty]
    public partial bool ShowPreview { get; set; }

    /// <summary>Shown as a large preview tile.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRow))]
    public partial bool IsLarge { get; set; }

    public bool IsRow => !IsLarge;

    [ObservableProperty]
    public partial double LargeWidth { get; set; }

    [ObservableProperty]
    public partial double LargeHeight { get; set; }

    public bool HasHotkeyHint => !string.IsNullOrEmpty(HotkeyHint);

    [RelayCommand]
    private void Focus() => _focus(AccountId);
}

/// <summary>The compact always-on-top switcher: running clients in slot order with their hotkeys.</summary>
public sealed partial class SwitcherViewModel : ObservableObject, IDisposable
{
    private readonly IClientSwitcher _switcher;
    private readonly IAccountStore _accounts;
    private readonly ITeamStore _teams;
    private readonly ISettingsStore _settings;
    private readonly IHotkeyCoordinator _hotkeys;
    private readonly UiCoalescer _refresh;
    private readonly UiDebouncer _positionSave = new(TimeSpan.FromMilliseconds(500));

    public SwitcherViewModel(
        IClientSwitcher switcher,
        IAccountStore accounts,
        ITeamStore teams,
        ISettingsStore settings,
        IHotkeyCoordinator hotkeys)
    {
        _switcher = switcher;
        _accounts = accounts;
        _teams = teams;
        _settings = settings;
        _hotkeys = hotkeys;
        _refresh = new UiCoalescer(Refresh);

        Refresh();

        _switcher.Changed += OnSourceChanged;
        _accounts.Changed += OnSourceChanged;
        _teams.Changed += OnSourceChanged;
        _settings.Changed += OnSettingsChanged;
        _hotkeys.RegistrationsChanged += OnSourceChanged;
    }

    public ObservableCollection<SwitcherEntryViewModel> Entries { get; } = [];

    [ObservableProperty]
    public partial bool IsEmpty { get; private set; } = true;

    [ObservableProperty]
    public partial string Title { get; private set; } = "Switcher";

    [ObservableProperty]
    public partial double Opacity { get; private set; } = 0.95;

    [ObservableProperty]
    public partial bool DoNotStealFocus { get; private set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowWidth), nameof(IsListMode), nameof(IsPreviewsMode), nameof(IsLargeMode))]
    public partial SwitcherViewMode ViewMode { get; private set; } = SwitcherViewMode.Previews;

    public bool IsListMode => ViewMode == SwitcherViewMode.List;

    public bool IsPreviewsMode => ViewMode == SwitcherViewMode.Previews;

    public bool IsLargeMode => ViewMode == SwitcherViewMode.Large;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowWidth))]
    public partial double LargeTileWidth { get; private set; } = 320;

    /// <summary>
    /// Window width in DIPs: fixed for the list and small previews; in Large mode the tile width plus the window's
    /// margin, padding and button padding.
    /// </summary>
    public double WindowWidth => ViewMode switch
    {
        SwitcherViewMode.List => 280,
        SwitcherViewMode.Previews => 340,
        _ => Math.Max(340, LargeTileWidth + 52),
    };

    // Working area of the switcher's monitor in DIPs; the large tiles are sized from it.
    private double _screenWidth = 1920;
    private double _screenHeight = 1040;

    /// <summary>Called by the window with the working area (DIPs) of the monitor it is on.</summary>
    public void SetScreen(double width, double height)
    {
        if (Math.Abs(width - _screenWidth) < 1 && Math.Abs(height - _screenHeight) < 1)
        {
            return;
        }

        _screenWidth = width;
        _screenHeight = height;
        _refresh.Request();
    }

    [RelayCommand]
    private void ShowList() => SetViewMode(SwitcherViewMode.List);

    [RelayCommand]
    private void ShowPreviews() => SetViewMode(SwitcherViewMode.Previews);

    [RelayCommand]
    private void ShowLarge() => SetViewMode(SwitcherViewMode.Large);

    private void SetViewMode(SwitcherViewMode mode)
    {
        ViewMode = mode;
        _refresh.Request();
        _settings.Update(settings => settings.Switcher.ViewMode == mode
            ? settings
            : settings with { Switcher = settings.Switcher with { ViewMode = mode } });
    }

    /// <summary>Where the user last left the switcher (physical pixels), or null for the default spot.</summary>
    public (int X, int Y)? SavedPosition =>
        _settings.Current.Switcher is { Left: { } left, Top: { } top } ? (left, top) : null;

    /// <summary>Remembers the switcher's position after the user drags it (saved after a short pause).</summary>
    public void RememberPosition(int x, int y) =>
        _positionSave.Schedule(() => _settings.Update(settings =>
            settings.Switcher.Left == x && settings.Switcher.Top == y
                ? settings
                : settings with { Switcher = settings.Switcher with { Left = x, Top = y } }));

    public void FlushPendingChanges() => _positionSave.Flush();

    public void Dispose()
    {
        _switcher.Changed -= OnSourceChanged;
        _accounts.Changed -= OnSourceChanged;
        _teams.Changed -= OnSourceChanged;
        _settings.Changed -= OnSettingsChanged;
        _hotkeys.RegistrationsChanged -= OnSourceChanged;
        _positionSave.Dispose();
    }

    private void OnSourceChanged(object? sender, EventArgs e) => _refresh.Request();

    private void OnSettingsChanged(object? sender, AppSettings settings) => _refresh.Request();

    private void FocusClient(Guid accountId) => _switcher.Focus(accountId);

    private void Refresh()
    {
        var settings = _settings.Current;
        Opacity = settings.Switcher.Opacity;
        DoNotStealFocus = settings.Switcher.DoNotStealFocus;
        ViewMode = settings.Switcher.ViewMode;

        // Large tiles: about a fifth of the monitor wide at 100%, 16:9, plus a details bar; only as many as fit
        // the monitor's height are large, the rest stay small rows below them.
        var scale = Math.Clamp(settings.Switcher.LargePreviewScalePercent, 50, 200) / 100.0;
        var tileWidth = Math.Round(Math.Clamp(_screenWidth * 0.2 * scale, 200, _screenWidth * 0.6));
        var tileHeight = Math.Round(tileWidth * 9 / 16);
        LargeTileWidth = tileWidth;
        const double chrome = 110; // title row, window margin and padding
        const double perTileExtra = 46; // details bar and spacing
        var maxLarge = Math.Max(1, (int)((_screenHeight - chrome) / (tileHeight + perTileExtra)));
        Title = _switcher.ActiveTeamId is { } teamId && _teams.Find(teamId) is { } team ? team.Name : "Switcher";

        var sessions = _switcher.OrderedSessions;
        var currentId = _switcher.Current?.AccountId;
        var failedHotkeys = _hotkeys.FailedActions;
        var existing = Entries.ToDictionary(entry => entry.AccountId);
        var ordered = new List<SwitcherEntryViewModel>(sessions.Count);
        for (var i = 0; i < sessions.Count; i++)
        {
            var session = sessions[i];
            if (!existing.TryGetValue(session.AccountId, out var entry))
            {
                entry = new SwitcherEntryViewModel(session.AccountId, FocusClient);
            }

            var account = _accounts.Find(session.AccountId);
            entry.Slot = i + 1;
            entry.Name = account?.DisplayName ?? session.Label ?? "Unknown account";
            entry.AccentColor = account?.AccentColor;
            entry.HotkeyHint = HotkeyHintFor(settings.Hotkeys, i, failedHotkeys);
            entry.IsCurrent = session.AccountId == currentId;
            entry.WindowHandle = session.WindowHandle;
            entry.IsLarge = ViewMode == SwitcherViewMode.Large && i < maxLarge;
            entry.ShowPreview = !entry.IsLarge && ViewMode != SwitcherViewMode.List;
            entry.LargeWidth = tileWidth;
            entry.LargeHeight = tileHeight;
            ordered.Add(entry);
        }

        CollectionSync.Apply(Entries, ordered);
        IsEmpty = Entries.Count == 0;
    }

    /// <summary>The slot's focus hotkey, or null when there is none or it could not be registered (in use elsewhere).</summary>
    private static string? HotkeyHintFor(HotkeySettings hotkeys, int slotIndex, IReadOnlyList<HotkeyAction> failed)
    {
        if (!hotkeys.Enabled || slotIndex > 7)
        {
            return null;
        }

        var action = HotkeyAction.FocusSlot1 + slotIndex;
        if (failed.Contains(action))
        {
            return null;
        }

        var text = DefaultHotkeys.Resolve(hotkeys.Bindings, action);
        return HotkeyBinding.TryParse(text, out var binding) && binding.IsValid ? binding.ToString() : null;
    }
}
