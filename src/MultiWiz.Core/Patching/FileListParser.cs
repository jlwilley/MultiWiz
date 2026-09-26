namespace MultiWiz.Core.Patching;

/// <summary>One file of a package as listed in LatestFileList.bin.</summary>
public sealed record PatchFileRecord
{
    /// <summary>Path on the download server, relative to <see cref="LatestFileListInfo.UrlPrefix"/> (forward slashes).</summary>
    public required string SourceName { get; init; }

    /// <summary>Path relative to the install root (<c>TarFileName</c>, or <see cref="SourceName"/> when that is empty).</summary>
    public required string TargetName { get; init; }

    public uint FileType { get; init; }
    public uint Size { get; init; }
    public uint HeaderSize { get; init; }
    public uint CompressedHeaderSize { get; init; }
    /// <summary>CRC of the whole file in KingsIsle's convention (see <see cref="KiCrc32"/>).</summary>
    public uint Crc { get; init; }
    public uint HeaderCrc { get; init; }

    /// <summary>
    /// A WAD the client builds on demand ("download as you play"): it starts as a zero-filled archive and its CRC only
    /// matches once every entry has been fetched. A full download replaces it with the complete file.
    /// </summary>
    public bool IsDynamicWad => FileType == PatchFileTypes.DynamicWad;
}

public static class PatchFileTypes
{
    public const uint DynamicWad = 5;
}

/// <summary>A package (DML table) of the file list, e.g. "Base" or a zone's WADs.</summary>
public sealed record PatchPackage(string Name, IReadOnlyList<PatchFileRecord> Files)
{
    /// <summary>Always installed and always verified; never written to LocalPackagesList.txt.</summary>
    public const string BaseName = "Base";

    public bool IsBase => string.Equals(Name, BaseName, StringComparison.Ordinal);
}

/// <summary>A generic DML table: its name and records (field name → value).</summary>
public sealed record DmlTable(string Name, IReadOnlyList<IReadOnlyDictionary<string, object>> Records);

/// <summary>
/// Reads KingsIsle's binary DML tables (the format of LatestFileList.bin).
/// </summary>
/// <remarks>
/// The file is a sequence of tables. Each table is: record count u32; a template header (1 ignored byte, then 1 = the
/// record template); the template (u16 size, then fields of u16 name length, name, u8 type, 1 ignored byte, ending at
/// the field named <c>_TargetTable</c>, which is followed by the table name as a u16-length string); then for every
/// record a header (1 ignored byte, then 2 = record), a u16 size, and the field values in template order. Value sizes
/// by type code: 0 and 7 are 8 bytes; 1, 2 and 3 are 4 bytes (the file list uses 3 for its u32 columns); 4 and 5 one
/// byte; 6 two bytes; 8 and 9 are u16-length strings. Behaviour matches the decoder in w101-client-go that sigil and
/// umbra use; this is an independent implementation.
/// </remarks>
public static class FileListParser
{
    /// <summary>Tables that describe the list itself rather than downloadable packages.</summary>
    public static IReadOnlySet<string> MetaTables { get; } = new HashSet<string>(StringComparer.Ordinal) { "_TableList", "About", "PatchClient" };

    private const string TargetTableField = "_TargetTable";

