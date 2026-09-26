using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using MultiWiz.Core.Platform;
using Windows.Win32;
using Windows.Win32.System.Threading;

namespace MultiWiz.Platform.Windows.Processes;

/// <summary>
/// Efficiency mode (EcoQoS) and below-normal priority for background clients. Remembers what it applied to each
/// process so it only touches processes when something changes and can revert exactly that.
/// </summary>
internal sealed class ProcessThrottler : IProcessThrottler
{
    private readonly ILogger<ProcessThrottler> _logger;
    private readonly Lock _gate = new();
    private readonly Dictionary<int, Throttling> _applied = new();

    public ProcessThrottler(ILogger<ProcessThrottler> logger)
    {
        _logger = logger;
    }

    [Flags]
    private enum Throttling
    {
        None = 0,
        EfficiencyMode = 1,
        LowerPriority = 2,
    }

    public void Apply(int processId, bool background, bool efficiencyMode, bool lowerPriority)
    {
        var desired = Throttling.None;
        if (background && efficiencyMode)
        {
            desired |= Throttling.EfficiencyMode;
        }

        if (background && lowerPriority)
        {
            desired |= Throttling.LowerPriority;
        }

        lock (_gate)
        {
            var current = _applied.GetValueOrDefault(processId);
            if (current == desired)
            {
                return;
            }

            var result = Change(processId, current, desired);
            if (result == Throttling.None)
            {
                _applied.Remove(processId);
            }
            else
            {
                _applied[processId] = result;
            }
        }
    }

    public void Release(int processId)
    {
        lock (_gate)
        {
            if (_applied.Remove(processId, out var current))
            {
                Change(processId, current, Throttling.None);
            }
        }
    }

    /// <summary>Moves a process from <paramref name="current"/> to <paramref name="desired"/>; returns what is now in effect.</summary>
    private Throttling Change(int processId, Throttling current, Throttling desired)
    {
        using var process = PInvoke.OpenProcess_SafeHandle(
            PROCESS_ACCESS_RIGHTS.PROCESS_SET_INFORMATION | PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION,
            false,
            (uint)processId);
        if (process.IsInvalid)
        {
            // Gone, or not ours to change: nothing is in effect any more that we could revert.
            _logger.LogDebug(
                "Could not open process {ProcessId} to adjust throttling (error {Error}).",
                processId, Marshal.GetLastPInvokeError());
            return Throttling.None;
        }

        var result = current;

        var wantEfficiency = desired.HasFlag(Throttling.EfficiencyMode);
        if (wantEfficiency != current.HasFlag(Throttling.EfficiencyMode) && SetEfficiencyMode(process, processId, wantEfficiency))
        {
            result = wantEfficiency ? result | Throttling.EfficiencyMode : result & ~Throttling.EfficiencyMode;
        }

        var wantLowerPriority = desired.HasFlag(Throttling.LowerPriority);
        if (wantLowerPriority != current.HasFlag(Throttling.LowerPriority) && SetLowerPriority(process, processId, wantLowerPriority))
        {
            result = wantLowerPriority ? result | Throttling.LowerPriority : result & ~Throttling.LowerPriority;
        }

        return result;
    }

    private bool SetEfficiencyMode(SafeFileHandle process, int processId, bool enable)
    {
        // ControlMask = EXECUTION_SPEED with StateMask = EXECUTION_SPEED turns EcoQoS on;
        // ControlMask = 0 hands the decision back to Windows.
        var state = new PROCESS_POWER_THROTTLING_STATE
        {
            Version = PInvoke.PROCESS_POWER_THROTTLING_CURRENT_VERSION,
            ControlMask = enable ? PInvoke.PROCESS_POWER_THROTTLING_EXECUTION_SPEED : 0u,
            StateMask = enable ? PInvoke.PROCESS_POWER_THROTTLING_EXECUTION_SPEED : 0u,
        };

        var bytes = MemoryMarshal.AsBytes(new ReadOnlySpan<PROCESS_POWER_THROTTLING_STATE>(in state));
        if (PInvoke.SetProcessInformation(process, PROCESS_INFORMATION_CLASS.ProcessPowerThrottling, bytes))
        {
            return true;
        }

        _logger.LogDebug(
            "Could not {Action} efficiency mode for process {ProcessId} (error {Error}).",
            enable ? "enable" : "disable", processId, Marshal.GetLastPInvokeError());
        return false;
    }

    private bool SetLowerPriority(SafeFileHandle process, int processId, bool lower)
    {
        var priority = lower
            ? PROCESS_CREATION_FLAGS.BELOW_NORMAL_PRIORITY_CLASS
            : PROCESS_CREATION_FLAGS.NORMAL_PRIORITY_CLASS;
        if (PInvoke.SetPriorityClass(process, priority))
        {
            return true;
        }

        _logger.LogDebug(
            "Could not set the priority of process {ProcessId} (error {Error}).",
            processId, Marshal.GetLastPInvokeError());
        return false;
    }
}
