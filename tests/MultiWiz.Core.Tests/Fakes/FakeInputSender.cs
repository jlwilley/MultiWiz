using MultiWiz.Core.Platform;

namespace MultiWiz.Core.Tests.Fakes;

/// <summary>Records input as "hwnd:text:value" and "hwnd:key:vk" entries.</summary>
internal sealed class FakeInputSender : IInputSender
{
    private readonly Lock _lock = new();
    private readonly List<string> _log = [];

    /// <summary>When set, <see cref="SendTextAsync"/> fails with this exception (like a window that went away).</summary>
    public Exception? SendException { get; set; }

    public IReadOnlyList<string> Log
    {
        get
        {
            lock (_lock)
            {
                return _log.ToArray();
            }
        }
    }

    public Task SendTextAsync(nint hwnd, string text, TimeSpan perCharacterDelay, CancellationToken cancellationToken = default)
    {
        if (SendException is { } exception)
        {
            return Task.FromException(exception);
        }

        lock (_lock)
        {
            _log.Add($"{hwnd}:text:{text}");
        }

        return Task.CompletedTask;
    }

    public Task SendKeyAsync(nint hwnd, int virtualKey, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _log.Add($"{hwnd}:key:{virtualKey}");
        }

        return Task.CompletedTask;
    }
}
