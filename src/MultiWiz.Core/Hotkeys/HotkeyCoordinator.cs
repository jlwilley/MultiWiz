using System.Text;
using Microsoft.Extensions.Logging;
using MultiWiz.Core.Platform;
using MultiWiz.Core.Settings;
using MultiWiz.Core.Switching;

namespace MultiWiz.Core.Hotkeys;

/// <summary>
/// Registers the effective hotkey bindings (<see cref="DefaultHotkeys.Resolve"/>) and routes them: focus actions go
/// straight to <see cref="IClientSwitcher"/> on the platform message thread, everything else is raised as
/// <see cref="UiActionRequested"/>. Re-registers when the hotkey settings change.
/// </summary>
public sealed class HotkeyCoordinator : IHotkeyCoordinator
{
    private readonly IHotkeyService _hotkeys;
    private readonly IClientSwitcher _switcher;
    private readonly ISettingsStore _settings;
    private readonly ILogger<HotkeyCoordinator> _logger;
    private readonly Lock _lock = new();
    private readonly List<IDisposable> _registrations = [];
    private IReadOnlyList<HotkeyAction> _failedActions = [];
    private string? _registeredSignature;
    private bool _started;

    public HotkeyCoordinator(IHotkeyService hotkeys, IClientSwitcher switcher, ISettingsStore settings, ILogger<HotkeyCoordinator> logger)
    {
        _hotkeys = hotkeys;
        _switcher = switcher;
        _settings = settings;
        _logger = logger;
    }

    public event EventHandler<HotkeyAction>? UiActionRequested;

    public event EventHandler? RegistrationsChanged;

    public IReadOnlyList<HotkeyAction> FailedActions
    {
        get
        {
            lock (_lock)
            {
                return _failedActions;
            }
        }
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_started)
            {
                return;
            }

            _started = true;
            _settings.Changed += OnSettingsChanged;
            RegisterLocked(_settings.Current.Hotkeys);
        }

        RegistrationsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_started)
            {
                return;
            }

            _started = false;
            _settings.Changed -= OnSettingsChanged;
            UnregisterLocked();
            _failedActions = [];
        }

        RegistrationsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => Stop();

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        lock (_lock)
        {
            // Changed can arrive out of order when two threads update the settings at once, so register what is
            // current rather than what this notification carries.
            var hotkeys = _settings.Current.Hotkeys;
            if (!_started || Signature(hotkeys) == _registeredSignature)
            {
                return;
            }

            RegisterLocked(hotkeys);
        }

        RegistrationsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RegisterLocked(HotkeySettings hotkeys)
    {
        UnregisterLocked();
        _registeredSignature = Signature(hotkeys);

        if (!hotkeys.Enabled)
        {
            _failedActions = [];
            _logger.LogInformation("Global hotkeys are turned off");
            return;
        }

        var failed = new List<HotkeyAction>();
        var used = new HashSet<HotkeyBinding>();
        foreach (var action in Enum.GetValues<HotkeyAction>())
        {
            var text = DefaultHotkeys.Resolve(hotkeys.Bindings, action);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue; // Unbound on purpose.
            }

            if (!HotkeyBinding.TryParse(text, out var binding) || !binding.IsValid)
            {
                _logger.LogWarning("Hotkey {Binding} for {Action} is not a valid key combination", text, action);
                failed.Add(action);
                continue;
            }

            if (!used.Add(binding))
            {
                _logger.LogWarning("Hotkey {Binding} for {Action} is already used by another action", binding, action);
                failed.Add(action);
                continue;
            }

            IDisposable? registration;
            try
            {
                registration = _hotkeys.TryRegister(binding, () => OnHotkey(action));
            }
            catch (Exception ex)
            {
                // One broken registration must not stop the others or escape into the settings store's Changed event.
                _logger.LogWarning(ex, "Registering hotkey {Binding} for {Action} failed", binding, action);
                failed.Add(action);
                continue;
            }

            if (registration is null)
            {
                _logger.LogWarning("Hotkey {Binding} for {Action} could not be registered; another program probably uses it", binding, action);
                failed.Add(action);
                continue;
            }

            _registrations.Add(registration);
            if (binding.MayCollideWithAltGr)
            {
                // Helps explain "I can't type ś any more" reports from users with AltGr keyboard layouts.
                _logger.LogInformation(
                    "Hotkey {Binding} for {Action} is Ctrl+Alt plus a character key; on keyboard layouts with AltGr it takes that character over in every program",
                    binding, action);
            }
        }

        _failedActions = failed.ToArray();
        _logger.LogInformation("Registered {Count} global hotkeys ({Failed} failed)", _registrations.Count, failed.Count);
    }

    private void UnregisterLocked()
    {
        foreach (var registration in _registrations)
        {
            try
            {
                registration.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unregistering a hotkey failed");
            }
        }

        _registrations.Clear();
        _registeredSignature = null;
    }

    private void OnHotkey(HotkeyAction action)
    {
        try
        {
            switch (action)
            {
                case >= HotkeyAction.FocusSlot1 and <= HotkeyAction.FocusSlot8:
                    _switcher.FocusSlot(action - HotkeyAction.FocusSlot1);
                    break;
                case HotkeyAction.NextClient:
                    _switcher.FocusNext();
                    break;
                case HotkeyAction.PreviousClient:
                    _switcher.FocusPrevious();
                    break;
                default:
                    UiActionRequested?.Invoke(this, action);
                    break;
            }
        }
        catch (Exception ex)
        {
            // Hotkey callbacks run on the platform message loop, which must keep running.
            _logger.LogError(ex, "Handling hotkey {Action} failed", action);
        }
    }

    // Identifies what would be registered, so unrelated settings changes don't cause re-registration.
    private static string Signature(HotkeySettings hotkeys)
    {
        var builder = new StringBuilder(hotkeys.Enabled ? "on" : "off");
        foreach (var action in Enum.GetValues<HotkeyAction>())
        {
            builder.Append('|').Append(action).Append('=').Append(DefaultHotkeys.Resolve(hotkeys.Bindings, action));
        }

        return builder.ToString();
    }
}
