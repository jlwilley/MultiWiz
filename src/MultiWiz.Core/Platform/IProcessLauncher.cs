namespace MultiWiz.Core.Platform;

public sealed record ProcessLaunchRequest(string ExecutablePath, string WorkingDirectory, string Arguments);

/// <summary>A process started by <see cref="IProcessLauncher"/>.</summary>
public interface ILaunchedProcess : IDisposable
{
    int Id { get; }
    bool HasExited { get; }
    Task WaitForExitAsync(CancellationToken cancellationToken = default);

    /// <summary>Kills the process; no-op if it already exited.</summary>
    void Kill();

    /// <summary>
    /// When the process started, or null if unknown. Process ids are reused, so this is what identifies a client
    /// across a MultiWiz restart (see <see cref="IProcessLauncher.TryAttach"/>). May throw once the process exited.
    /// </summary>
    DateTimeOffset? StartTime => null;

    /// <summary>The executable name without extension as the OS reports it (e.g. "WizardGraphicalClient"), or null if unknown.</summary>
    string? ProcessName => null;
}

public interface IProcessLauncher
{
    /// <summary>Starts a process. Throws if the executable is missing or cannot be started.</summary>
    ILaunchedProcess Start(ProcessLaunchRequest request);

    /// <summary>
    /// Opens a process that is already running, such as a client started before MultiWiz restarted, so it can be
    /// watched and stopped like a started one. Returns null if no process has that id or it cannot be opened. The
    /// result must report <see cref="ILaunchedProcess.StartTime"/> and <see cref="ILaunchedProcess.ProcessName"/>:
    /// a process that does not is never adopted, because its id may have been reused by another program.
    /// The default adopts nothing.
    /// </summary>
    ILaunchedProcess? TryAttach(int processId) => null;
}
