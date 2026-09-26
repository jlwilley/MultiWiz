using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MultiWiz.App.Services;

/// <summary>Short-lived messages for the main window's status bar. <see cref="Show"/> is safe to call from any thread.</summary>
public sealed partial class StatusService : ObservableObject
{
    private static readonly TimeSpan DisplayTime = TimeSpan.FromSeconds(6);
    private DispatcherTimer? _clearTimer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    public partial string? Message { get; private set; }

    [ObservableProperty]
    public partial bool IsError { get; private set; }

    public bool HasMessage => !string.IsNullOrEmpty(Message);

    public void Show(string message, bool isError = false)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Show(message, isError));
            return;
        }

        Message = message;
        IsError = isError;

        _clearTimer ??= CreateClearTimer();
        _clearTimer.Stop();
        _clearTimer.Interval = isError ? DisplayTime * 2 : DisplayTime;
        _clearTimer.Start();
    }

    private DispatcherTimer CreateClearTimer()
    {
        var timer = new DispatcherTimer();
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Message = null;
            IsError = false;
        };
        return timer;
    }
}
