namespace MultiWiz.Core.Patching;

/// <summary>
/// The patch server's answer to "latest file list v2" (service 8, message 2): the current revision and where the
/// binary file list (LatestFileList.bin) and the game files can be downloaded.
/// </summary>
public sealed record LatestFileListInfo
{
    public required uint LatestVersion { get; init; }
    public string ListFileName { get; init; } = "";
    public uint ListFileType { get; init; }
    public uint ListFileTime { get; init; }
    public uint ListFileSize { get; init; }
    public uint ListFileCrc { get; init; }
    public required string ListFileUrl { get; init; }
    /// <summary>Base URL of the game files; a file's URL is this plus its <c>SrcFileName</c>.</summary>
    public required string UrlPrefix { get; init; }
    public string UrlSuffix { get; init; } = "";
    public string Locale { get; init; } = "";
}

/// <summary>A KingsIsle patch server (host and TCP port).</summary>
public sealed record PatchServerEndpoint(string Host, int Port)
{
    public override string ToString() => $"{Host}:{Port}";
}
