using MultiWiz.Core.Patching;

namespace MultiWiz.Core.Tests.Patching;

public sealed class FileListParserTests
{
    [Fact]
    public void Parses_packages_and_skips_meta_tables()
    {
        var bytes = FileListBuilder.Build(
        [
            new FakeFile("Base", "Bin/WizardGraphicalClient.exe", [1, 2, 3]),
            new FakeFile("Base", "Data/GameData/Root.wad", [4, 5], Target: "Data/GameData/Root.wad"),
            new FakeFile("WizardCity-WC_Hub", "Data/GameData/WizardCity-WC_Hub.wad", [6], FileType: PatchFileTypes.DynamicWad),
        ]);

        var packages = FileListParser.ParsePackages(bytes);

        Assert.Equal(new[] { "Base", "WizardCity-WC_Hub" }, packages.Select(p => p.Name).ToArray());
        var exe = packages[0].Files[0];
        Assert.Equal("Bin/WizardGraphicalClient.exe", exe.SourceName);
        Assert.Equal("Bin/WizardGraphicalClient.exe", exe.TargetName); // empty TarFileName falls back to the source
        Assert.Equal(3u, exe.Size);
        Assert.Equal(KiCrc32.Compute([1, 2, 3]), exe.Crc);
        Assert.Equal(1u, exe.FileType);
        Assert.False(exe.IsDynamicWad);
        Assert.True(packages[1].Files[0].IsDynamicWad);
        Assert.True(packages[0].IsBase);
    }

    [Fact]
    public void Reads_every_value_type_generically()
    {
        using var output = new MemoryStream();
        DmlBuilder.WriteTable(output, "About",
            [("Text", DmlBuilder.Str), ("Small", DmlBuilder.U8), ("Short", DmlBuilder.U16), ("Big", DmlBuilder.U64), ("Number", DmlBuilder.U32)],
            [["hello", (byte)7, (ushort)300, 1UL << 40, 99u]]);

        var table = Assert.Single(FileListParser.ParseTables(output.ToArray()));

        Assert.Equal("About", table.Name);
        var record = Assert.Single(table.Records);
        Assert.Equal("hello", record["Text"]);
        Assert.Equal((byte)7, record["Small"]);
        Assert.Equal((ushort)300, record["Short"]);
        Assert.Equal(1UL << 40, record["Big"]);
        Assert.Equal(99u, record["Number"]);
    }

    [Fact]
    public void Empty_file_list_has_no_tables() => Assert.Empty(FileListParser.ParseTables([]));

    [Fact]
    public void Truncated_data_is_rejected()
    {
        var bytes = FileListBuilder.Build([new FakeFile("Base", "Bin/a.dll", [1, 2, 3])]);

        Assert.Throws<InvalidDataException>(() => FileListParser.ParsePackages(bytes.AsSpan(0, bytes.Length - 3).ToArray()));
    }

    [Fact]
    public void Absurd_record_counts_are_rejected()
    {
        using var output = new MemoryStream();
        DmlBuilder.WriteTable(output, "Base", FileListBuilder.FileFields, []);
        var bytes = output.ToArray();
        bytes[0] = 0xFF;
        bytes[1] = 0xFF;
        bytes[2] = 0xFF;

        Assert.Throws<InvalidDataException>(() => FileListParser.ParseTables(bytes));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData(@"a\b")]
    [InlineData("C:x")]
    public void Unsafe_package_names_are_rejected(string name)
    {
        using var output = new MemoryStream();
        DmlBuilder.WriteTable(output, name, FileListBuilder.FileFields, []);

        Assert.Throws<InvalidDataException>(() => FileListParser.ParsePackages(output.ToArray()));
    }
}

public sealed class PatchPathsTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "mw-root");

    [Theory]
    [InlineData("Bin/WizardGraphicalClient.exe", "Bin|WizardGraphicalClient.exe")]
    [InlineData(@"Data\GameData\Root.wad", "Data|GameData|Root.wad")]
    [InlineData("./Data//x.wad", "Data|x.wad")]
    public void Resolves_relative_paths_inside_the_install(string target, string expectedParts)
    {
        Assert.Equal(Path.Combine([Path.GetFullPath(Root), .. expectedParts.Split('|')]), PatchPaths.ResolveTarget(Root, target));
    }

    [Theory]
    [InlineData("../outside.dll")]
    [InlineData(@"..\outside.dll")]
    [InlineData("Data/../../outside.dll")]
    [InlineData("/etc/passwd")]
    [InlineData(@"\Windows\evil.dll")]
    [InlineData(@"C:\Windows\evil.dll")]
    [InlineData("C:relative.dll")]
    [InlineData("file.dll:stream")]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("Data/..")]
    public void Rejects_paths_that_escape_the_install(string target)
    {
        Assert.Throws<InvalidDataException>(() => PatchPaths.ResolveTarget(Root, target));
    }

    [Fact]
    public void Builds_escaped_download_urls()
    {
        Assert.Equal("https://cdn.example.test/r1/Data/Game%20Data/a.wad", PatchPaths.BuildFileUrl("https://cdn.example.test/r1/", "Data/Game Data/a.wad").AbsoluteUri);
        Assert.Throws<InvalidDataException>(() => PatchPaths.BuildFileUrl("https://cdn.example.test/r1", "../x"));
        Assert.Throws<InvalidDataException>(() => PatchPaths.BuildFileUrl("file:///c:/", "x"));
    }
}
