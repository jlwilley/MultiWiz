using System.Security.Cryptography;
using System.Text;
using MultiWiz.Core.Games;
using MultiWiz.Core.Platform;
using MultiWiz.Core.Primitives;
using MultiWiz.Core.Security;

namespace MultiWiz.Core.Tests.Fakes;

internal sealed class FakeInstallLocator : IInstallLocator
{
    public List<GameInstall> Installs { get; } = [];

    public int DiscoverCalls { get; private set; }

    public IReadOnlyList<GameInstall> Discover()
    {
        DiscoverCalls++;
        return Installs.ToArray();
    }
}

internal sealed class FakeSteamSupport : ISteamSupport
{
    private readonly Lock _lock = new();
    private readonly List<GameInstall> _calls = [];
    private int _activeCalls;
    private int _maxConcurrentCalls;

    public SteamReadiness Result { get; set; } = new(true, null);

    /// <summary>Every call waits for this task before answering, like Steam that is still signing in.</summary>
    public Task SignedIn { get; set; } = Task.CompletedTask;

    public IReadOnlyList<GameInstall> Calls
    {
        get
        {
            lock (_lock)
            {
                return _calls.ToArray();
            }
        }
    }

    public int MaxConcurrentCalls
    {
        get
        {
            lock (_lock)
            {
                return _maxConcurrentCalls;
            }
        }
    }

    public async Task<SteamReadiness> EnsureReadyAsync(GameInstall install, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        BeginCall(install);
        try
        {
            await SignedIn.WaitAsync(cancellationToken);
            return Result;
        }
        finally
        {
            EndCall();
        }
    }

    private void BeginCall(GameInstall install)
    {
        lock (_lock)
        {
            _calls.Add(install);
            _activeCalls++;
            _maxConcurrentCalls = Math.Max(_maxConcurrentCalls, _activeCalls);
        }
    }

    private void EndCall()
    {
        lock (_lock)
        {
            _activeCalls--;
        }
    }
}

internal sealed class FakeCredentialVault : ICredentialVault
{
    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, (string Username, string Password)> _entries = new();

    public bool Save(Guid accountId, string username, string password)
    {
        lock (_lock)
        {
            _entries[accountId] = (username, password);
            return true;
        }
    }

    public string? GetPassword(Guid accountId)
    {
        lock (_lock)
        {
            return _entries.TryGetValue(accountId, out var entry) ? entry.Password : null;
        }
    }

    public string? GetUsername(Guid accountId)
    {
        lock (_lock)
        {
            return _entries.TryGetValue(accountId, out var entry) ? entry.Username : null;
        }
    }

    public bool Delete(Guid accountId)
    {
        lock (_lock)
        {
            return _entries.Remove(accountId);
        }
    }
}

internal sealed class FakeAudioService : IAudioService
{
    private readonly Lock _lock = new();
    private readonly List<IReadOnlyList<VolumeTarget>> _applied = [];
    private readonly List<int> _released = [];
    private int _restoreAllCount;

    public IReadOnlyList<IReadOnlyList<VolumeTarget>> Applied
    {
        get
        {
            lock (_lock)
            {
                return _applied.ToArray();
            }
        }
    }

    public IReadOnlyList<int> Released
    {
        get
        {
            lock (_lock)
            {
                return _released.ToArray();
            }
        }
    }

    public int RestoreAllCount
    {
        get
        {
            lock (_lock)
            {
                return _restoreAllCount;
            }
        }
    }

    /// <summary>Invoked at the start of every <see cref="Release"/>, on the caller's thread.</summary>
    public Action<int>? Releasing { get; set; }

    public void Apply(IReadOnlyList<VolumeTarget> targets)
    {
        lock (_lock)
        {
            _applied.Add(targets.ToArray());
        }
    }

    public void Release(int processId)
    {
        Releasing?.Invoke(processId);
        lock (_lock)
        {
            _released.Add(processId);
        }
    }

    public void RestoreAll()
    {
        lock (_lock)
        {
            _restoreAllCount++;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _applied.Clear();
            _released.Clear();
            _restoreAllCount = 0;
        }
    }

    public void Dispose()
    {
    }
}

internal sealed class FakeProcessThrottler : IProcessThrottler
{
    private readonly Lock _lock = new();
    private readonly List<(int ProcessId, bool Background, bool EfficiencyMode, bool LowerPriority)> _applied = [];
    private readonly List<int> _released = [];

    public IReadOnlyList<(int ProcessId, bool Background, bool EfficiencyMode, bool LowerPriority)> Applied
    {
        get
        {
            lock (_lock)
            {
                return _applied.ToArray();
            }
        }
    }

    public IReadOnlyList<int> Released
    {
        get
        {
            lock (_lock)
            {
                return _released.ToArray();
            }
        }
    }

    public void Apply(int processId, bool background, bool efficiencyMode, bool lowerPriority)
    {
        lock (_lock)
        {
            _applied.Add((processId, background, efficiencyMode, lowerPriority));
        }
    }

    public void Release(int processId)
    {
        lock (_lock)
        {
            _released.Add(processId);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _applied.Clear();
            _released.Clear();
        }
    }
}

internal sealed class FakeDisplayService : IDisplayService
{
    public List<MonitorInfo> Monitors { get; } = [];

    public static MonitorInfo Monitor(int index, int x, int y, int width, int height, int taskbarHeight = 40) =>
        new(index, $@"\\.\DISPLAY{index + 1}", new PixelRect(x, y, width, height), new PixelRect(x, y, width, height - taskbarHeight), 1.0, index == 0);

    public IReadOnlyList<MonitorInfo> GetMonitors() => Monitors.ToArray();
}

internal sealed class FakeWindowEvents : IWindowEvents
{
    public event EventHandler<ForegroundChangedEventArgs>? ForegroundChanged;

    public void RaiseForeground(nint windowHandle, int processId) =>
        ForegroundChanged?.Invoke(this, new ForegroundChangedEventArgs(windowHandle, processId));

    public IDisposable TrackWindow(nint hwnd, Action<WindowTrackingUpdate> onUpdate)
    {
        onUpdate(new WindowTrackingUpdate(null, IsMinimized: false, IsForeground: false, IsClosed: false));
        return new NoopDisposable();
    }

    public void Dispose()
    {
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}

/// <summary>"Encrypts" by prefixing a marker and XOR-ing; anything without the marker fails like DPAPI does.</summary>
internal sealed class FakeSecretProtector : ISecretProtector
{
    private static readonly byte[] Marker = "FAKE-DPAPI:"u8.ToArray();

    public byte[] Protect(byte[] plaintext) => [.. Marker, .. plaintext.Select(value => (byte)(value ^ 0x5A))];

    public byte[] Unprotect(byte[] protectedData)
    {
        if (protectedData.Length < Marker.Length || !protectedData.AsSpan(0, Marker.Length).SequenceEqual(Marker))
        {
            throw new CryptographicException("The data was not protected for this user.");
        }

        return protectedData.Skip(Marker.Length).Select(value => (byte)(value ^ 0x5A)).ToArray();
    }

    public string ProtectToBase64(string text) => Convert.ToBase64String(Protect(Encoding.UTF8.GetBytes(text)));
}
