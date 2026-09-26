using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MultiWiz.App.Services;
using MultiWiz.Core.Storage;

namespace MultiWiz.App.ViewModels;

/// <summary>The About page: version, project links, log/data folders and the legal notice.</summary>
public sealed partial class AboutPageViewModel
{
    private readonly AppPaths _paths;
    private readonly ILogger<AboutPageViewModel> _logger;

    public AboutPageViewModel(AppPaths paths, UpdateService updates, ILogger<AboutPageViewModel> logger)
    {
        _paths = paths;
        _logger = logger;
        Version = updates.CurrentVersion;
        InstallKind = updates.IsInstalled ? "Installed build · updates enabled" : "Development build · updates disabled";
    }

    public string Version { get; }

    public string InstallKind { get; }

    public string LogFolder => _paths.LogsDirectory;

    public string DataFolder => _paths.DataDirectory;

    [RelayCommand]
    private void OpenRepository() => ShellLauncher.OpenUrl(AppInfo.RepositoryUrl, _logger);

    [RelayCommand]
    private void OpenReleases() => ShellLauncher.OpenUrl(AppInfo.ReleasesUrl, _logger);

    [RelayCommand]
    private void OpenIssues() => ShellLauncher.OpenUrl(AppInfo.IssuesUrl, _logger);

    [RelayCommand]
    private void OpenLogFolder() => ShellLauncher.OpenFolder(_paths.LogsDirectory, _logger);

    [RelayCommand]
    private void OpenDataFolder() => ShellLauncher.OpenFolder(_paths.DataDirectory, _logger);
}
