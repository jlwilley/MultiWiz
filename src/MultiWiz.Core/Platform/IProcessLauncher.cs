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
}

public interface IProcessLauncher
{
    /// <summary>Starts a process. Throws if the executable is missing or cannot be started.</summary>
    ILaunchedProcess Start(ProcessLaunchRequest request);
}
