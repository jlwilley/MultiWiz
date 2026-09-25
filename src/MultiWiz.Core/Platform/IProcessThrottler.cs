namespace MultiWiz.Core.Platform;

/// <summary>Reduces the CPU cost of background clients using OS-level knobs only.</summary>
public interface IProcessThrottler
{
    /// <summary>Applies efficiency mode (EcoQoS) and/or below-normal priority when <paramref name="background"/> is true; reverts when false.</summary>
    void Apply(int processId, bool background, bool efficiencyMode, bool lowerPriority);

    /// <summary>Reverts anything applied to the process and forgets it.</summary>
    void Release(int processId);
}
