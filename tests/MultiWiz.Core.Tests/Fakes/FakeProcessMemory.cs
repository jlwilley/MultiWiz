using System.Buffers.Binary;
using MultiWiz.Core.Live;

namespace MultiWiz.Core.Tests.Fakes;

/// <summary>
/// A readable region of <c>size</c> bytes starting at <c>baseAddress</c>; everything else is unreadable.
/// The Set* helpers only fill the fake's own buffer for test setup.
/// </summary>
internal sealed class FakeProcessMemory(nint baseAddress, int size) : IProcessMemory
{
    private readonly byte[] _memory = new byte[size];
    private readonly List<(nint Address, int Length)> _reads = [];

    public int ProcessId => 4242;

    public nint BaseAddress { get; } = baseAddress;

    public IReadOnlyList<(nint Address, int Length)> Reads => _reads.ToArray();

    public bool TryRead(nint address, Span<byte> buffer)
    {
        _reads.Add((address, buffer.Length));
        var offset = (long)address - (long)BaseAddress;
        if (offset < 0 || offset + buffer.Length > _memory.Length)
        {
            return false;
        }

        _memory.AsSpan((int)offset, buffer.Length).CopyTo(buffer);
        return true;
    }

    public ModuleInfo? FindModule(string moduleName) =>
        string.Equals(moduleName, "Game.exe", StringComparison.OrdinalIgnoreCase) ? new ModuleInfo("Game.exe", BaseAddress, _memory.Length) : null;

    public void SetInt32(nint address, int value) => BinaryPrimitives.WriteInt32LittleEndian(Slice(address, sizeof(int)), value);

    public void SetInt64(nint address, long value) => BinaryPrimitives.WriteInt64LittleEndian(Slice(address, sizeof(long)), value);

    public void SetBytes(nint address, ReadOnlySpan<byte> bytes) => bytes.CopyTo(Slice(address, bytes.Length));

    public void Dispose()
    {
    }

    private Span<byte> Slice(nint address, int length) => _memory.AsSpan((int)(address - BaseAddress), length);
}
