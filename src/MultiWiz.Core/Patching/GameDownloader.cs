using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;
using MultiWiz.Core.Games;
using MultiWiz.Core.Storage;

namespace MultiWiz.Core.Patching;

/// <summary>
/// <see cref="IGameDownloader"/> over KingsIsle's patch server and file CDN.
/// </summary>
/// <remarks>
/// Package selection, the dynamic-WAD rule, path checks and the PatchInfo bookkeeping follow sigil-w101launcher's
/// internal/patch (Copyright (c) 2026 GhostNoodl, MIT License; see THIRD-PARTY-NOTICES.md).
/// </remarks>
public sealed class GameDownloader : IGameDownloader
{
    /// <summary>The official CDN expects the patcher's user agent.</summary>
    public const string UserAgent = "KingsIsle Patcher";

    private const int Concurrency = 4;
    private const int MaxAttempts = 3;
    private const int BufferSize = 128 * 1024;
    private const int MaxFileListBytes = 256 * 1024 * 1024;
    private const string TemporaryExtension = ".mwpart";
    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(250);

    private readonly AppPaths _paths;
    private readonly IPatchServerClient _server;
    private readonly HttpClient _http;
    private readonly TimeProvider _time;
    private readonly ILogger<GameDownloader> _logger;
    private readonly SemaphoreSlim _downloadGate = new(1, 1);

    public GameDownloader(AppPaths paths, IPatchServerClient server, HttpClient http, TimeProvider time, ILogger<GameDownloader> logger)
    {
        _paths = paths;
        _server = server;
        _http = http;
        _time = time;
        _logger = logger;
    }

    /// <summary>A download (or the response headers) that makes no progress for this long is retried.</summary>
    public TimeSpan StallTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Delay before retry n (1-based) is n times this.</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Moves a verified temporary file over the destination. Replaceable in tests to simulate a locked file.</summary>
    internal Action<string, string> MoveFile { get; set; } = static (source, destination) => File.Move(source, destination, overwrite: true);

    /// <summary>The shared <see cref="HttpClient"/> used in the app: pooled connections, system proxy, no overall timeout.</summary>
    public static HttpClient CreateDefaultHttpClient() => new(new SocketsHttpHandler
    {
        MaxConnectionsPerServer = Concurrency * 2,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectTimeout = TimeSpan.FromSeconds(30),
        AutomaticDecompression = DecompressionMethods.None,
    })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    public GameDownloadSupport GetSupport(GameInstall install)
    {
        ArgumentNullException.ThrowIfNull(install);
        if (install.Source == InstallSource.Steam || !string.IsNullOrEmpty(install.SteamAppId))
        {
            return GameDownloadSupport.NotSupported("Steam installs already include every game file, and Steam keeps them up to date.");
        }

        if (PatchServers.For(install.Game) is null)
        {
            return GameDownloadSupport.NotSupported($"Downloading {install.Game} files isn't supported yet: its patch server hasn't been verified.");
        }

        if (install.RootPath.Contains("gameforge", StringComparison.OrdinalIgnoreCase))
        {
            return GameDownloadSupport.NotSupported("Only the North American (KingsIsle) version is supported. European installs use a different patch server.");
        }

        if (!Directory.Exists(install.RootPath))
        {
            return GameDownloadSupport.NotSupported("The game folder doesn't exist.");
        }

        return GameDownloadSupport.Supported;
    }

    public async Task<uint> GetLatestRevisionAsync(GameKind game, CancellationToken cancellationToken = default)
    {
        var endpoint = PatchServers.For(game)
            ?? throw new PatchServerException($"MultiWiz doesn't know {game}'s patch server.");
        var info = await _server.GetLatestFileListAsync(endpoint, cancellationToken).ConfigureAwait(false);
        return info.LatestVersion;
    }

    public async Task<GameUpdateStatus> CheckForUpdateAsync(GameInstall install, CancellationToken cancellationToken = default)
    {
        var plan = await PlanCoreAsync(install, DownloadScope.Update, baseOnly: true, progress: null, cancellationToken).ConfigureAwait(false);
        return new GameUpdateStatus(plan.Revision, plan.FileCount, plan.TotalBytes);
    }

    public Task<DownloadPlan> PlanAsync(GameInstall install, DownloadScope scope, IProgress<PlanProgress>? progress = null, CancellationToken cancellationToken = default) =>
        PlanCoreAsync(install, scope, baseOnly: false, progress, cancellationToken);

