using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MultiWiz.Core.Platform;

namespace MultiWiz.Platform.Windows.Processes;

/// <summary>Starts game clients directly (no shell) with an explicit working directory.</summary>
internal sealed class ProcessLauncher : IProcessLauncher
{
    private readonly ILogger<ProcessLauncher> _logger;

    public ProcessLauncher(ILogger<ProcessLauncher> logger)
    {
        _logger = logger;
    }

    public ILaunchedProcess Start(ProcessLaunchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!File.Exists(request.ExecutablePath))
        {
            throw new FileNotFoundException("The game executable was not found.", request.ExecutablePath);
        }

        var workingDirectory = string.IsNullOrWhiteSpace(request.WorkingDirectory)
            ? Path.GetDirectoryName(request.ExecutablePath) ?? string.Empty
            : request.WorkingDirectory;

        var startInfo = new ProcessStartInfo
        {
            FileName = request.ExecutablePath,
            Arguments = request.Arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
        };

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Windows did not start {Path.GetFileName(request.ExecutablePath)}.");

        _logger.LogInformation(
            "Started {Executable} as process {ProcessId} with arguments {Arguments}.",
            request.ExecutablePath, process.Id, request.Arguments);
        return new LaunchedProcess(process, _logger);
    }
}
