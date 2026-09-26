using MultiWiz.Core.Games;

namespace MultiWiz.Core.Patching;

/// <summary>Which files a plan covers.</summary>
public enum DownloadScope
{
    /// <summary>Every package, including the WADs the client would otherwise download while you play.</summary>
    FullGame = 0,

    /// <summary>
    /// Only packages already installed (Base, LocalPackagesList.txt, or files present on disk), without dynamic WADs:
    /// what the official patcher updates before the game starts.
    /// </summary>
    Update = 1,
}

/// <summary>Whether MultiWiz can download files for an install, and if not, why (for the user).</summary>
public sealed record GameDownloadSupport(bool IsSupported, string? Reason)
{
    public static GameDownloadSupport Supported { get; } = new(true, null);

    public static GameDownloadSupport NotSupported(string reason) => new(false, reason);
}

/// <summary>A file the plan will download.</summary>
public sealed record PlannedFile
{
    public required string Package { get; init; }
    public required PatchFileRecord Record { get; init; }
    /// <summary>Absolute destination path inside the install.</summary>
    public required string FullPath { get; init; }
    public required Uri Url { get; init; }
    /// <summary>Why it is needed: missing, wrong size or wrong CRC.</summary>
    public required string Reason { get; init; }

    public long Size => Record.Size;
}

/// <summary>The outcome of <see cref="IGameDownloader.PlanAsync"/>: what a download would fetch.</summary>
public sealed record DownloadPlan
{
    public required GameInstall Install { get; init; }
    public required DownloadScope Scope { get; init; }
    /// <summary>The patch server's current revision.</summary>
    public required uint Revision { get; init; }
    public required IReadOnlyList<PlannedFile> Files { get; init; }
    /// <summary>Files in scope that were checked (downloaded or already correct).</summary>
    public required int CheckedFileCount { get; init; }
    /// <summary>Free space on the install's drive when the plan was made; null when unknown.</summary>
    public long? AvailableFreeBytes { get; init; }

    internal LatestFileListInfo FileListInfo { get; init; } = null!;
    internal byte[] FileListBytes { get; init; } = [];
    internal IReadOnlyList<(PatchPackage Package, IReadOnlyList<PatchFileRecord> Files)> Packages { get; init; } = [];

    public int FileCount => Files.Count;

    public long TotalBytes => Files.Sum(file => file.Size);

    public bool IsUpToDate => Files.Count == 0;

    /// <summary>False when the drive is known to be too small (each file is written next to the old one, then swapped).</summary>
    public bool HasEnoughSpace => AvailableFreeBytes is not { } free || free >= TotalBytes;
}

/// <summary>Progress while checking files. Reported from background threads.</summary>
public readonly record struct PlanProgress(int FilesChecked, int FilesTotal);

/// <summary>Progress while downloading. Reported from background threads.</summary>
public readonly record struct DownloadProgress(long BytesDone, long BytesTotal, int FilesDone, int FilesTotal, string? CurrentFile);

/// <summary>A file that couldn't be downloaded, with a user-readable reason.</summary>
public sealed record FileProblem(string RelativePath, string Reason);

public sealed record DownloadResult
{
    public required uint Revision { get; init; }
    public required int DownloadedFiles { get; init; }
    public required long DownloadedBytes { get; init; }
    /// <summary>Files that failed after every retry (network, size or CRC problems).</summary>
    public required IReadOnlyList<FileProblem> Failed { get; init; }
    /// <summary>Files that were in use (usually by a running game) and were left alone.</summary>
    public required IReadOnlyList<FileProblem> InUse { get; init; }

    public bool Succeeded => Failed.Count == 0 && InUse.Count == 0;
}

/// <summary>Whether the game's core files (the Base package: client executable and libraries) match the server.</summary>
public sealed record GameUpdateStatus(uint Revision, int OutdatedFiles, long BytesToDownload)
{
    public bool NeedsUpdate => OutdatedFiles > 0;
}

/// <summary>
/// Downloads Wizard101's files from KingsIsle's patch server exactly as published (verified by KingsIsle's own size
/// and CRC), so zones don't have to be fetched while playing. Never modifies game code: files are written byte for byte.
/// Only one download runs at a time. Throws <see cref="PatchServerException"/> for server and file list problems,
/// <see cref="InvalidDataException"/> for an unsafe file list, and <see cref="OperationCanceledException"/> when
/// cancelled (no partial files are left behind).
/// </summary>
public interface IGameDownloader
{
    GameDownloadSupport GetSupport(GameInstall install);

    Task<uint> GetLatestRevisionAsync(GameKind game, CancellationToken cancellationToken = default);

    /// <summary>Checks the Base package only (fast after the first run thanks to the verification cache).</summary>
    Task<GameUpdateStatus> CheckForUpdateAsync(GameInstall install, CancellationToken cancellationToken = default);

    Task<DownloadPlan> PlanAsync(GameInstall install, DownloadScope scope, IProgress<PlanProgress>? progress = null, CancellationToken cancellationToken = default);

    Task<DownloadResult> DownloadAsync(DownloadPlan plan, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default);
}