    /// <summary>Parses every table. Throws <see cref="InvalidDataException"/> for malformed data.</summary>
    public static IReadOnlyList<DmlTable> ParseTables(ReadOnlySpan<byte> data)
    {
        var reader = new PatchProtocol.SpanReader(data);
        var tables = new List<DmlTable>();
        while (reader.Remaining > 0)
        {
            var recordCount = reader.ReadUInt32();
            ExpectHeader(ref reader, 1, "record template");
            reader.Skip(2); // template size

            var fields = new List<(string Name, byte Type)>();
            string tableName;
            while (true)
            {
                var name = reader.ReadString();
                var type = reader.ReadByte();
                reader.Skip(1);
                if (name == TargetTableField)
                {
                    tableName = reader.ReadString();
                    break;
                }

                if (fields.Count > 256)
                {
                    throw new InvalidDataException("Too many fields in a file list table.");
                }

                fields.Add((name, type));
            }

            // Every record takes at least 4 bytes, so a larger count means the data is corrupt.
            if (recordCount > (uint)reader.Remaining / 4)
            {
                throw new InvalidDataException($"Table {tableName} claims {recordCount} records but the data is too short.");
            }

            var records = new List<IReadOnlyDictionary<string, object>>((int)recordCount);
            for (var i = 0u; i < recordCount; i++)
            {
                ExpectHeader(ref reader, 2, "record");
                reader.Skip(2); // record size
                var record = new Dictionary<string, object>(fields.Count, StringComparer.Ordinal);
                foreach (var (name, type) in fields)
                {
                    record[name] = type switch
                    {
                        0 or 7 => reader.ReadUInt64(),
                        1 or 2 or 3 => reader.ReadUInt32(),
                        4 or 5 => reader.ReadByte(),
                        6 => reader.ReadUInt16(),
                        8 or 9 => reader.ReadString(),
                        _ => throw new InvalidDataException($"Unknown field type {type} in table {tableName}."),
                    };
                }

                records.Add(record);
            }

            tables.Add(new DmlTable(tableName, records));
        }

        return tables;
    }

    /// <summary>
    /// Parses the file list and returns its packages (meta tables excluded). Throws <see cref="InvalidDataException"/>
    /// for malformed data or unsafe package names.
    /// </summary>
    public static IReadOnlyList<PatchPackage> ParsePackages(ReadOnlySpan<byte> data)
    {
        var packages = new List<PatchPackage>();
        foreach (var table in ParseTables(data))
        {
            if (MetaTables.Contains(table.Name))
            {
                continue;
            }

            if (!IsSafePackageName(table.Name))
            {
                throw new InvalidDataException($"The file list has an unsafe package name: \"{table.Name}\".");
            }

            var files = new List<PatchFileRecord>(table.Records.Count);
            foreach (var record in table.Records)
            {
                var source = GetString(record, "SrcFileName");
                if (string.IsNullOrEmpty(source))
                {
                    throw new InvalidDataException($"A file in package {table.Name} has no name.");
                }

                var target = GetString(record, "TarFileName");
                files.Add(new PatchFileRecord
                {
                    SourceName = source,
                    TargetName = string.IsNullOrEmpty(target) ? source : target,
                    FileType = GetUInt32(record, "FileType"),
                    Size = GetUInt32(record, "Size"),
                    HeaderSize = GetUInt32(record, "HeaderSize"),
                    CompressedHeaderSize = GetUInt32(record, "CompressedHeaderSize"),
                    Crc = GetUInt32(record, "CRC"),
                    HeaderCrc = GetUInt32(record, "HeaderCRC"),
                });
            }

            packages.Add(new PatchPackage(table.Name, files));
        }

        return packages;
    }

    /// <summary>Package names become file names (PatchInfo\CRC_&lt;name&gt;.dat), so they must be plain names.</summary>
    public static bool IsSafePackageName(string name) =>
        !string.IsNullOrEmpty(name) && name != "." && name != ".."
        && name.IndexOfAny(['/', '\\', ':', '\0', '\r', '\n']) < 0
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private static void ExpectHeader(ref PatchProtocol.SpanReader reader, byte expected, string what)
    {
        reader.Skip(1);
        var kind = reader.ReadByte();
        if (kind != expected)
        {
            throw new InvalidDataException($"Expected a {what} at offset {reader.Position - 1} of the file list, found type {kind}.");
        }
    }

    private static string GetString(IReadOnlyDictionary<string, object> record, string field) =>
        record.TryGetValue(field, out var value) && value is string text ? text : "";

    private static uint GetUInt32(IReadOnlyDictionary<string, object> record, string field) =>
        record.TryGetValue(field, out var value)
            ? value switch
            {
                uint number => number,
                ushort number => number,
                byte number => number,
                ulong number => (uint)number,
                _ => 0,
            }
            : 0;
}
