using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MultiWiz.App.Services;
using MultiWiz.Core.Games;
using MultiWiz.Core.Patching;

namespace MultiWiz.App.ViewModels;

/// <summary>
/// Settings → Games → Game files: one row per install. Downloads the complete game (or just the update) from
/// KingsIsle's patch server so zones don't have to be fetched while playing. One operation runs at a time.
/// </summary>
public sealed partial class GameFilesViewModel : ObservableObject
{
    private readonly GameFilesService _service;
    private readonly IDialogService _dialogs;
    private readonly ILogger<GameFilesViewModel> _logger;

    public GameFilesViewModel(GameFilesService service, IDialogService dialogs, ILogger<GameFilesViewModel> logger)
    {
        _service = service;
        _dialogs = dialogs;
        _logger = logger;
    }

    public ObservableCollection<GameFilesItemViewModel> Items { get; } = [];

    [ObservableProperty]
    public partial bool HasItems { get; private set; }

    /// <summary>True while any row is checking or downloading.</summary>
    public bool IsAnyBusy => Items.Any(item => item.IsBusy);

    internal GameFilesService Service => _service;

    internal IDialogService Dialogs => _dialogs;

    internal ILogger Logger => _logger;

    /// <summary>Syncs the rows with the current installs (UI thread). Rows that are busy are kept.</summary>
    public void Refresh(IReadOnlyList<GameInstall> installs)
    {
        var wanted = installs.Where(install => install.Game == GameKind.Wizard101 || install.Game == GameKind.Pirate101).ToList();
        foreach (var item in Items.ToList())
        {
            if (!item.IsBusy && !wanted.Any(install => install.Id == item.Install.Id))
            {
                Items.Remove(item);
            }
        }

        foreach (var install in wanted)
        {
            var existing = Items.FirstOrDefault(item => item.Install.Id == install.Id);
            if (existing is null)
            {
                var item = new GameFilesItemViewModel(this, install);
                Items.Add(item);
                item.StartUpdateCheck();
            }
            else if (!existing.IsBusy && existing.Install != install)
            {
                Items[Items.IndexOf(existing)] = new GameFilesItemViewModel(this, install);
            }
        }

        HasItems = Items.Count > 0;
    }

    /// <summary>Cancels whatever is running (on shutdown).</summary>
    public void CancelAll()
    {
        foreach (var item in Items)
        {
            item.CancelOperation();
        }
    }

    internal void OnBusyChanged()
    {
        OnPropertyChanged(nameof(IsAnyBusy));
        foreach (var item in Items)
        {
            item.RefreshCommands();
        }
    }
}

/// <summary>One install's "Game files" row.</summary>
public sealed partial class GameFilesItemViewModel : ObservableObject
{
    private readonly GameFilesViewModel _owner;
    private readonly GameDownloadSupport _support;
    private CancellationTokenSource? _cancellation;
    private DownloadPlan? _fullPlan;
    private long _speedBytes;
    private long _speedTimestamp;
    private double _bytesPerSecond;

    internal GameFilesItemViewModel(GameFilesViewModel owner, GameInstall install)
    {
        _owner = owner;
        Install = install;
        _support = owner.Service.Downloader.GetSupport(install);
        StatusText = _support.IsSupported
            ? "Check to see how much of the game still has to be downloaded."
            : _support.Reason ?? "Not supported.";
    }

    public GameInstall Install { get; }

    public string Title => (string.IsNullOrWhiteSpace(Install.DisplayName) ? Install.Game.ToString() : Install.DisplayName)
        + " · " + InstallLabels.SourceName(Install.Source);

    public string RootPath => Install.RootPath;

