using System.Text;
using MultiWiz.Core.Patching;

namespace MultiWiz.Core.Tests.Patching;

public sealed class KiCrc32Tests
{
    [Fact]
    public void Matches_the_kingsisle_check_value()
    {
        // Reflected 0xEDB88320, initial register 0, no final XOR (what sigil/umbra compute); standard CRC-32 is 0xCBF43926.
        Assert.Equal(0x2DFD2D88u, KiCrc32.Compute(Encoding.ASCII.GetBytes("123456789")));
        Assert.Equal(0u, KiCrc32.Compute([]));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(64)]
    [InlineData(1000)]
    [InlineData(65_537)]
    public void Fast_path_agrees_with_the_bitwise_reference(int length)
    {
        var data = new byte[length];
        new Random(length).NextBytes(data);

        Assert.Equal(KiCrc32.ComputeReference(data), KiCrc32.Compute(data));
    }

    [Fact]
    public void Incremental_hashing_equals_one_shot()
    {
        var data = new byte[10_000];
        new Random(42).NextBytes(data);
        var crc = new KiCrc32();
        crc.Append(data.AsSpan(0, 1));
        crc.Append(data.AsSpan(1, 4095));
        crc.Append(data.AsSpan(4096));

        Assert.Equal(KiCrc32.Compute(data), crc.GetCurrentHash());
        Assert.Equal(10_000L, crc.Length);

        crc.Reset();
        Assert.Equal(0u, crc.GetCurrentHash());
    }

    [Fact]
    public async Task File_crc_uses_the_same_convention()
    {
        using var temp = new Support.TempDirectory();
        var path = temp.Combine("file.bin");
        var data = new byte[3 * 1024 * 1024 + 17];
        new Random(7).NextBytes(data);
        await File.WriteAllBytesAsync(path, data);

        Assert.Equal(KiCrc32.ComputeReference(data), await GameDownloader.ComputeFileCrcAsync(path, CancellationToken.None));
    }
}
