namespace MultiWiz.Core.Patching;

/// <summary>Turns file list paths into local paths and download URLs, rejecting anything that escapes the install.</summary>
public static class PatchPaths
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    /// Resolves <paramref name="targetName"/> (e.g. <c>Data/GameData/Root.wad</c>) under <paramref name="installRoot"/>.
    /// Throws <see cref="InvalidDataException"/> for absolute paths, drive or stream syntax (<c>:</c>), <c>..</c>
    /// segments, or anything else that would land outside the install folder.
    /// </summary>
    public static string ResolveTarget(string installRoot, string targetName)
    {
        ArgumentException.ThrowIfNullOrEmpty(installRoot);
        if (string.IsNullOrWhiteSpace(targetName) || targetName.IndexOfAny([':', '\0']) >= 0)
        {
            throw Unsafe(targetName);
        }

        var normalized = targetName.Replace('\\', '/');
        if (normalized.StartsWith('/'))
        {
            throw Unsafe(targetName);
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Where(segment => segment != ".").ToArray();
        if (segments.Length == 0 || segments.Any(segment => segment.Trim() is "" or "." or ".."))
        {
            throw Unsafe(targetName);
        }

        var relative = Path.Combine(segments);
        if (Path.IsPathRooted(relative))
        {
            throw Unsafe(targetName);
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installRoot));
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, PathComparison))
        {
            throw Unsafe(targetName);
        }

        return full;
    }

    /// <summary>Normalized relative path used as a cache key (forward slashes, lower case: Windows paths ignore case).</summary>
    public static string CacheKey(string targetName) => targetName.Replace('\\', '/').Trim('/').ToLowerInvariant();

    /// <summary><paramref name="urlPrefix"/> joined with the escaped segments of <paramref name="sourceName"/>.</summary>
    public static Uri BuildFileUrl(string urlPrefix, string sourceName)
    {
        var segments = sourceName.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
        {
            throw new InvalidDataException($"The file list has an unsafe download path: \"{sourceName}\".");
        }

        var url = urlPrefix.TrimEnd('/') + "/" + string.Join('/', segments.Select(Uri.EscapeDataString));
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidDataException($"The file list has an invalid download address: \"{url}\".");
        }

        return uri;
    }

    private static InvalidDataException Unsafe(string targetName) =>
        new($"The file list has an unsafe file path: \"{targetName}\". Nothing was downloaded.");
}