    private async Task<DownloadPlan> PlanCoreAsync(GameInstall install, DownloadScope scope, bool baseOnly, IProgress<PlanProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(install);
        var support = GetSupport(install);
        if (!support.IsSupported)
        {
            throw new PatchServerException(support.Reason!);
        }

        var root = install.RootPath;
        var info = await _server.GetLatestFileListAsync(PatchServers.For(install.Game)!, cancellationToken).ConfigureAwait(false);
        var fileListBytes = await DownloadFileListAsync(info, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<PatchPackage> packages;
        try
        {
            packages = FileListParser.ParsePackages(fileListBytes);
        }
        catch (InvalidDataException ex)
        {
            throw new PatchServerException("KingsIsle's file list couldn't be read: " + ex.Message, ex);
        }

        if (!packages.Any(package => package.IsBase))
        {
            throw new PatchServerException("KingsIsle's file list has no Base package; it may be incomplete. Try again later.");
        }

        // Resolve every path first so an unsafe file list is rejected before anything is read or written.
        var installed = PatchInfoWriter.ReadInstalledPackages(root);
        var selected = new List<(PatchPackage Package, IReadOnlyList<PatchFileRecord> Files)>();
        var candidates = new List<(string Package, PatchFileRecord Record, string FullPath, Uri Url)>();
        foreach (var package in packages)
        {
            var resolved = package.Files
                .Select(file => (file, fullPath: PatchPaths.ResolveTarget(root, file.TargetName)))
                .ToList();
            var include = baseOnly
                ? package.IsBase
                : scope == DownloadScope.FullGame || package.IsBase || installed.Contains(package.Name) || resolved.Any(item => File.Exists(item.fullPath));
            if (!include)
            {
                continue;
            }

            var files = new List<PatchFileRecord>();
            foreach (var (file, fullPath) in resolved)
            {
                if (file.IsDynamicWad && scope != DownloadScope.FullGame)
                {
                    continue;
                }

                files.Add(file);
                candidates.Add((package.Name, file, fullPath, PatchPaths.BuildFileUrl(info.UrlPrefix, file.SourceName)));
            }

            selected.Add((package, files));
        }

        var cache = PatchVerifyCache.Load(PatchVerifyCache.PathFor(_paths, install.Id));
        var needed = new ConcurrentBag<(int Index, PlannedFile File)>();
        var checkedCount = 0;
        var lastReport = _time.GetTimestamp();
        progress?.Report(new PlanProgress(0, candidates.Count));
        try
        {
            await Parallel.ForEachAsync(
                Enumerable.Range(0, candidates.Count),
                new ParallelOptions { MaxDegreeOfParallelism = Concurrency, CancellationToken = cancellationToken },
                async (index, token) =>
                {
                    var (package, record, fullPath, url) = candidates[index];
                    var reason = await CheckFileAsync(cache, record, fullPath, token).ConfigureAwait(false);
                    if (reason is not null)
                    {
                        needed.Add((index, new PlannedFile { Package = package, Record = record, FullPath = fullPath, Url = url, Reason = reason }));
                    }

                    var done = Interlocked.Increment(ref checkedCount);
                    if (progress is not null && ShouldReport(ref lastReport))
                    {
                        progress.Report(new PlanProgress(done, candidates.Count));
                    }
                }).ConfigureAwait(false);
        }
        finally
        {
            cache.Save();
        }

        progress?.Report(new PlanProgress(candidates.Count, candidates.Count));
        var plan = new DownloadPlan
        {
            Install = install,
            Scope = scope,
            Revision = info.LatestVersion,
            Files = needed.OrderBy(item => item.Index).Select(item => item.File).ToList(),
            CheckedFileCount = candidates.Count,
            AvailableFreeBytes = TryGetFreeSpace(root),
            FileListInfo = info,
            FileListBytes = fileListBytes,
            Packages = selected,
        };
        _logger.LogInformation(
            "Planned {Scope} for {Install} at revision {Revision}: {Count} of {Checked} files, {Bytes} bytes",
            scope, install.Id, plan.Revision, plan.FileCount, plan.CheckedFileCount, plan.TotalBytes);
        return plan;
    }

    public async Task<DownloadResult> DownloadAsync(DownloadPlan plan, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!await _downloadGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("A game download is already running.");
        }

        try
        {
            return await DownloadCoreAsync(plan, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _downloadGate.Release();
        }
    }

    private async Task<DownloadResult> DownloadCoreAsync(DownloadPlan plan, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        var root = plan.Install.RootPath;
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"The game folder {root} no longer exists.");
        }

