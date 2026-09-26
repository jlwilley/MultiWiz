namespace MultiWiz.Core.Live;

/// <summary>Finds <see cref="BytePattern"/>s in memory that has already been read.</summary>
public static class PatternScanner
{
    /// <summary>
    /// Index of the first match of <paramref name="pattern"/> in <paramref name="haystack"/>, or -1. Uses a vectorized
    /// search for the pattern's first non-wildcard byte to skip ahead, then verifies the whole pattern.
    /// </summary>
    public static int IndexOf(ReadOnlySpan<byte> haystack, BytePattern pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        var length = pattern.Length;
        if (length == 0 || haystack.Length < length)
        {
            return -1;
        }

        var anchor = pattern.AnchorIndex;
        if (anchor < 0)
        {
            return 0; // Only wildcards: matches at the start.
        }

        var anchorByte = pattern.AnchorByte;
        var lastStart = haystack.Length - length;
        var start = 0;
        while (start <= lastStart)
        {
            // Candidate starts are start..lastStart, so their anchor bytes sit at start+anchor..lastStart+anchor.
            var found = haystack.Slice(start + anchor, lastStart - start + 1).IndexOf(anchorByte);
            if (found < 0)
            {
                return -1;
            }

            var candidate = start + found;
            if (pattern.Matches(haystack[candidate..]))
            {
                return candidate;
            }

            start = candidate + 1;
        }

        return -1;
    }
}
