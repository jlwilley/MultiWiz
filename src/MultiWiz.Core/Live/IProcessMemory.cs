namespace MultiWiz.Core.Live;

public sealed record ModuleInfo(string Name, nint BaseAddress, int Size);

/// <summary>
/// READ-ONLY view of another process's memory, the foundation for live overlays (stats, duel state).
/// Implementations must open the process with read/query rights only and must never write memory,
/// inject code, create remote threads, or hook functions.
/// </summary>
public interface IProcessMemory : IDisposable
{
    int ProcessId { get; }

    /// <summary>Fills <paramref name="buffer"/> from <paramref name="address"/>. Returns false on any failure (never partial success).</summary>
    bool TryRead(nint address, Span<byte> buffer);

    /// <summary>A loaded module by file name (case-insensitive), e.g. "WizardGraphicalClient.exe".</summary>
    ModuleInfo? FindModule(string moduleName);
}

public interface IProcessMemoryFactory
{
    /// <summary>Opens a process for reading, or null if it is gone or access is denied.</summary>
    IProcessMemory? Open(int processId);
}