        RemoveStaleTemporaryFiles(plan);
        var cache = PatchVerifyCache.Load(PatchVerifyCache.PathFor(_paths, plan.Install.Id));
        var failed = new ConcurrentBag<FileProblem>();
        var inUse = new ConcurrentBag<FileProblem>();
        var counters = new ProgressCounters(plan.TotalBytes, plan.FileCount);
        var lastReport = _time.GetTimestamp();
        void Report(string? current, bool force)
        {
            if (progress is not null && (force || ShouldReport(ref lastReport)))
            {
                progress.Report(new DownloadProgress(
                    Interlocked.Read(ref counters.BytesDone), counters.BytesTotal, Volatile.Read(ref counters.FilesDone), counters.FilesTotal, current));
            }
        }

        _logger.LogInformation("Downloading {Count} files ({Bytes} bytes) into {Root}", plan.FileCount, plan.TotalBytes, root);
        Report(null, force: true);
        try
        {
            await Parallel.ForEachAsync(
                plan.Files,
                new ParallelOptions { MaxDegreeOfParallelism = Concurrency, CancellationToken = cancellationToken },
                async (file, token) =>
                {
                    var outcome = await DownloadWithRetriesAsync(file, counters, current => Report(current, force: false), token).ConfigureAwait(false);
                    var relative = file.Record.TargetName;
                    var key = PatchPaths.CacheKey(relative);
                    switch (outcome.Kind)
                    {
                        case OutcomeKind.Downloaded:
                            cache.Remember(key, file.FullPath, file.Record.Size, file.Record.Crc);
                            Interlocked.Increment(ref counters.FilesDone);
                            Interlocked.Add(ref counters.BytesWritten, file.Size);
                            break;
                        case OutcomeKind.InUse:
                            inUse.Add(new FileProblem(relative, outcome.Message!));
                            break;
                        default:
                            cache.Forget(key);
                            failed.Add(new FileProblem(relative, outcome.Message!));
                            break;
                    }

                    Report(relative, force: false);
                }).ConfigureAwait(false);
        }
        finally
        {
            cache.Save();
        }

        Report(null, force: true);
        var result = new DownloadResult
        {
            Revision = plan.Revision,
            DownloadedFiles = counters.FilesDone,
            DownloadedBytes = Interlocked.Read(ref counters.BytesWritten),
            Failed = failed.OrderBy(problem => problem.RelativePath, StringComparer.OrdinalIgnoreCase).ToList(),
            InUse = inUse.OrderBy(problem => problem.RelativePath, StringComparer.OrdinalIgnoreCase).ToList(),
        };

        if (result.Succeeded)
        {
            UpdatePatchInfo(plan);
        }

