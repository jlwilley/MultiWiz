using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;

namespace MultiWiz.Core.Storage;

/// <summary>Reads and atomically writes JSON documents using source-generated metadata.</summary>
public static class JsonFileStore
{
    private const int ReplaceAttempts = 5;
    private static readonly TimeSpan ReplaceRetryDelay = TimeSpan.FromMilliseconds(25);

    /// <summary>
    /// Reads <paramref name="path"/>. Returns null when the file does not exist or contains JSON <c>null</c>.
    /// A file that is not valid JSON for <typeparamref name="T"/> is renamed to
    /// <c>&lt;name&gt;.corrupt-&lt;yyyyMMddHHmmss&gt;</c> (local time) so it is kept for inspection, null is returned, and
    /// <paramref name="onQuarantined"/> receives the new path so the user can be told where the old data went.
    /// I/O errors are not swallowed: treating an unreadable file as empty would let the next save overwrite it.
    /// </summary>
    /// <param name="defaults">
    /// Values for the properties the file lacks (see <see cref="JsonDefaults"/>); without it such properties get
    /// <c>default(T)</c> when the model uses init-only properties.
    /// </param>
    public static T? Load<T>(
        string path,
        JsonTypeInfo<T> typeInfo,
        ILogger? logger = null,
        TimeProvider? timeProvider = null,
        JsonDefaults? defaults = null,
        Action<string>? onQuarantined = null)
        where T : class
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(typeInfo);

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (defaults is null)
            {
                return JsonSerializer.Deserialize(stream, typeInfo);
            }

            using var document = JsonDocument.Parse(stream, DocumentOptionsFor(typeInfo.Options));
            return defaults.Deserialize(document.RootElement, typeInfo);
        }
        catch (JsonException ex)
        {
            if (Quarantine(path, ex, logger, timeProvider ?? TimeProvider.System) is { } target)
            {
                onQuarantined?.Invoke(target);
            }

            return null;
        }
    }

    /// <summary>
    /// Writes <paramref name="value"/> to <c>&lt;path&gt;.tmp</c>, flushes it to disk, then moves it over
    /// <paramref name="path"/>, so a crash never leaves a half-written file behind. Creates the directory if needed.
    /// </summary>
    public static void Save<T>(string path, T value, JsonTypeInfo<T> typeInfo)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(typeInfo);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = fullPath + ".tmp";
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, value, typeInfo);
                stream.Flush(flushToDisk: true);
            }

            Replace(tempPath, fullPath);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    // Antivirus scanners, the search indexer and backup or sync tools briefly open files without FILE_SHARE_DELETE,
    // which makes the replace fail with a sharing violation or access denied. A short synchronous retry rides that out
    // (callers hold their store's lock and expect Save to either finish or throw).
    private static void Replace(string sourcePath, string destinationPath)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(sourcePath, destinationPath, overwrite: true);
                return;
            }
            catch (Exception ex) when (attempt < ReplaceAttempts && ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(ReplaceRetryDelay * attempt);
            }
        }
    }

    // The same tolerance the serializer applies (comments and trailing commas), for parsing into a JsonDocument.
    private static JsonDocumentOptions DocumentOptionsFor(JsonSerializerOptions options) => new()
    {
        AllowTrailingCommas = options.AllowTrailingCommas,
        CommentHandling = options.ReadCommentHandling == JsonCommentHandling.Disallow ? JsonCommentHandling.Disallow : JsonCommentHandling.Skip,
        MaxDepth = options.MaxDepth,
    };

    /// <summary>Moves a corrupt file aside. Returns its new path, or null if it could not be moved.</summary>
    private static string? Quarantine(string path, JsonException error, ILogger? logger, TimeProvider timeProvider)
    {
        var stamp = timeProvider.GetLocalNow().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var target = $"{path}.corrupt-{stamp}";
        for (var attempt = 2; File.Exists(target); attempt++)
        {
            target = $"{path}.corrupt-{stamp}-{attempt}";
        }

        try
        {
            File.Move(path, target);
            logger?.LogWarning(error, "{Path} was not valid JSON; it was renamed to {Target} and defaults are used instead", path, target);
            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger?.LogError(ex, "{Path} was not valid JSON and could not be renamed", path);
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a stale .tmp file is overwritten by the next save.
        }
    }
}
