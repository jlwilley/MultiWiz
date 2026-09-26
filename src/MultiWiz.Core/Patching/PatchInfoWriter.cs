using System.Buffers.Binary;
using System.Text;

namespace MultiWiz.Core.Patching;

/// <summary>
/// Keeps KingsIsle's own bookkeeping in step after files were verified or downloaded, the way the official patcher
/// does: <c>PatchInfo\LatestFileList.bin</c> (the file list), <c>PatchInfo\CRC_&lt;package&gt;.dat</c> (24-byte records:
/// CRC of the source name, size, CRC, 0, Unix milliseconds) and <c>LocalPackagesList.txt</c> (the packages the client
/// treats as downloaded, one per line; "Base" is implied and never written).
/// </summary>
/// <remarks>
/// Ported from sigil-w101launcher's internal/patch (processTables, marshalCRCRecord, addPackage, writeAtomic),
/// Copyright (c) 2026 GhostNoodl, MIT License (see THIRD-PARTY-NOTICES.md).
/// </remarks>
internal static class PatchInfoWriter
{
    public const string PackagesListFileName = "LocalPackagesList.txt";
    public const string PatchInfoFolder = "PatchInfo";
    public const string FileListName = "LatestFileList.bin";

    public static IReadOnlySet<string> ReadInstalledPackages(string installRoot)
    {
        var path = Path.Combine(installRoot, PackagesListFileName);
        var packages = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            if (File.Exists(path))
            {
                foreach (var line in File.ReadLines(path))
                {
                    var name = line.Trim();
                    if (name.Length > 0)
                    {
                        packages.Add(name);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Treated as "nothing listed"; files found on disk still mark their packages as installed.
        }

        return packages;
    }

    /// <summary>Appends <paramref name="packages"/> that aren't listed yet, keeping the file's line endings.</summary>
    public static void AddInstalledPackages(string installRoot, IEnumerable<string> packages)
    {
        var path = Path.Combine(installRoot, PackagesListFileName);
        var existing = ReadInstalledPackages(installRoot);
        var missing = packages
            .Where(name => name != PatchPackage.BaseName && !existing.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var content = File.Exists(path) ? File.ReadAllText(path) : "";
        var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : content.Contains('\n') ? "\n" : "\r\n";
        var builder = new StringBuilder(content);
        if (content.Length > 0 && !content.EndsWith('\n'))
        {
            builder.Append(newline);
        }

        foreach (var name in missing)
        {
            builder.Append(name).Append(newline);
        }

        WriteAtomic(path, Encoding.ASCII.GetBytes(builder.ToString()));
    }

    public static void WriteFileList(string installRoot, byte[] fileList) =>
        WriteAtomic(Path.Combine(installRoot, PatchInfoFolder, FileListName), fileList);

    public static void WriteCrcFile(string installRoot, PatchPackage package, IEnumerable<PatchFileRecord> files, DateTimeOffset now)
    {
        var records = files.ToList();
        var buffer = new byte[records.Count * 24];
        var milliseconds = (ulong)now.ToUnixTimeMilliseconds();
        for (var i = 0; i < records.Count; i++)
        {
            var span = buffer.AsSpan(i * 24, 24);
            var source = NormalizeSourceName(records[i].SourceName);
            BinaryPrimitives.WriteUInt32LittleEndian(span, KiCrc32.Compute(Encoding.UTF8.GetBytes(source)));
            BinaryPrimitives.WriteUInt32LittleEndian(span[4..], records[i].Size);
            BinaryPrimitives.WriteUInt32LittleEndian(span[8..], records[i].Crc);
            BinaryPrimitives.WriteUInt32LittleEndian(span[12..], 0);
            BinaryPrimitives.WriteUInt64LittleEndian(span[16..], milliseconds);
        }

        WriteAtomic(Path.Combine(installRoot, PatchInfoFolder, $"CRC_{package.Name}.dat"), buffer);
    }

    /// <summary>Go's <c>path.Clean</c> for the simple relative names in the file list.</summary>
    internal static string NormalizeSourceName(string source)
    {
        var parts = new List<string>();
        foreach (var segment in source.Split('/'))
        {
            if (segment is "" or ".")
            {
                continue;
            }

            parts.Add(segment);
        }

        return string.Join('/', parts);
    }

    /// <summary>Writes through a temporary file in the same folder, then renames it over the target.</summary>
    internal static void WriteAtomic(string path, byte[] content)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    internal static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
