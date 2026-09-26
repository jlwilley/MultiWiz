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

    [ObservableProperty]
    public partial bool ShowPreview { get; set; }

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
    [NotifyPropertyChangedFor(nameof(WindowWidth))]
    public partial bool ShowPreviews { get; private set; } = true;

    /// <summary>Wider when previews are shown, so names still fit next to them.</summary>
    public double WindowWidth => ShowPreviews ? 340 : 280;

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
        ShowPreviews = settings.Switcher.ShowPreviews;
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
            entry.ShowPreview = settings.Switcher.ShowPreviews;
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
