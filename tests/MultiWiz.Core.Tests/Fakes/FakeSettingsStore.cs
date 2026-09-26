using MultiWiz.Core.Settings;

namespace MultiWiz.Core.Tests.Fakes;

internal sealed class FakeSettingsStore(AppSettings? initial = null) : ISettingsStore
{
    private readonly Lock _lock = new();
    private AppSettings _current = initial ?? new AppSettings();

    public event EventHandler<AppSettings>? Changed;

    public int UpdateCount { get; private set; }

    /// <summary>When set, <see cref="Update"/> throws this exception instead of saving (like a locked settings file).</summary>
    public Exception? UpdateException { get; set; }

    public AppSettings Current
    {
        get
        {
            lock (_lock)
            {
                return _current;
            }
        }
    }

    public void Update(Func<AppSettings, AppSettings> mutate)
    {
        if (UpdateException is { } exception)
        {
            throw exception;
        }

        AppSettings next;
        lock (_lock)
        {
            next = mutate(_current);
            _current = next;
            UpdateCount++;
        }

        Changed?.Invoke(this, next);
    }
}
