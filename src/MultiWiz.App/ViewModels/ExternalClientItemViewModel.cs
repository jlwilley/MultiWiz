using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MultiWiz.Core.Sessions;

namespace MultiWiz.App.ViewModels;

/// <summary>A game client started outside MultiWiz, listed on the Accounts page so it can be linked to an account.</summary>
public sealed partial class ExternalClientItemViewModel : ObservableObject
{
    private readonly AccountsPageViewModel _owner;

    public ExternalClientItemViewModel(AccountsPageViewModel owner, Guid sessionId)
    {
        _owner = owner;
        SessionId = sessionId;
    }

    /// <summary>The session's synthetic id (<see cref="ClientSession.AccountId"/> of an external session).</summary>
    public Guid SessionId { get; }

    [ObservableProperty]
    public partial string Label { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Detail { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FocusCommand))]
    public partial bool HasWindow { get; private set; }

    /// <summary>Accounts of the same game that have no client running.</summary>
    public ObservableCollection<AccountOption> LinkOptions { get; } = [];

    [ObservableProperty]
    public partial bool HasLinkOptions { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LinkCommand))]
    public partial AccountOption? SelectedAccount { get; set; }

    internal void Update(ClientSession session, IReadOnlyList<AccountOption> options)
    {
        Label = session.Label ?? "Game client";
        Detail = $"Process {session.ProcessId}";
        HasWindow = session.HasWindow;

        // Only touch the list when it changed, so an open drop-down and the choice survive unrelated refreshes.
        if (!LinkOptions.SequenceEqual(options))
        {
            var previous = SelectedAccount?.AccountId;
            LinkOptions.Clear();
            foreach (var option in options)
            {
                LinkOptions.Add(option);
            }

            SelectedAccount = LinkOptions.FirstOrDefault(option => option.AccountId == previous);
        }

        HasLinkOptions = LinkOptions.Count > 0;
    }

    [RelayCommand(CanExecute = nameof(CanLink))]
    private void Link() => _owner.LinkExternal(this);

    private bool CanLink() => SelectedAccount is not null;

    [RelayCommand(CanExecute = nameof(HasWindow))]
    private void Focus() => _owner.FocusExternal(this);
}
