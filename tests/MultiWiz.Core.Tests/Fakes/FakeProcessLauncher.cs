using MultiWiz.Core.Games;
using MultiWiz.Core.Platform;

namespace MultiWiz.Core.Tests.Fakes;

internal sealed record FakeLaunch(ProcessLaunchRequest Request, DateTimeOffset StartedAt, FakeLaunchedProcess Process);

internal sealed class FakeProcessLauncher(TimeProvider timeProvider) : IProcessLauncher
{
    private readonly Lock _lock = new();
    private readonly List<FakeLaunch> _launches = [];
    private readonly List<FakeLaunchedProcess> _others = [];
    private int _nextProcessId = 1000;

    public Exception? StartException { get; set; }

    public IReadOnlyList<FakeLaunch> Launches
    {
        get
        {
            lock (_lock)
            {
                return _launches.ToArray();
            }
        }
    }

    public ILaunchedProcess Start(ProcessLaunchRequest request)
    {
        if (StartException is { } exception)
        {
            throw exception;
        }

        lock (_lock)
        {
            var process = new FakeLaunchedProcess(++_nextProcessId) { StartTime = timeProvider.GetUtcNow() };
            _launches.Add(new FakeLaunch(request, timeProvider.GetUtcNow(), process));
            return process;
        }
    }

    /// <summary>Makes a process that this launcher did not start (another program, or a reused id) attachable.</summary>
    public void AddRunning(FakeLaunchedProcess process)
    {
        lock (_lock)
        {
            _others.Add(process);
        }
    }

    /// <summary>Returns the running process with the id, like opening it by id; the same object each time.</summary>
    public ILaunchedProcess? TryAttach(int processId)
    {
        lock (_lock)
        {
            return _launches.Select(launch => launch.Process).Concat(_others)
                .FirstOrDefault(process => process.Id == processId && !process.HasExited);
        }
    }
}

internal sealed class FakeLaunchedProcess(int id) : ILaunchedProcess
{
    // Asynchronous continuations, like a real process exit notification arriving on another thread.
    private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _killCount;

    public int Id { get; } = id;

    public bool HasExited => _exited.Task.IsCompleted;

    public int KillCount => Volatile.Read(ref _killCount);

    public bool IsDisposed { get; private set; }

    public DateTimeOffset? StartTime { get; set; }

    public string? ProcessName { get; set; } = GameExecutables.ClientProcessName(GameKind.Wizard101);

    public Task WaitForExitAsync(CancellationToken cancellationToken = default) => _exited.Task.WaitAsync(cancellationToken);

    public void Kill()
    {
        Interlocked.Increment(ref _killCount);
        Exit();
    }

    public void Exit() => _exited.TrySetResult();

    public void Dispose() => IsDisposed = true;
}
