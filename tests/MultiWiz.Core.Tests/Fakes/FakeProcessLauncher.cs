using MultiWiz.Core.Platform;

namespace MultiWiz.Core.Tests.Fakes;

internal sealed record FakeLaunch(ProcessLaunchRequest Request, DateTimeOffset StartedAt, FakeLaunchedProcess Process);

internal sealed class FakeProcessLauncher(TimeProvider timeProvider) : IProcessLauncher
{
    private readonly Lock _lock = new();
    private readonly List<FakeLaunch> _launches = [];
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
            var process = new FakeLaunchedProcess(++_nextProcessId);
            _launches.Add(new FakeLaunch(request, timeProvider.GetUtcNow(), process));
            return process;
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

    public Task WaitForExitAsync(CancellationToken cancellationToken = default) => _exited.Task.WaitAsync(cancellationToken);

    public void Kill()
    {
        Interlocked.Increment(ref _killCount);
        Exit();
    }

    public void Exit() => _exited.TrySetResult();

    public void Dispose() => IsDisposed = true;
}
