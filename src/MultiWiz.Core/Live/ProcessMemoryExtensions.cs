using System.Runtime.InteropServices;
using System.Text;

namespace MultiWiz.Core.Live;

/// <summary>Typed, read-only helpers over <see cref="IProcessMemory"/>.</summary>
public static class ProcessMemoryExtensions
{
    // Reads never cross a 64-byte boundary, which divides the page size, so a short string at the end of a readable
    // region is not lost to a read that runs into the unreadable page after it.
    private const int StringChunkSize = 64;
    private const int MaxStackBytes = 1024;

    /// <summary>Reads an unmanaged value (little-endian, as laid out in memory). <paramref name="value"/> is default on failure.</summary>
    public static bool TryRead<T>(this IProcessMemory memory, nint address, out T value)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(memory);

        value = default;
        if (memory.TryRead(address, MemoryMarshal.AsBytes(new Span<T>(ref value))))
        {
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Reads a 64-bit pointer.</summary>
    public static bool TryReadPointer(this IProcessMemory memory, nint address, out nint value)
    {
        if (memory.TryRead(address, out long raw))
        {
            value = (nint)raw;
            return true;
        }

        value = 0;
        return false;
    }

    /// <summary>
    /// Reads a NUL-terminated UTF-8 string of at most <paramref name="maxBytes"/> bytes. If no NUL is found within
    /// <paramref name="maxBytes"/>, the bytes read so far are returned. <paramref name="value"/> is empty on failure.
    /// </summary>
    public static bool TryReadString(this IProcessMemory memory, nint address, int maxBytes, out string value)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);

        value = string.Empty;
        if (maxBytes == 0)
        {
            return true;
        }

        Span<byte> bytes = maxBytes <= MaxStackBytes ? stackalloc byte[maxBytes] : new byte[maxBytes];
        var read = 0;
        while (read < maxBytes)
        {
            var current = address + read;
            var offsetInChunk = (int)((nuint)current & (nuint)(StringChunkSize - 1));
            var count = Math.Min(StringChunkSize - offsetInChunk, maxBytes - read);
            var chunk = bytes.Slice(read, count);
            if (!memory.TryRead(current, chunk))
            {
                return false;
            }

            var terminator = chunk.IndexOf((byte)0);
            if (terminator >= 0)
            {
                value = Encoding.UTF8.GetString(bytes[..(read + terminator)]);
                return true;
            }

            read += count;
        }

        value = Encoding.UTF8.GetString(bytes);
        return true;
    }

    /// <summary>
    /// Follows a pointer chain the way memory tools write it: starting at <paramref name="baseAddress"/>, reads a
    /// pointer and adds the next offset, for every offset. With offsets [a, b] the result is <c>[[base] + a] + b</c>;
    /// the final address is not dereferenced. Fails on unreadable memory or a null pointer.
    /// </summary>
    public static bool TryFollowPointerChain(this IProcessMemory memory, nint baseAddress, ReadOnlySpan<int> offsets, out nint address)
    {
        ArgumentNullException.ThrowIfNull(memory);

        var current = baseAddress;
        foreach (var offset in offsets)
        {
            if (!memory.TryReadPointer(current, out var pointer) || pointer == 0)
            {
                address = 0;
                return false;
            }

            current = pointer + offset;
        }

        address = current;
        return true;
    }
}
