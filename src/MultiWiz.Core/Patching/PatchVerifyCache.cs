using System.Text.Json;
using System.Text.Json.Serialization;
using MultiWiz.Core.Storage;

namespace MultiWiz.Core.Patching;

/// <summary>
/// Remembers files that already passed a CRC check (by size, expected CRC and last-write time) so later checks only
/// need a metadata lookup. Any change to the file or to the expected CRC falls back to hashing it again.
/// Thread-safe. Stored in <c>%LocalAppData%\MultiWiz\state\patch-cache-&lt;installId&gt;.json</c>.
/// </summary>
internal sealed class PatchVerifyCache
{
    private readonly object _gate = new();
    private readonly string _path;
    private readonly Dictionary<string, PatchCacheEntry> _files;
    private bool _dirty;

    private PatchVerifyCache(string path, Dictionary<string, PatchCacheEntry> files)
    {
        _path = path;
        _files = files;
    }

    public static string PathFor(AppPaths paths, string installId)
    {
        var safeId = string.Concat(installId.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return Path.Combine(paths.StateDirectory, $"patch-cache-{safeId}.json");
    }

    /// <summary>Loads the cache; a missing or unreadable file gives an empty cache (it is only an optimization).</summary>
    public static PatchVerifyCache Load(string path)
    {
        Dictionary<string, PatchCacheEntry>? files = null;
        try
        {
            if (File.Exists(path))
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                files = JsonSerializer.Deserialize(stream, PatchingJsonContext.Default.PatchCacheDocument)?.Files;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            files = null;
        }

        return new PatchVerifyCache(path, new Dictionary<string, PatchCacheEntry>(files ?? [], StringComparer.Ordinal));
    }

    public bool Matches(string cacheKey, FileInfo file, uint size, uint crc)
    {
        lock (_gate)
        {
            return _files.TryGetValue(cacheKey, out var entry)
                && entry.Size == size && entry.Crc == crc
                && file.Length == size && entry.LastWriteTicks == file.LastWriteTimeUtc.Ticks;
        }
    }

    public void Remember(string cacheKey, string fullPath, uint size, uint crc)
    {
        var info = new FileInfo(fullPath);
        if (!info.Exists)
        {
            return;
        }

        lock (_gate)
        {
            _files[cacheKey] = new PatchCacheEntry { Size = size, Crc = crc, LastWriteTicks = info.LastWriteTimeUtc.Ticks };
            _dirty = true;
        }
    }

    public void Forget(string cacheKey)
    {
        lock (_gate)
        {
            _dirty |= _files.Remove(cacheKey);
        }
    }

    /// <summary>Writes the cache if it changed. Failures are ignored (the next check just hashes again).</summary>
    public void Save()
    {
        PatchCacheDocument document;
        lock (_gate)
        {
            if (!_dirty)
            {
                return;
            }

            document = new PatchCacheDocument { Files = new Dictionary<string, PatchCacheEntry>(_files, StringComparer.Ordinal) };
            _dirty = false;
        }

        try
        {
            JsonFileStore.Save(_path, document, PatchingJsonContext.Default.PatchCacheDocument);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            lock (_gate)
            {
                _dirty = true;
            }
        }
    }
}

internal sealed class PatchCacheDocument
{
    public int SchemaVersion { get; set; } = 1;

    public Dictionary<string, PatchCacheEntry> Files { get; set; } = [];
}

internal sealed record PatchCacheEntry
{
    public uint Size { get; init; }
    public uint Crc { get; init; }
    public long LastWriteTicks { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PatchCacheDocument))]
internal sealed partial class PatchingJsonContext : JsonSerializerContext
{
}