    public bool IsSupported => _support.IsSupported;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand), nameof(DownloadCommand), nameof(UpdateCommand), nameof(CancelCommand))]
    public partial bool IsBusy { get; private set; }

    public bool IsIdle => !IsBusy;

    [ObservableProperty]
    public partial string StatusText { get; private set; }

    [ObservableProperty]
    public partial bool IsError { get; private set; }

    [ObservableProperty]
    public partial double ProgressValue { get; private set; }

    [ObservableProperty]
    public partial string? ProgressText { get; private set; }

    /// <summary>The Base package (client and libraries) doesn't match the server.</summary>
    [ObservableProperty]
    public partial bool IsOutdated { get; private set; }

    [ObservableProperty]
    public partial string? OutdatedText { get; private set; }

    [ObservableProperty]
    public partial string DownloadButtonText { get; private set; } = "Download full game";

    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task CheckAsync() => RunAsync(async token =>
    {
        _fullPlan = null;
        var plan = await PlanAsync(DownloadScope.FullGame, token);
        _fullPlan = plan;
        ShowPlan(plan);
    });

    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task DownloadAsync() => RunAsync(async token =>
    {
        var plan = _fullPlan ?? await PlanAsync(DownloadScope.FullGame, token);
        _fullPlan = null;
        if (plan.IsUpToDate)
        {
            ShowPlan(plan);
            return;
        }

        if (!await ConfirmAsync(plan, "Download full game", $"Download {plan.FileCount:N0} files ({GameFilesService.FormatBytes(plan.TotalBytes)}) from KingsIsle into {Install.RootPath}?\n\nZones will then load without downloading while you play. Don't start the game until the download finishes."))
        {
            ShowPlan(plan);
            return;
        }

        await DownloadPlanAsync(plan, token);
    });

    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task UpdateAsync() => RunAsync(async token =>
    {
        var plan = await PlanAsync(DownloadScope.Update, token);
        if (plan.IsUpToDate)
        {
            SetOutdated(null);
            SetStatus("Your installed game files are up to date.");
            return;
        }

        if (!await ConfirmAsync(plan, "Update game files", $"Download {plan.FileCount:N0} updated files ({GameFilesService.FormatBytes(plan.TotalBytes)}) from KingsIsle?"))
        {
            SetStatus($"{plan.FileCount:N0} files ({GameFilesService.FormatBytes(plan.TotalBytes)}) are out of date.");
            return;
        }

        await DownloadPlanAsync(plan, token);
    });

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel()
    {
        _cancellation?.Cancel();
        ProgressText = "Cancelling…";
    }

    private bool CanStart() => IsSupported && !_owner.IsAnyBusy;

    internal void RefreshCommands()
    {
        CheckCommand.NotifyCanExecuteChanged();
        DownloadCommand.NotifyCanExecuteChanged();
        UpdateCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Quietly compares the core game files with the server once, in the background.</summary>
    internal void StartUpdateCheck()
    {
        if (!IsSupported)
        {
            return;
        }

        var install = Install;
        var downloader = _owner.Service.Downloader;
        _ = Task.Run(async () =>
        {
            try
            {
                var status = await downloader.CheckForUpdateAsync(install);
                Dispatcher.UIThread.Post(() => SetOutdated(status.NeedsUpdate
                    ? $"Your {install.Game} install is out of date ({status.OutdatedFiles:N0} core files, {GameFilesService.FormatBytes(status.BytesToDownload)}). Update the game files, or open the official launcher once."
                    : null));
            }
            catch (Exception ex)
            {
                _owner.Logger.LogInformation(ex, "Background update check for {Install} failed", install.Id);
            }
        });
    }

    internal void CancelOperation() => _cancellation?.Cancel();

    private async Task<DownloadPlan> PlanAsync(DownloadScope scope, CancellationToken token)
    {
        SetStatus("Contacting KingsIsle's patch server…");
        ProgressValue = 0;
        ProgressText = null;
        var downloader = _owner.Service.Downloader;
        var progress = new UiProgress<PlanProgress>(value =>
        {
            SetStatus("Checking game files…");
            ProgressValue = value.FilesTotal == 0 ? 0 : 100.0 * value.FilesChecked / value.FilesTotal;
            ProgressText = $"{value.FilesChecked:N0} of {value.FilesTotal:N0} files checked";
        });
        DownloadPlan plan;
        try
        {
            plan = await Task.Run(() => downloader.PlanAsync(Install, scope, progress, token), token);
        }
        finally
        {
            progress.Stop();
        }

        ProgressText = null;
        return plan;
    }

    private void ShowPlan(DownloadPlan plan)
    {
        if (plan.IsUpToDate)
        {
            if (plan.Scope == DownloadScope.FullGame)
            {
                SetOutdated(null);
            }

            DownloadButtonText = "Download full game";
            SetStatus($"The full game is downloaded and up to date (revision {plan.Revision}).");
            return;
        }

        DownloadButtonText = $"Download {GameFilesService.FormatBytes(plan.TotalBytes)}";
        var space = plan.AvailableFreeBytes is { } free ? $" · {GameFilesService.FormatBytes(free)} free on this drive" : "";
        SetStatus($"{plan.FileCount:N0} files, {GameFilesService.FormatBytes(plan.TotalBytes)} to download{space}.", isError: !plan.HasEnoughSpace);
    }

    private async Task<bool> ConfirmAsync(DownloadPlan plan, string title, string message)
    {
        if (!plan.HasEnoughSpace)
        {
            var text = $"This needs {GameFilesService.FormatBytes(plan.TotalBytes)} but only {GameFilesService.FormatBytes(plan.AvailableFreeBytes ?? 0)} is free on the game's drive. Free up some space and try again.";
            SetStatus(text, isError: true);
            await _owner.Dialogs.ShowMessageAsync("Not enough disk space", text);
            return false;
        }

        if (!EnsureNothingRunning())
        {
            return false;
        }

        return await _owner.Dialogs.ConfirmAsync(title, message, "Download");
    }

    private bool EnsureNothingRunning()
    {
        var reason = _owner.Service.GetBlockingReason();
        if (reason is null)
        {
            return true;
        }

        SetStatus(reason, isError: true);
        return false;
    }

    private async Task DownloadPlanAsync(DownloadPlan plan, CancellationToken token)
    {
        // The confirmation dialog may have been open for a while.
        if (!EnsureNothingRunning())
        {
            return;
        }

        SetStatus($"Downloading {GameFilesService.FormatBytes(plan.TotalBytes)}… Don't start the game until this finishes.");
        ProgressValue = 0;
        _speedBytes = 0;
        _speedTimestamp = Stopwatch.GetTimestamp();
        _bytesPerSecond = 0;
        var downloader = _owner.Service.Downloader;
        var progress = new UiProgress<DownloadProgress>(OnDownloadProgress);
        DownloadResult result;
        try
        {
            result = await Task.Run(() => downloader.DownloadAsync(plan, progress, token), token);
        }
        finally
        {
            progress.Stop();
        }

        ProgressText = null;

        if (result.Succeeded)
        {
            SetOutdated(null);
            DownloadButtonText = "Download full game";
            SetStatus(plan.Scope == DownloadScope.FullGame
                ? $"Done: {result.DownloadedFiles:N0} files ({GameFilesService.FormatBytes(result.DownloadedBytes)}) downloaded. The full game is installed (revision {result.Revision})."
                : $"Done: {result.DownloadedFiles:N0} files updated (revision {result.Revision}).");
            return;
        }

        var problems = result.InUse.Concat(result.Failed).ToList();
        var details = string.Join(Environment.NewLine, problems.Take(20).Select(problem => $"{problem.RelativePath}: {problem.Reason}"));
        if (problems.Count > 20)
        {
            details += Environment.NewLine + $"…and {problems.Count - 20} more (see the log).";
        }

        foreach (var problem in problems)
        {
            _owner.Logger.LogWarning("Game file {Path} was not downloaded: {Reason}", problem.RelativePath, problem.Reason);
        }

        var summary = result.InUse.Count > 0
            ? $"{result.InUse.Count:N0} files were in use and were skipped; close the game and run it again."
            : $"{result.Failed.Count:N0} files couldn't be downloaded. Run it again to retry them.";
        SetStatus($"Downloaded {result.DownloadedFiles:N0} files. {summary}", isError: true);
        await _owner.Dialogs.ShowErrorAsync("Some game files weren't downloaded", summary, details);
    }

    private void OnDownloadProgress(DownloadProgress value)
    {
        ProgressValue = value.BytesTotal == 0 ? 100 : 100.0 * value.BytesDone / value.BytesTotal;
        var now = Stopwatch.GetTimestamp();
        var elapsed = Stopwatch.GetElapsedTime(_speedTimestamp, now).TotalSeconds;
        if (elapsed >= 1)
        {
            var sample = Math.Max(0, value.BytesDone - _speedBytes) / elapsed;
            _bytesPerSecond = _bytesPerSecond == 0 ? sample : (_bytesPerSecond * 0.7) + (sample * 0.3);
            _speedBytes = value.BytesDone;
            _speedTimestamp = now;
        }

        var speed = _bytesPerSecond > 0 ? $" · {GameFilesService.FormatBytes((long)_bytesPerSecond)}/s" : "";
        ProgressText = $"{GameFilesService.FormatBytes(value.BytesDone)} of {GameFilesService.FormatBytes(value.BytesTotal)} · {value.FilesDone:N0} of {value.FilesTotal:N0} files{speed}";
    }

    /// <summary>Runs one operation with busy state, cancellation and user-readable errors (UI thread).</summary>
    private async Task RunAsync(Func<CancellationToken, Task> operation)
    {
        if (IsBusy || _owner.IsAnyBusy)
        {
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        IsBusy = true;
        IsError = false;
        _owner.OnBusyChanged();
        try
        {
            await operation(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            SetStatus("Cancelled. Files that finished downloading were kept; nothing was left half-written.");
        }
        catch (PatchServerException ex)
        {
            SetStatus(ex.Message, isError: true);
        }
        catch (InvalidDataException ex)
        {
            _owner.Logger.LogWarning(ex, "Unsafe or unreadable file list for {Install}", Install.Id);
            SetStatus("KingsIsle's file list looked unsafe or damaged, so nothing was downloaded. Try again later.", isError: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _owner.Logger.LogWarning(ex, "Game file operation failed for {Install}", Install.Id);
            SetStatus($"Couldn't write to the game folder: {ex.Message}", isError: true);
        }
        catch (Exception ex)
        {
            _owner.Logger.LogError(ex, "Game file operation failed for {Install}", Install.Id);
            SetStatus($"Something went wrong: {ex.Message}", isError: true);
        }
        finally
        {
            _cancellation = null;
            ProgressText = null;
            IsBusy = false;
            _owner.OnBusyChanged();
        }
    }

    private void SetStatus(string text, bool isError = false)
    {
        StatusText = text;
        IsError = isError;
    }

    private void SetOutdated(string? text)
    {
        OutdatedText = text;
        IsOutdated = text is not null;
    }

    /// <summary>
    /// Posts progress to the UI thread (Core reports from background threads, already throttled). Reports still queued
    /// when the operation ends are dropped so they can't overwrite the final status.
    /// </summary>
    private sealed class UiProgress<T>(Action<T> report) : IProgress<T>
    {
        private volatile bool _stopped;

        public void Report(T value) => Dispatcher.UIThread.Post(() =>
        {
            if (!_stopped)
            {
                report(value);
            }
        });

        /// <summary>Call on the UI thread when the operation has finished.</summary>
        public void Stop() => _stopped = true;
    }
}
