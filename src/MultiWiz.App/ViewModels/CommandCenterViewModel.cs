using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MultiWiz.App.Services;
using MultiWiz.Core.Accounts;
using MultiWiz.Core.Switching;

namespace MultiWiz.App.ViewModels;

/// <summary>One live preview tile in Command Center.</summary>
public sealed partial class ThumbnailTileViewModel : ObservableObject
{
    private readonly Action<Guid> _focus;

    public ThumbnailTileViewModel(Guid accountId, Action<Guid> focus)
    {
        AccountId = accountId;
        _focus = focus;
    }

    public Guid AccountId { get; }

    /// <summary>The game window shown in this tile (the DWM thumbnail source).</summary>
    [ObservableProperty]
    public partial nint WindowHandle { get; set; }

    [ObservableProperty]
    public partial int Slot { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? AccentColor { get; set; }

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    [RelayCommand]
    private void Focus() => _focus(AccountId);
}

/// <summary>Command Center: a grid of live previews of every running client; click one to focus it.</summary>
public sealed partial class CommandCenterViewModel : ObservableObject, IDisposable
{
    private readonly IClientSwitcher _switcher;
    private readonly IAccountStore _accounts;
    private readonly UiCoalescer _refresh;

    public CommandCenterViewModel(IClientSwitcher switcher, IAccountStore accounts)
    {
        _switcher = switcher;
        _accounts = accounts;
        _refresh = new UiCoalescer(Refresh);

        Refresh();

        _switcher.Changed += OnSourceChanged;
        _accounts.Changed += OnSourceChanged;
    }

    public ObservableCollection<ThumbnailTileViewModel> Tiles { get; } = [];

    [ObservableProperty]
    public partial int Columns { get; private set; } = 1;

    [ObservableProperty]
    public partial bool IsEmpty { get; private set; } = true;

    [ObservableProperty]
    public partial string Summary { get; private set; } = string.Empty;

    public void Dispose()
    {
        _switcher.Changed -= OnSourceChanged;
        _accounts.Changed -= OnSourceChanged;
    }

    private void OnSourceChanged(object? sender, EventArgs e) => _refresh.Request();

    private void FocusClient(Guid accountId) => _switcher.Focus(accountId);

    private void Refresh()
    {
        var sessions = _switcher.OrderedSessions;
        var currentId = _switcher.Current?.AccountId;
        var existing = Tiles.ToDictionary(tile => tile.AccountId);
        var ordered = new List<ThumbnailTileViewModel>(sessions.Count);
        for (var i = 0; i < sessions.Count; i++)
        {
            var session = sessions[i];
            if (!existing.TryGetValue(session.AccountId, out var tile))
            {
                tile = new ThumbnailTileViewModel(session.AccountId, FocusClient);
            }

            var account = _accounts.Find(session.AccountId);
            tile.WindowHandle = session.WindowHandle;
            tile.Slot = i + 1;
            tile.Name = account?.DisplayName ?? "Unknown account";
            tile.AccentColor = account?.AccentColor;
            tile.IsCurrent = session.AccountId == currentId;
            ordered.Add(tile);
        }

        CollectionSync.Apply(Tiles, ordered);
        IsEmpty = Tiles.Count == 0;
        Columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(Tiles.Count)));
        Summary = Tiles.Count switch
        {
            0 => "No clients running",
            1 => "1 client",
            _ => $"{Tiles.Count} clients",
        };
    }
}