        _logger.LogInformation(
            "Download finished: {Downloaded} files, {Failed} failed, {InUse} in use",
            result.DownloadedFiles, result.Failed.Count, result.InUse.Count);
        return result;
    }

    /// <summary>Records the verified packages the way the official patcher does, so the client treats them as downloaded.</summary>
    private void UpdatePatchInfo(DownloadPlan plan)
    {
        var root = plan.Install.RootPath;
        try
        {
            // Only packages whose every file was verified: an update skips dynamic WADs, and a package whose WAD is
            // still being filled in on demand must not be marked as downloaded.
            var now = _time.GetUtcNow();
            var complete = plan.Packages.Where(item => item.Files.Count == item.Package.Files.Count).ToList();
            foreach (var (package, files) in complete)
            {
                PatchInfoWriter.WriteCrcFile(root, package, files, now);
            }

            PatchInfoWriter.AddInstalledPackages(root, complete.Select(item => item.Package.Name));
            if (plan.FileListBytes.Length > 0)
            {
                PatchInfoWriter.WriteFileList(root, plan.FileListBytes);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The files themselves are correct; the official patcher rebuilds its bookkeeping on its next run.
            _logger.LogWarning(ex, "Could not update PatchInfo in {Root}", root);
        }
    }

    private async Task<byte[]> DownloadFileListAsync(LatestFileListInfo info, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(info.ListFileUrl, UriKind.Absolute, out var url) || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
        {
            throw new PatchServerException("The patch server sent an invalid file list address.");
        }

        Exception? lastError = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            if (attempt > 1)
            {
                await Task.Delay(RetryDelay * (attempt - 1), _time, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                var bytes = await GetBytesAsync(url, cancellationToken).ConfigureAwait(false);
                if (info.ListFileSize != 0 && bytes.Length != info.ListFileSize)
                {
                    lastError = new PatchServerException($"The file list was {bytes.Length} bytes instead of {info.ListFileSize}.");
                    continue;
                }

                if (info.ListFileCrc != 0 && KiCrc32.Compute(bytes) != info.ListFileCrc)
                {
                    // Only the size is enforced: the server's CRC for the list itself is not independently confirmed to
                    // use the same convention as the per-file CRCs, which are always enforced.
                    _logger.LogWarning("File list CRC {Actual:X8} differs from the advertised {Expected:X8}", KiCrc32.Compute(bytes), info.ListFileCrc);
                }

                return bytes;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                lastError = ex;
                _logger.LogWarning(ex, "File list download attempt {Attempt} failed", attempt);
            }
        }

        throw new PatchServerException("Couldn't download KingsIsle's file list. Check your internet connection and try again.", lastError!);
    }

    private async Task<byte[]> GetBytesAsync(Uri url, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(StallTimeout * 2);
        using var request = CreateRequest(url);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}", null, response.StatusCode);
        }

        if (response.Content.Headers.ContentLength > MaxFileListBytes)
        {
            throw new IOException("The file list is unexpectedly large.");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
        return bytes.Length > MaxFileListBytes ? throw new IOException("The file list is unexpectedly large.") : bytes;
    }

    private static HttpRequestMessage CreateRequest(Uri url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        return request;
    }

    /// <summary>Returns null when the file is present with the expected size and CRC, else why it must be downloaded.</summary>
    private static async Task<string?> CheckFileAsync(PatchVerifyCache cache, PatchFileRecord record, string fullPath, CancellationToken cancellationToken)
    {
        var info = new FileInfo(fullPath);
        if (!info.Exists)
        {
            return Directory.Exists(fullPath) ? "a folder is in the way" : "missing";
        }

        if (info.Length != record.Size)
        {
            return "wrong size";
        }

        var key = PatchPaths.CacheKey(record.TargetName);
        if (cache.Matches(key, info, record.Size, record.Crc))
        {
            return null;
        }

        try
        {
            var crc = await ComputeFileCrcAsync(fullPath, cancellationToken).ConfigureAwait(false);
            if (crc != record.Crc)
            {
                cache.Forget(key);
                return record.IsDynamicWad ? "partly downloaded" : "outdated or damaged";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "unreadable";
        }

        cache.Remember(key, fullPath, record.Size, record.Crc);
        return null;
    }

    internal static async Task<uint> ComputeFileCrcAsync(string path, CancellationToken cancellationToken)
    {
        var crc = new KiCrc32();
        var buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.Asynchronous | FileOptions.SequentialScan);
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                crc.Append(buffer.AsSpan(0, read));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return crc.GetCurrentHash();
    }

    private async Task<Outcome> DownloadWithRetriesAsync(PlannedFile file, ProgressCounters counters, Action<string> reportProgress, CancellationToken cancellationToken)
    {
        string? lastError = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            if (attempt > 1)
            {
                await Task.Delay(RetryDelay * (attempt - 1), _time, cancellationToken).ConfigureAwait(false);
            }

            long attemptBytes = 0;
            try
            {
                return await DownloadOnceAsync(file, bytes =>
                {
                    attemptBytes += bytes;
                    Interlocked.Add(ref counters.BytesDone, bytes);
                    reportProgress(file.Record.TargetName);
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (DownloadAttemptException ex)
            {
                lastError = ex.Message;
            }
            catch (HttpRequestException ex)
            {
                lastError = ex.StatusCode is { } status ? $"the server answered {(int)status}" : "the connection failed";
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastError = "the download stalled";
            }
            catch (IOException ex)
            {
                lastError = ex.Message;
            }

            // The failed attempt's bytes no longer count towards progress.
            Interlocked.Add(ref counters.BytesDone, -attemptBytes);
            _logger.LogWarning("Download of {File} failed (attempt {Attempt}/{Max}): {Error}", file.Record.TargetName, attempt, MaxAttempts, lastError);
        }

        return new Outcome(OutcomeKind.Failed, lastError);
    }

    private async Task<Outcome> DownloadOnceAsync(PlannedFile file, Action<int> onBytes, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(file.FullPath)!;
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, $".{Path.GetFileName(file.FullPath)}.{Guid.NewGuid():N}{TemporaryExtension}");
        var moved = false;
        try
        {
            using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            stall.CancelAfter(StallTimeout);
            using var request = CreateRequest(file.Url);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stall.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"HTTP {(int)response.StatusCode}", null, response.StatusCode);
            }

            var expected = (long)file.Record.Size;
            if (response.Content.Headers.ContentLength is { } length && length != expected)
            {
                throw new DownloadAttemptException($"the server offered {length} bytes instead of {expected}");
            }

            var crc = new KiCrc32();
            var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                await using var body = await response.Content.ReadAsStreamAsync(stall.Token).ConfigureAwait(false);
                await using var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.Asynchronous);
                if (expected > 0)
                {
                    output.SetLength(expected);
                }

                long total = 0;
                while (true)
                {
                    stall.CancelAfter(StallTimeout);
                    var read = await body.ReadAsync(buffer.AsMemory(0, BufferSize), stall.Token).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    total += read;
                    if (total > expected)
                    {
                        throw new DownloadAttemptException("the server sent more data than expected");
                    }

                    crc.Append(buffer.AsSpan(0, read));
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    onBytes(read);
                }

                if (total != expected)
                {
                    throw new DownloadAttemptException($"the download ended after {total} of {expected} bytes");
                }

                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            var actual = crc.GetCurrentHash();
            if (actual != file.Record.Crc)
            {
                throw new DownloadAttemptException($"the CRC didn't match (got {actual:X8}, expected {file.Record.Crc:X8})");
            }

            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                ClearReadOnly(file.FullPath);
                MoveFile(temp, file.FullPath);
                moved = true;
            }
            catch (IOException ex) when (IsLockViolation(ex))
            {
                return new Outcome(OutcomeKind.InUse, "the file is in use (close the game and try again)");
            }
            catch (UnauthorizedAccessException)
            {
                return new Outcome(OutcomeKind.Failed, "access to the file was denied");
            }

            return new Outcome(OutcomeKind.Downloaded, null);
        }
        finally
        {
            if (!moved)
            {
                PatchInfoWriter.TryDelete(temp);
            }
        }
    }

    /// <summary>Deletes temporary files an earlier run left behind if MultiWiz was killed mid-download.</summary>
    private void RemoveStaleTemporaryFiles(DownloadPlan plan)
    {
        foreach (var directory in plan.Files.Select(file => Path.GetDirectoryName(file.FullPath)!).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!Directory.Exists(directory))
                {
                    continue;
                }

                foreach (var stale in Directory.EnumerateFiles(directory, "." + "*" + TemporaryExtension))
                {
                    _logger.LogInformation("Removing leftover partial download {File}", stale);
                    PatchInfoWriter.TryDelete(stale);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort.
            }
        }
    }

    private static void ClearReadOnly(string path)
    {
        var info = new FileInfo(path);
        if (info.Exists && info.IsReadOnly)
        {
            info.IsReadOnly = false;
        }
    }

    private static bool IsLockViolation(IOException ex) => (ex.HResult & 0xFFFF) is ErrorSharingViolation or ErrorLockViolation;

    private bool ShouldReport(ref long lastReport)
    {
        var last = Interlocked.Read(ref lastReport);
        var now = _time.GetTimestamp();
        return _time.GetElapsedTime(last, now) >= ProgressInterval
            && Interlocked.CompareExchange(ref lastReport, now, last) == last;
    }

    private static long? TryGetFreeSpace(string root)
    {
        try
        {
            var drive = Path.GetPathRoot(Path.GetFullPath(root));
            return string.IsNullOrEmpty(drive) ? null : new DriveInfo(drive).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private enum OutcomeKind
    {
        Downloaded,
        InUse,
        Failed,
    }

    private readonly record struct Outcome(OutcomeKind Kind, string? Message);

    private sealed class ProgressCounters(long bytesTotal, int filesTotal)
    {
        public long BytesDone;
        public long BytesWritten;
        public int FilesDone;

        public long BytesTotal { get; } = bytesTotal;

        public int FilesTotal { get; } = filesTotal;
    }

    private sealed class DownloadAttemptException(string message) : Exception(message);
}
