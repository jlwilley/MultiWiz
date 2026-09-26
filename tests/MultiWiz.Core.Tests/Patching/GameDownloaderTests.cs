using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MultiWiz.Core.Games;
using MultiWiz.Core.Patching;
using MultiWiz.Core.Tests.Support;

namespace MultiWiz.Core.Tests.Patching;

public sealed class GameDownloaderTests : IDisposable
{
    private const string ListUrl = "https://cdn.example.test/r900/LatestFileList.bin";
    private const string Prefix = "https://cdn.example.test/r900/files";

    private readonly TempDirectory _temp = new();
    private readonly FakeHttpHandler _http = new();
    private readonly string _root;
    private readonly GameInstall _install;
    private List<FakeFile> _files = [];
    private byte[] _fileList = [];

    public GameDownloaderTests()
    {
        _root = _temp.CreateGameFolder("Wizard101", "WizardGraphicalClient.exe");
        _install = new GameInstall { Id = "standalone-wizard101", Game = GameKind.Wizard101, Source = InstallSource.Standalone, RootPath = _root };
    }

    public void Dispose() => _temp.Dispose();

    private static readonly byte[] ExeV2 = [0x4D, 0x5A, 2, 2, 2, 2];

    private void Publish(params FakeFile[] files)
    {
        _files = files.ToList();
        _fileList = FileListBuilder.Build(files);
        _http.Serve(ListUrl, _fileList);
        foreach (var file in files)
        {
            _http.Serve(FileUrl(file), file.Content);
        }
    }

    private static string FileUrl(FakeFile file) => $"{Prefix}/{file.Source}";

