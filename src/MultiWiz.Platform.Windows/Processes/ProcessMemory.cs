using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using MultiWiz.Core.Live;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.ProcessStatus;
using Windows.Win32.System.Threading;

namespace MultiWiz.Platform.Windows.Processes;

/// <summary>
/// READ-ONLY view of another process's memory. Handles are opened with read/query rights only; nothing in this
/// class can write memory, allocate in, or run code in the target process.
/// </summary>
internal sealed unsafe class ProcessMemory : IProcessMemory
{
    private const int MaxModuleNameLength = 260; // MAX_PATH
    private const int InitialModuleCapacity = 512;

    private readonly SafeFileHandle _readHandle;
    private readonly ILogger _logger;
    private readonly Lock _moduleGate = new();
    private readonly Dictionary<string, ModuleInfo> _moduleCache = new(StringComparer.OrdinalIgnoreCase);
    private SafeFileHandle? _queryHandle;
    private int _disposed;

    public ProcessMemory(int processId, SafeFileHandle readHandle, ILogger logger)
    {
        ProcessId = processId;
        _readHandle = readHandle;
        _logger = logger;
    }

    public int ProcessId { get; }

    public bool TryRead(nint address, Span<byte> buffer)
    {
        if (buffer.IsEmpty)
        {
            return true;
        }

        if (address == 0 || Volatile.Read(ref _disposed) != 0)
        {
            return false;
        }

        try
        {
            bool success = PInvoke.ReadProcessMemory(_readHandle, (void*)address, buffer, out nuint bytesRead);
            return success && bytesRead == (nuint)buffer.Length;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    public ModuleInfo? FindModule(string moduleName)
    {
        ArgumentException.ThrowIfNullOrEmpty(moduleName);

        lock (_moduleGate)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return null;
            }

            if (_moduleCache.TryGetValue(moduleName, out var cached))
            {
                return cached;
            }

            // Module enumeration needs PROCESS_QUERY_INFORMATION on some systems; if the limited handle is refused,
            // open a second handle with that right. It is still read-only.
            if (!TryFindModule(_readHandle, moduleName, out var module))
            {
                var queryHandle = GetQueryHandle();
                if (queryHandle is null || !TryFindModule(queryHandle, moduleName, out module))
                {
                    _logger.LogDebug(
                        "Could not enumerate the modules of process {ProcessId} (error {Error}).",
                        ProcessId, Marshal.GetLastPInvokeError());
                    return null;
                }
            }

            if (module is not null)
            {
                _moduleCache[moduleName] = module;
            }

            return module;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // Taking the lock waits for a module lookup in progress; reads in progress are protected by SafeHandle ref counts.
        lock (_moduleGate)
        {
            _readHandle.Dispose();
            _queryHandle?.Dispose();
            _queryHandle = null;
        }
    }

    private SafeFileHandle? GetQueryHandle()
    {
        if (_queryHandle is not null)
        {
            return _queryHandle.IsInvalid ? null : _queryHandle;
        }

        _queryHandle = PInvoke.OpenProcess_SafeHandle(
            PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_INFORMATION | PROCESS_ACCESS_RIGHTS.PROCESS_VM_READ,
            false,
            (uint)ProcessId);
        return _queryHandle.IsInvalid ? null : _queryHandle;
    }

    /// <summary>Returns false if the modules could not be enumerated; otherwise <paramref name="module"/> is the match or null.</summary>
    private static bool TryFindModule(SafeFileHandle process, string moduleName, out ModuleInfo? module)
    {
        module = null;

        var modules = new HMODULE[InitialModuleCapacity];
        int count;
        while (true)
        {
            if (!PInvoke.EnumProcessModulesEx(
                    process, MemoryMarshal.AsBytes(modules.AsSpan()), out uint bytesNeeded,
                    ENUM_PROCESS_MODULES_EX_FLAGS.LIST_MODULES_ALL))
            {
                return false;
            }

            count = (int)(bytesNeeded / (uint)sizeof(HMODULE));
            if (count <= modules.Length)
            {
                break;
            }

            // Modules were loaded between calls; retry with room to spare.
            modules = new HMODULE[count + 64];
        }

        var addedReference = false;
        process.DangerousAddRef(ref addedReference);
        try
        {
            var rawHandle = (HANDLE)process.DangerousGetHandle();
            char* nameBuffer = stackalloc char[MaxModuleNameLength];
            for (var i = 0; i < count; i++)
            {
                var length = PInvoke.GetModuleBaseName(rawHandle, modules[i], nameBuffer, MaxModuleNameLength);
                if (length == 0)
                {
                    continue;
                }

                var name = new string(nameBuffer, 0, (int)length);
                if (!string.Equals(name, moduleName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                MODULEINFO info;
                if (!PInvoke.GetModuleInformation(rawHandle, modules[i], &info, (uint)sizeof(MODULEINFO)))
                {
                    continue;
                }

                module = new ModuleInfo(name, (nint)info.lpBaseOfDll, (int)info.SizeOfImage);
                return true;
            }
        }
        finally
        {
            if (addedReference)
            {
                process.DangerousRelease();
            }
        }

        return true;
    }
}
