using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace MultiWiz.Core.Live;

/// <summary>A byte signature with wildcards, written as hex bytes separated by spaces: <c>"48 8B 05 ?? ?? ?? ??"</c>.</summary>
public sealed class BytePattern
{
    private static readonly char[] Separators = [' ', '\t', '\r', '\n'];

    private readonly byte[] _bytes;
    private readonly bool[] _isFixed;

    private BytePattern(byte[] bytes, bool[] isFixed)
    {
        _bytes = bytes;
        _isFixed = isFixed;
        AnchorIndex = Array.IndexOf(isFixed, true);
    }

    /// <summary>Number of bytes the pattern covers, wildcards included.</summary>
    public int Length => _bytes.Length;

    /// <summary>Index of the first non-wildcard byte, or -1 when every byte is a wildcard.</summary>
    internal int AnchorIndex { get; }

    /// <summary>The byte at <see cref="AnchorIndex"/> (0 when there is none).</summary>
    internal byte AnchorByte => AnchorIndex >= 0 ? _bytes[AnchorIndex] : (byte)0;

    /// <summary>Parses hex bytes (<c>"8B"</c>) and wildcards (<c>"?"</c> or <c>"??"</c>) separated by whitespace.</summary>
    /// <exception cref="FormatException">The text is empty or contains something else.</exception>
    public static BytePattern Parse(string pattern) =>
        TryParse(pattern, out var result)
            ? result
            : throw new FormatException($"'{pattern}' is not a byte pattern. Use hex bytes separated by spaces, with ? or ?? as wildcards.");

    public static bool TryParse([NotNullWhen(true)] string? pattern, [NotNullWhen(true)] out BytePattern? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        var tokens = pattern.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        var bytes = new byte[tokens.Length];
        var isFixed = new bool[tokens.Length];
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (token is "?" or "??")
            {
                continue;
            }

            if (token.Length > 2 || !byte.TryParse(token, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out bytes[i]))
            {
                return false;
            }

            isFixed[i] = true;
        }

        result = new BytePattern(bytes, isFixed);
        return true;
    }

    /// <summary>True when the first <see cref="Length"/> bytes of <paramref name="data"/> match the pattern.</summary>
    public bool Matches(ReadOnlySpan<byte> data)
    {
        if (data.Length < _bytes.Length)
        {
            return false;
        }

        for (var i = 0; i < _bytes.Length; i++)
        {
            if (_isFixed[i] && data[i] != _bytes[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The pattern in canonical form, e.g. <c>"48 8B 05 ?? ??"</c>.</summary>
    public override string ToString() =>
        string.Join(' ', _bytes.Select((value, index) => _isFixed[index] ? value.ToString("X2", CultureInfo.InvariantCulture) : "??"));
}
