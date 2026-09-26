using MultiWiz.Core.Platform;
using MultiWiz.Core.Settings;

namespace MultiWiz.Core.Switching;

/// <summary>Decides the volume of every client from which one has focus.</summary>
public static class AudioPolicy
{
    /// <summary>
    /// Empty when <see cref="AudioSettings.Enabled"/> is false. Otherwise the focused process gets
    /// <see cref="AudioSettings.FocusedVolumePercent"/> and every other process <see cref="AudioSettings.UnfocusedVolumePercent"/>;
    /// when nothing is focused, every process gets the focused volume. Volumes are clamped to 0..100 and duplicate or
    /// non-positive process ids are ignored.
    /// </summary>
    public static IReadOnlyList<VolumeTarget> Compute(IReadOnlyList<int> processIds, int? focusedProcessId, AudioSettings settings)
    {
        ArgumentNullException.ThrowIfNull(processIds);
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.Enabled || processIds.Count == 0)
        {
            return [];
        }

        var focusedVolume = Math.Clamp(settings.FocusedVolumePercent, 0, 100);
        var unfocusedVolume = Math.Clamp(settings.UnfocusedVolumePercent, 0, 100);
        var targets = new List<VolumeTarget>(processIds.Count);
        var seen = new HashSet<int>();
        foreach (var processId in processIds)
        {
            if (processId <= 0 || !seen.Add(processId))
            {
                continue;
            }

            var volume = focusedProcessId is null || processId == focusedProcessId ? focusedVolume : unfocusedVolume;
            targets.Add(new VolumeTarget(processId, volume));
        }

        return targets;
    }
}
