using System.Text;
using MultiWiz.Core.Live;
using MultiWiz.Core.Tests.Fakes;

namespace MultiWiz.Core.Tests;

public sealed class BytePatternTests
{
    [Fact]
    public void Parses_hex_bytes_and_wildcards()
    {
        var pattern = BytePattern.Parse("48 8B 05 ?? ?? ?? ??");

        Assert.Equal(7, pattern.Length);
        Assert.Equal("48 8B 05 ?? ?? ?? ??", pattern.ToString());
        Assert.True(pattern.Matches([0x48, 0x8B, 0x05, 0x10, 0x20, 0x30, 0x40]));
        Assert.True(pattern.Matches([0x48, 0x8B, 0x05, 0x00, 0x00, 0x00, 0x00, 0xFF]));
        Assert.False(pattern.Matches([0x48, 0x8B, 0x06, 0x10, 0x20, 0x30, 0x40]));
        Assert.False(pattern.Matches([0x48, 0x8B, 0x05]));
    }

    [Theory]
    [InlineData("e8 ? ? ? ?", "E8 ?? ?? ?? ??")]
    [InlineData("  0  ff\t1A ", "00 FF 1A")]
    [InlineData("??", "??")]
    public void Accepts_short_forms_and_extra_whitespace(string text, string canonical)
    {
        Assert.True(BytePattern.TryParse(text, out var pattern));
        Assert.Equal(canonical, pattern.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("4G")]
    [InlineData("123")]
    [InlineData("48 8B ???")]
    [InlineData("0x48")]
    public void Rejects_invalid_patterns(string? text)
    {
        Assert.False(BytePattern.TryParse(text, out var pattern));
        Assert.Null(pattern);
        if (text is not null)
        {
            Assert.Throws<FormatException>(() => BytePattern.Parse(text));
        }
    }
}

public sealed class PatternScannerTests
{
    private static readonly byte[] Haystack = [0x90, 0x90, 0x48, 0x8B, 0x0D, 0x48, 0x8B, 0x05, 0x11, 0x22, 0x33, 0x44, 0xC3];

    [Fact]
    public void Finds_the_first_match_skipping_near_misses()
    {
        Assert.Equal(5, PatternScanner.IndexOf(Haystack, BytePattern.Parse("48 8B 05 ?? ?? ?? ??")));
        Assert.Equal(2, PatternScanner.IndexOf(Haystack, BytePattern.Parse("48 8B")));
    }

    [Fact]
    public void Leading_wildcards_are_allowed()
    {
        Assert.Equal(4, PatternScanner.IndexOf(Haystack, BytePattern.Parse("?? 48 8B 05")));
        Assert.Equal(1, PatternScanner.IndexOf(Haystack, BytePattern.Parse("? 48")));
    }

    [Fact]
    public void Finds_a_match_that_ends_at_the_last_byte()
    {
        Assert.Equal(11, PatternScanner.IndexOf(Haystack, BytePattern.Parse("44 C3")));
        Assert.Equal(9, PatternScanner.IndexOf(Haystack, BytePattern.Parse("22 ?? ?? C3")));
    }

    [Fact]
    public void Returns_minus_one_when_there_is_no_match()
    {
        Assert.Equal(-1, PatternScanner.IndexOf(Haystack, BytePattern.Parse("48 8B 07")));
        Assert.Equal(-1, PatternScanner.IndexOf(Haystack, BytePattern.Parse("C3 ??")));
        Assert.Equal(-1, PatternScanner.IndexOf([], BytePattern.Parse("90")));
        Assert.Equal(-1, PatternScanner.IndexOf([0x90], BytePattern.Parse("90 90")));
    }

    [Fact]
    public void Only_wildcards_match_at_the_start()
    {
        Assert.Equal(0, PatternScanner.IndexOf(Haystack, BytePattern.Parse("?? ??")));
    }

    [Fact]
    public void Scans_large_buffers()
    {
        var buffer = new byte[1 << 20];
        buffer.AsSpan().Fill(0x48);
        buffer[^3] = 0x8B;
        buffer[^2] = 0x05;

        Assert.Equal(buffer.Length - 4, PatternScanner.IndexOf(buffer, BytePattern.Parse("48 8B 05")));
    }
}

public sealed class ProcessMemoryExtensionsTests
{
    private const long Base = 0x10000;
    private readonly FakeProcessMemory _memory = new((nint)Base, 256);