    private string Local(string relative) => Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));

    private GameDownloader CreateDownloader(uint? advertisedSize = null) => new(
        _temp.CreateAppPaths(),
        new FakePatchServerClient(() => new LatestFileListInfo
        {
            LatestVersion = 900,
            ListFileUrl = ListUrl,
            ListFileSize = advertisedSize ?? (uint)_fileList.Length,
            ListFileCrc = KiCrc32.Compute(_fileList),
            UrlPrefix = Prefix,
        }),
        new HttpClient(_http),
        TimeProvider.System,
        NullLogger<GameDownloader>.Instance)
    {
        RetryDelay = TimeSpan.Zero,
    };

    private void PublishTypicalGame()
    {
        Publish(
            new FakeFile("Base", "Bin/WizardGraphicalClient.exe", ExeV2),
            new FakeFile("Base", "Bin/Game.dll", [9, 9, 9, 9]),
            new FakeFile("WizardCity", "Data/GameData/WizardCity-WC_Hub.wad", [1, 2, 3, 4, 5], FileType: PatchFileTypes.DynamicWad),
            new FakeFile("Krokotopia", "Data/GameData/Krokotopia-KT_Hub.wad", [7, 7, 7], Target: "Data/GameData/Krokotopia-KT_Hub.wad"));
    }

    [Fact]
    public async Task Full_plan_lists_missing_outdated_and_partial_files_but_not_correct_ones()
    {
        PublishTypicalGame();
        File.WriteAllBytes(Local("Bin/Game.dll"), [9, 9, 9, 9]); // already correct
        Directory.CreateDirectory(Local("Data/GameData"));
        File.WriteAllBytes(Local("Data/GameData/WizardCity-WC_Hub.wad"), new byte[5]); // zero-filled dynamic WAD

        var plan = await CreateDownloader().PlanAsync(_install, DownloadScope.FullGame);

        Assert.Equal(900u, plan.Revision);
        Assert.Equal(4, plan.CheckedFileCount);
        Assert.Equal(
            new[] { "Bin/WizardGraphicalClient.exe", "Data/GameData/WizardCity-WC_Hub.wad", "Data/GameData/Krokotopia-KT_Hub.wad" },
            plan.Files.Select(file => file.Record.TargetName).ToArray());
        Assert.Equal(6 + 5 + 3, plan.TotalBytes);
        Assert.Equal("wrong size", plan.Files[0].Reason); // the empty placeholder exe
        Assert.Equal("partly downloaded", plan.Files[1].Reason);
        Assert.Equal("missing", plan.Files[2].Reason);
        Assert.False(plan.IsUpToDate);
    }

    [Fact]
    public async Task Update_plan_covers_installed_packages_without_dynamic_wads()
    {
        PublishTypicalGame();
        Directory.CreateDirectory(Local("Data/GameData"));
        File.WriteAllBytes(Local("Data/GameData/WizardCity-WC_Hub.wad"), new byte[5]);

        var plan = await CreateDownloader().PlanAsync(_install, DownloadScope.Update);

        // Base is always installed; WizardCity is on disk but only has a dynamic WAD; Krokotopia isn't installed.
        Assert.Equal(new[] { "Bin/WizardGraphicalClient.exe", "Bin/Game.dll" }, plan.Files.Select(file => file.Record.TargetName).ToArray());

        File.WriteAllText(Path.Combine(_root, "LocalPackagesList.txt"), "Krokotopia\r\n");
        plan = await CreateDownloader().PlanAsync(_install, DownloadScope.Update);
        Assert.Contains("Data/GameData/Krokotopia-KT_Hub.wad", plan.Files.Select(file => file.Record.TargetName));
    }

    [Fact]
    public async Task Download_writes_verified_files_and_kingsisle_bookkeeping()
    {
        PublishTypicalGame();
        var downloader = CreateDownloader();
        var plan = await downloader.PlanAsync(_install, DownloadScope.FullGame);
        var reports = new List<DownloadProgress>();
        File.WriteAllBytes(Local("Bin/.Game.dll.0123.mwpart"), [1]); // left behind by a killed run

        var result = await downloader.DownloadAsync(plan, new SyncProgress<DownloadProgress>(reports.Add));

        Assert.True(result.Succeeded);
        Assert.Equal(4, result.DownloadedFiles);
        Assert.Equal(plan.TotalBytes, result.DownloadedBytes);
        foreach (var file in _files)
        {
            Assert.Equal(file.Content, File.ReadAllBytes(Local(file.Source)));
        }

        var last = reports[^1];
        Assert.Equal(plan.TotalBytes, last.BytesDone);
        Assert.Equal(4, last.FilesDone);
        Assert.All(_http.Requests, request => Assert.Equal("KingsIsle Patcher", request.UserAgent));
        Assert.Empty(Directory.GetFiles(_root, "*.mwpart", SearchOption.AllDirectories));

        Assert.Equal(_fileList, File.ReadAllBytes(Path.Combine(_root, "PatchInfo", "LatestFileList.bin")));
        Assert.Equal(2 * 24, new FileInfo(Path.Combine(_root, "PatchInfo", "CRC_Base.dat")).Length);
        Assert.True(File.Exists(Path.Combine(_root, "PatchInfo", "CRC_WizardCity.dat")));
        var listed = File.ReadAllLines(Path.Combine(_root, "LocalPackagesList.txt"));
        Assert.Equal(new[] { "WizardCity", "Krokotopia" }, listed);

        // Everything now matches; the second check is served from the verification cache.
        var again = await downloader.PlanAsync(_install, DownloadScope.FullGame);
        Assert.True(again.IsUpToDate);
    }

    [Fact]
    public async Task Update_never_marks_a_partly_downloaded_package_as_installed()
    {
        PublishTypicalGame();
        Directory.CreateDirectory(Local("Data/GameData"));
        File.WriteAllBytes(Local("Data/GameData/WizardCity-WC_Hub.wad"), new byte[5]);
        var downloader = CreateDownloader();

        var result = await downloader.DownloadAsync(await downloader.PlanAsync(_install, DownloadScope.Update));

        Assert.True(result.Succeeded);
        Assert.True(File.Exists(Path.Combine(_root, "PatchInfo", "CRC_Base.dat")));
        Assert.False(File.Exists(Path.Combine(_root, "PatchInfo", "CRC_WizardCity.dat")));
        Assert.False(File.Exists(Path.Combine(_root, "LocalPackagesList.txt")));
        Assert.Equal(new byte[5], File.ReadAllBytes(Local("Data/GameData/WizardCity-WC_Hub.wad")));
    }

    [Fact]
    public async Task Crc_mismatch_is_retried_and_the_good_copy_is_kept()
    {
        PublishTypicalGame();
        var exe = _files[0];
        _http.Serve(FileUrl(exe), attempt => FakeHttpHandler.Ok(attempt == 0 ? [0x4D, 0x5A, 6, 6, 6, 6] : exe.Content));
        var downloader = CreateDownloader();
        var plan = await downloader.PlanAsync(_install, DownloadScope.Update);

        var result = await downloader.DownloadAsync(plan);

        Assert.True(result.Succeeded);
        Assert.Equal(2, _http.Hits(FileUrl(exe)));
        Assert.Equal(exe.Content, File.ReadAllBytes(Local(exe.Source)));
    }

    [Fact]
    public async Task Persistent_crc_mismatch_fails_the_file_and_keeps_the_original()
    {
        PublishTypicalGame();
        var exe = _files[0];
        _http.Serve(FileUrl(exe), _ => FakeHttpHandler.Ok([0x4D, 0x5A, 6, 6, 6, 6]));
        var original = File.ReadAllBytes(Local(exe.Source));
        var downloader = CreateDownloader();
        var plan = await downloader.PlanAsync(_install, DownloadScope.Update);

        var result = await downloader.DownloadAsync(plan);

        Assert.False(result.Succeeded);
        var problem = Assert.Single(result.Failed);
        Assert.Equal("Bin/WizardGraphicalClient.exe", problem.RelativePath);
        Assert.Contains("CRC", problem.Reason);
        Assert.Equal(3, _http.Hits(FileUrl(exe)));
        Assert.Equal(original, File.ReadAllBytes(Local(exe.Source)));
        Assert.Equal(new byte[] { 9, 9, 9, 9 }, File.ReadAllBytes(Local("Bin/Game.dll")));
        Assert.Empty(Directory.GetFiles(_root, "*.mwpart", SearchOption.AllDirectories));
        Assert.False(Directory.Exists(Path.Combine(_root, "PatchInfo")), "bookkeeping is only written after a clean run");
    }

    [Fact]
    public async Task Short_downloads_and_http_errors_fail_after_three_attempts()
    {
        PublishTypicalGame();
        var exe = _files[0];
        var dll = _files[1];
        _http.Serve(FileUrl(exe), _ => FakeHttpHandler.Ok([0x4D, 0x5A]));
        _http.Serve(FileUrl(dll), _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var downloader = CreateDownloader();
        var plan = await downloader.PlanAsync(_install, DownloadScope.Update);

        var result = await downloader.DownloadAsync(plan);

        Assert.Equal(2, result.Failed.Count);
        Assert.Contains("503", result.Failed.Single(p => p.RelativePath == dll.Source).Reason);
        Assert.Equal(3, _http.Hits(FileUrl(dll)));
        Assert.Equal(0, result.DownloadedFiles);
    }

    [Fact]
    public async Task Cancelling_leaves_no_partial_files()
    {
        PublishTypicalGame();
        var exe = _files[0];
        var stalling = new StallingStream([0x4D, 0x5A, 2]);
        _http.Serve(FileUrl(exe), (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stalling) }));
        var original = File.ReadAllBytes(Local(exe.Source));
        var downloader = CreateDownloader();
        var plan = await downloader.PlanAsync(_install, DownloadScope.FullGame);
        using var cts = new CancellationTokenSource();

        var download = downloader.DownloadAsync(plan, null, cts.Token);
        await stalling.Stalled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download);
        Assert.Empty(Directory.GetFiles(_root, "*.mwpart", SearchOption.AllDirectories));
        Assert.Equal(original, File.ReadAllBytes(Local(exe.Source)));
        Assert.False(Directory.Exists(Path.Combine(_root, "PatchInfo")));
    }

    [Fact]
    public async Task Files_locked_by_a_running_client_are_skipped_and_reported()
    {
        PublishTypicalGame();
        var downloader = CreateDownloader();
        downloader.MoveFile = (source, destination) =>
        {
            if (destination.EndsWith("Game.dll", StringComparison.Ordinal))
            {
                throw new IOException("The process cannot access the file because it is being used by another process.", unchecked((int)0x80070020));
            }

            File.Move(source, destination, overwrite: true);
        };
        var plan = await downloader.PlanAsync(_install, DownloadScope.Update);

        var result = await downloader.DownloadAsync(plan);

        var locked = Assert.Single(result.InUse);
        Assert.Equal("Bin/Game.dll", locked.RelativePath);
        Assert.Empty(result.Failed);
        Assert.Equal(1, result.DownloadedFiles);
        Assert.False(File.Exists(Local("Bin/Game.dll")));
        Assert.Empty(Directory.GetFiles(_root, "*.mwpart", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Unsafe_paths_in_the_file_list_abort_before_anything_is_written()
    {
        Publish(
            new FakeFile("Base", "Bin/Game.dll", [1]),
            new FakeFile("Base", "evil.dll", [6, 6, 6], Target: "../evil.dll"));

        await Assert.ThrowsAnyAsync<InvalidDataException>(() => CreateDownloader().PlanAsync(_install, DownloadScope.FullGame));

        Assert.False(File.Exists(Path.Combine(_temp.Root, "evil.dll")));
        Assert.False(File.Exists(Local("Bin/Game.dll")));
        Assert.Empty(_http.Requests.Where(request => request.Url.EndsWith(".dll", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Incomplete_file_list_is_rejected()
    {
        PublishTypicalGame();

        var error = await Assert.ThrowsAnyAsync<PatchServerException>(() => CreateDownloader(advertisedSize: 12345).PlanAsync(_install, DownloadScope.FullGame));

        Assert.Contains("file list", error.Message);
        Assert.Equal(3, _http.Hits(ListUrl));
    }

    [Fact]
    public async Task Update_check_compares_the_base_package()
    {
        PublishTypicalGame();
        var downloader = CreateDownloader();

        var status = await downloader.CheckForUpdateAsync(_install);
        Assert.True(status.NeedsUpdate);
        Assert.Equal(900u, status.Revision);
        Assert.Equal(2, status.OutdatedFiles);

        await downloader.DownloadAsync(await downloader.PlanAsync(_install, DownloadScope.Update));
        Assert.False((await downloader.CheckForUpdateAsync(_install)).NeedsUpdate);
        Assert.Equal(900u, await downloader.GetLatestRevisionAsync(GameKind.Wizard101));
    }

    [Fact]
    public void Steam_pirate101_and_missing_installs_are_not_supported()
    {
        var downloader = CreateDownloader();

        Assert.True(downloader.GetSupport(_install).IsSupported);
        var steam = downloader.GetSupport(_install with { Source = InstallSource.Steam, SteamAppId = "799960" });
        Assert.False(steam.IsSupported);
        Assert.Contains("Steam", steam.Reason);
        Assert.False(downloader.GetSupport(_install with { Game = GameKind.Pirate101 }).IsSupported);
        Assert.False(downloader.GetSupport(_install with { RootPath = _temp.Combine("nope") }).IsSupported);
    }

    [Fact]
    public async Task Unsupported_installs_are_refused()
    {
        var downloader = CreateDownloader();

        await Assert.ThrowsAnyAsync<PatchServerException>(() => downloader.PlanAsync(_install with { Source = InstallSource.Steam, SteamAppId = "799960" }, DownloadScope.FullGame));
    }

    [Fact]
    public void Core_registers_the_downloader()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddMultiWizCore(_temp.CreateAppPaths());
        using var provider = services.BuildServiceProvider();

        var downloader = provider.GetRequiredService<IGameDownloader>();
        Assert.Same(provider.GetRequiredService<GameDownloader>(), downloader);
        Assert.IsType<PatchServerClient>(provider.GetRequiredService<IPatchServerClient>());
    }

    /// <summary>Invokes the callback synchronously (Progress&lt;T&gt; would post to the thread pool).</summary>
    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        private readonly object _gate = new();

        public void Report(T value)
        {
            lock (_gate)
            {
                report(value);
            }
        }
    }
}
