using MultiWiz.Core.Hotkeys;
using MultiWiz.Core.Platform;

namespace MultiWiz.Core.Tests.Fakes;

internal sealed class FakeHotkeyService : IHotkeyService
{
    private readonly Lock _lock = new();
    private readonly Dictionary<HotkeyBinding, Action> _active = new();
    private readonly HashSet<HotkeyBinding> _taken = [];
    private readonly HashSet<HotkeyBinding> _broken = [];
    private int _registerCalls;

    public int RegisterCalls
    {
        get
        {
            lock (_lock)
            {
                return _registerCalls;
            }
        }
    }

    public IReadOnlyList<HotkeyBinding> Active
    {
        get
        {
            lock (_lock)
            {
                return _active.Keys.ToArray();
            }
        }
    }

    /// <summary>Simulates another program owning the combination.</summary>
    public void MarkTaken(string binding)
    {
        lock (_lock)
        {
            _taken.Add(Parse(binding));
        }
    }

    /// <summary>Makes registering the combination throw, like a platform service that failed or was disposed.</summary>
    public void MarkBroken(string binding)
    {
        lock (_lock)
        {
            _broken.Add(Parse(binding));
        }
    }

    public bool IsActive(string binding)
    {
        lock (_lock)
        {
            return _active.ContainsKey(Parse(binding));
        }
    }

    /// <summary>Invokes the callback registered for the combination. Returns false if nothing is registered.</summary>
    public bool Press(string binding)
    {
        Action? callback;
        lock (_lock)
        {
            _active.TryGetValue(Parse(binding), out callback);
        }

        callback?.Invoke();
        return callback is not null;
    }

    public IDisposable? TryRegister(HotkeyBinding binding, Action callback)
    {
        lock (_lock)
        {
            _registerCalls++;
            if (_broken.Contains(binding))
            {
                throw new InvalidOperationException($"Registering {binding} failed.");
            }

            if (!binding.IsValid || _taken.Contains(binding) || _active.ContainsKey(binding))
            {
                return null;
            }

            _active[binding] = callback;
            return new Registration(this, binding);
        }
    }

    public void Dispose()
    {
    }

    private void Unregister(HotkeyBinding binding)
    {
        lock (_lock)
        {
            _active.Remove(binding);
        }
    }

    private static HotkeyBinding Parse(string text) =>
        HotkeyBinding.TryParse(text, out var binding) ? binding : throw new ArgumentException($"Bad binding '{text}'", nameof(text));

    private sealed class Registration(FakeHotkeyService owner, HotkeyBinding binding) : IDisposable
    {
        public void Dispose() => owner.Unregister(binding);
    }
}