    [Fact]
    public void Reads_unmanaged_values()
    {
        _memory.SetInt32((nint)(Base + 8), -123456);
        _memory.SetInt64((nint)(Base + 16), 0x1122334455667788);

        Assert.True(_memory.TryRead((nint)(Base + 8), out int small));
        Assert.Equal(-123456, small);
        Assert.True(_memory.TryRead((nint)(Base + 16), out long large));
        Assert.Equal(0x1122334455667788, large);
        Assert.True(_memory.TryRead((nint)(Base + 16), out byte low));
        Assert.Equal(0x88, low);
    }

    [Fact]
    public void Failed_reads_return_default()
    {
        Assert.False(_memory.TryRead((nint)(Base + 252), out long value));
        Assert.Equal(0, value);
        Assert.False(_memory.TryRead((nint)(Base - 4), out int before));
        Assert.Equal(0, before);
    }

    [Fact]
    public void Reads_pointers()
    {
        _memory.SetInt64((nint)(Base + 32), Base + 128);

        Assert.True(_memory.TryReadPointer((nint)(Base + 32), out var pointer));
        Assert.Equal((nint)(Base + 128), pointer);
        Assert.False(_memory.TryReadPointer((nint)(Base + 250), out var missing));
        Assert.Equal(0, missing);
    }

    [Fact]
    public void Reads_nul_terminated_utf8_strings()
    {
        _memory.SetBytes((nint)(Base + 40), "Grüße, Wizard!\0garbage"u8);

        Assert.True(_memory.TryReadString((nint)(Base + 40), 64, out var text));
        Assert.Equal("Grüße, Wizard!", text);
    }

    [Fact]
    public void Strings_without_a_terminator_are_cut_at_max_bytes()
    {
        _memory.SetBytes((nint)(Base + 40), "Merle Ambrose"u8);

        Assert.True(_memory.TryReadString((nint)(Base + 40), 5, out var text));
        Assert.Equal("Merle", text);
        Assert.True(_memory.TryReadString((nint)(Base + 40), 0, out var empty));
        Assert.Equal(string.Empty, empty);
    }

    [Fact]
    public void Short_strings_at_the_end_of_readable_memory_are_read()
    {
        // The region ends at Base + 256; asking for 64 bytes must not fail just because the string is near the end.
        _memory.SetBytes((nint)(Base + 250), "Hi\0"u8);

        Assert.True(_memory.TryReadString((nint)(Base + 250), 64, out var text));
        Assert.Equal("Hi", text);
        Assert.All(_memory.Reads, read => Assert.True((long)read.Address + read.Length <= Base + 256));
    }

    [Fact]
    public void Unreadable_strings_fail_with_an_empty_value()
    {
        Assert.False(_memory.TryReadString((nint)(Base + 1024), 16, out var text));
        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public void Follows_pointer_chains()
    {
        // [[Base] + 0x10] + 0x8
        _memory.SetInt64((nint)Base, Base + 0x40);
        _memory.SetInt64((nint)(Base + 0x50), Base + 0x80);

        Assert.True(_memory.TryFollowPointerChain((nint)Base, [0x10, 0x8], out var address));
        Assert.Equal((nint)(Base + 0x88), address);

        Assert.True(_memory.TryFollowPointerChain((nint)(Base + 0x20), [], out var unchanged));
        Assert.Equal((nint)(Base + 0x20), unchanged);
    }

    [Fact]
    public void Pointer_chains_fail_on_null_or_unreadable_pointers()
    {
        _memory.SetInt64((nint)Base, 0);
        Assert.False(_memory.TryFollowPointerChain((nint)Base, [0x10], out var fromNull));
        Assert.Equal(0, fromNull);

        _memory.SetInt64((nint)Base, 0x7FFF_0000);
        Assert.False(_memory.TryFollowPointerChain((nint)Base, [0x10, 0x8], out var fromUnreadable));
        Assert.Equal(0, fromUnreadable);
    }

    [Fact]
    public void Strings_are_decoded_as_utf8()
    {
        var bytes = Encoding.UTF8.GetBytes("Ravenwood ✨\0");
        _memory.SetBytes((nint)(Base + 100), bytes);

        Assert.True(_memory.TryReadString((nint)(Base + 100), 128, out var text));
        Assert.Equal("Ravenwood ✨", text);
    }
}
