using System.IO.Hashing;

namespace MultiWiz.Core.Patching;

/// <summary>
/// The CRC-32 KingsIsle publishes in its patch file lists: the reflected IEEE polynomial (0xEDB88320) with an
/// initial register of 0 and no final XOR. For "123456789" it is 0x2DFD2D88 (standard CRC-32 gives 0xCBF43926).
/// </summary>
/// <remarks>
/// This matches the hasher in sigil-w101launcher and umbra-launcher (Go <c>crc32.Update</c> seeded with 0xFFFFFFFF
/// and XORed with 0xFFFFFFFF at the end) and katsuba's KIWAD CRC. It is computed with the hardware-accelerated
/// <see cref="Crc32"/> (standard CRC-32) plus a length-only correction: the two variants differ only in the initial
/// register, and by linearity <c>ki(d) = ~ieee(d) ^ shift(0xFFFFFFFF, len(d))</c>, where <c>shift</c> feeds zero bytes
/// through the register. The shift is computed in O(log n) with the same GF(2) arithmetic zlib uses in
/// <c>crc32_combine</c>. Not thread-safe; use one instance per stream.
/// </remarks>
public sealed class KiCrc32
{
    private const uint ReflectedPolynomial = 0xEDB88320;
    private static readonly uint[] PowersOfX = BuildPowersOfX();

    private readonly Crc32 _ieee = new();
    private long _length;

    /// <summary>Number of bytes appended so far.</summary>
    public long Length => _length;

    public void Append(ReadOnlySpan<byte> data)
    {
        _ieee.Append(data);
        _length += data.Length;
    }

    public uint GetCurrentHash() => FromStandardCrc(_ieee.GetCurrentHashAsUInt32(), _length);

    public void Reset()
    {
        _ieee.Reset();
        _length = 0;
    }

    public static uint Compute(ReadOnlySpan<byte> data) => FromStandardCrc(Crc32.HashToUInt32(data), data.Length);

    /// <summary>Converts a standard CRC-32 of <paramref name="length"/> bytes into KingsIsle's variant.</summary>
    internal static uint FromStandardCrc(uint standardCrc, long length) =>
        ~standardCrc ^ MultiplyModP(XToThe8NModP(length), 0xFFFFFFFF);

    /// <summary>Bit-at-a-time reference implementation, used by tests to check the fast path.</summary>
    internal static uint ComputeReference(ReadOnlySpan<byte> data)
    {
        uint register = 0;
        foreach (var value in data)
        {
            register ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                register = (register & 1) != 0 ? (register >> 1) ^ ReflectedPolynomial : register >> 1;
            }
        }

        return register;
    }

    // Polynomials over GF(2) are stored reflected: bit 31 is x^0, bit 0 is x^31 (zlib's convention).
    private static uint MultiplyModP(uint a, uint b)
    {
        if (a == 0)
        {
            return 0;
        }

        uint mask = 1u << 31;
        uint product = 0;
        while (true)
        {
            if ((a & mask) != 0)
            {
                product ^= b;
                if ((a & (mask - 1)) == 0)
                {
                    break;
                }
            }

            mask >>= 1;
            b = (b & 1) != 0 ? (b >> 1) ^ ReflectedPolynomial : b >> 1;
        }

        return product;
    }

    /// <summary>x^(8n) mod P: the operator that feeds n zero bytes through the register.</summary>
    private static uint XToThe8NModP(long byteCount)
    {
        uint result = 1u << 31; // x^0
        var k = 3; // 8n = n * 2^3
        for (var n = byteCount; n != 0; n >>= 1, k++)
        {
            if ((n & 1) != 0)
            {
                result = MultiplyModP(PowersOfX[k & 31], result);
            }
        }

        return result;
    }

    /// <summary>PowersOfX[k] = x^(2^k) mod P.</summary>
    private static uint[] BuildPowersOfX()
    {
        var table = new uint[32];
        var p = 1u << 30; // x^1
        table[0] = p;
        for (var k = 1; k < table.Length; k++)
        {
            table[k] = p = MultiplyModP(p, p);
        }

        return table;
    }
}
