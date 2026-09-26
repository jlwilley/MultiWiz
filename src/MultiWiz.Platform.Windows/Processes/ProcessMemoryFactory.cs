using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using MultiWiz.Core.Live;
using Windows.Win32;
using Windows.Win32.System.Threading;

namespace MultiWiz.Platform.Windows.Processes;

/// <summary>Opens processes for READ-ONLY memory access (PROCESS_VM_READ | PROCESS_QUERY_LIMITED_INFORMATION).</summary>
internal sealed class ProcessMemoryFactory : IProcessMemoryFactory
{
    private readonly ILogger<ProcessMemoryFactory> _logger;

    public ProcessMemoryFactory(ILogger<ProcessMemoryFactory> logger)
    {
        _logger = logger;
    }

    public IProcessMemory? Open(int processId)
    {
        if (processId <= 0)
        {
            return null;
        }

        var handle = PInvoke.OpenProcess_SafeHandle(
            PROCESS_ACCESS_RIGHTS.PROCESS_VM_READ | PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION,
            false,
            (uint)processId);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            _logger.LogDebug("Could not open process {ProcessId} for reading (error {Error}).", processId, error);
            return null;
        }

        return new ProcessMemory(processId, handle, _logger);
    }
}
