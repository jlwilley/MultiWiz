namespace MultiWiz.Core.Platform;

public readonly record struct VolumeTarget(int ProcessId, int VolumePercent);

/// <summary>Per-process volume via Windows audio sessions.</summary>
public interface IAudioService : IDisposable
{
    /// <summary>
    /// Sets the volume of every audio session belonging to each process. Returns immediately; work is
    /// serialized on a background worker, and a newer call supersedes queued older ones.
    /// Remembers each process's original volume the first time it is changed.
    /// </summary>
    void Apply(IReadOnlyList<VolumeTarget> targets);

    /// <summary>Restores the original volume of a process (if it is still running) and forgets it.</summary>
    void Release(int processId);

    /// <summary>Synchronously restores every changed process to its original volume. Used on shutdown.</summary>
    void RestoreAll();
}
