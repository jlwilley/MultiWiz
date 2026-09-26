using MultiWiz.Core.Games;

namespace MultiWiz.Core.Tests;

public sealed class SteamLibraryParserTests
{
    [Fact]
    public void Parses_modern_library_folders()
    {
        const string vdf = """
            "libraryfolders"
            {
            	"0"
            	{
            		"path"		"C:\\Program Files (x86)\\Steam"
            		"label"		""
            		"contentid"		"4121347219488151012"
            		"totalsize"		"0"
            		"update_clean_bytes_tally"		"0"
            		"time_last_update_verified"		"0"
            		"apps"
            		{
            			"228980"		"456296042"
            		}
            	}
            	"1"
            	{
            		"path"		"D:\\SteamLibrary"
            		"label"		"Games"
            		"apps"
            		{
            			"799960"		"8765432101"
            		}
            	}
            }
            """;

        Assert.Equal(new[] { @"C:\Program Files (x86)\Steam", @"D:\SteamLibrary" }, SteamLibraryParser.ParseLibraryFolders(vdf).ToArray());
    }

    [Fact]
    public void Parses_legacy_library_folders()
    {
        const string vdf = """
            "LibraryFolders"
            {
            	"TimeNextStatsReport"		"1612345678"
            	"ContentStatsID"		"-5271863125462453789"
            	"1"		"D:\\Games\\Steam Library"
            	"2"		"E:\\SteamLibrary"
            }
            """;

        Assert.Equal(new[] { @"D:\Games\Steam Library", @"E:\SteamLibrary" }, SteamLibraryParser.ParseLibraryFolders(vdf).ToArray());
    }

    [Fact]
    public void Skips_duplicates_comments_and_empty_paths()
    {
        const string vdf = """
            // Written by hand
            "libraryfolders"
            {
            	"0" { "path" "D:\\SteamLibrary" }
            	"1" { "path" "d:\\steamlibrary\\" }
            	"2" { "label" "no path here" }
            	"3" ""
            	"4" "E:\\Other" [$WIN32]
            }
            """;

        Assert.Equal(new[] { @"D:\SteamLibrary", @"E:\Other" }, SteamLibraryParser.ParseLibraryFolders(vdf).ToArray());
    }

    [Fact]
    public void Keeps_unescaped_backslashes_and_unescapes_quotes()
    {
        const string vdf = """
            "libraryfolders"
            {
            	"0" { "path" "D:\Games\Steam" }
            	"1" { "path" "E:\\Say \"hi\"" }
            }
            """;

        Assert.Equal(new[] { @"D:\Games\Steam", "E:\\Say \"hi\"" }, SteamLibraryParser.ParseLibraryFolders(vdf).ToArray());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a vdf file at all")]
    [InlineData("\"libraryfolders\" { \"0\" { \"path\" ")]
    [InlineData("}}}{{{")]
    public void Malformed_library_files_yield_no_paths_without_throwing(string vdf)
    {
        Assert.Empty(SteamLibraryParser.ParseLibraryFolders(vdf));
    }

    [Fact]
    public void Parses_an_app_manifest()
    {
        const string acf = """
            "AppState"
            {
            	"appid"		"799960"
            	"Universe"		"1"
            	"LauncherPath"		"C:\\Program Files (x86)\\Steam\\steam.exe"
            	"name"		"Wizard101"
            	"StateFlags"		"4"
            	"installdir"		"Wizard101"
            	"InstalledDepots"
            	{
            		"799961"
            		{
            			"manifest"		"123"
            		}
            	}
            }
            """;

        var manifest = SteamLibraryParser.ParseAppManifest(acf);

        Assert.NotNull(manifest);
        Assert.Equal("799960", manifest.Value.AppId);
        Assert.Equal("Wizard101", manifest.Value.InstallDir);
    }

    [Fact]
    public void App_manifest_keys_are_case_insensitive()
    {
        var manifest = SteamLibraryParser.ParseAppManifest("\"AppState\" { \"AppID\" \"12345\" \"InstallDir\" \"Pirate101\" }");

        Assert.Equal(("12345", "Pirate101"), manifest);
    }

    [Theory]
    [InlineData("\"AppState\" { \"appid\" \"799960\" }")]
    [InlineData("\"AppState\" { \"installdir\" \"Wizard101\" }")]
    [InlineData("\"AppState\" { \"appid\" \"\" \"installdir\" \"Wizard101\" }")]
    [InlineData("garbage")]
    [InlineData("")]
    public void Incomplete_app_manifests_return_null(string acf)
    {
        Assert.Null(SteamLibraryParser.ParseAppManifest(acf));
    }
}
