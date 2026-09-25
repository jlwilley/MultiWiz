namespace MultiWiz.Core.Sessions;

public enum ClientSessionState
{
    Launching = 0,
    WaitingForWindow = 1,
    WaitingForReady = 2,
    LoggingIn = 3,
    Running = 4,
    Exited = 5,
    Failed = 6,
}

/// <summary>
/// Immutable snapshot of one running (or recently finished) game client.
/// <see cref="ISessionManager"/> publishes a new snapshot on every change.
/// </summary>
public sealed record ClientSession
{
    public required Guid AccountId { get; init; }
    public required ClientSessionState State { get; init; }
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>OS process id; 0 until the process has started.</summary>
    public int ProcessId { get; init; }

    /// <summary>Top-level game window handle; 0 until found.</summary>
    public nint WindowHandle { get; init; }

    /// <summary>Human-readable reason when <see cref="State"/> is <see cref="ClientSessionState.Failed"/>.</summary>
    public string? Error { get; init; }

    public bool IsAlive => State is ClientSessionState.Launching or ClientSessionState.WaitingForWindow
        or ClientSessionState.WaitingForReady or ClientSessionState.LoggingIn or ClientSessionState.Running;

    public bool HasWindow => WindowHandle != 0;
}
