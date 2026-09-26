using System.Security;

namespace MultiWiz.Platform.Windows.Games;

/// <summary>Normalizes paths read from the registry and Steam files (quotes, forward slashes, trailing separators).</summary>
internal static class InstallPaths
{
    public static string? NormalizeDirectory(string? path)
    {
        var full = NormalizeFile(path);
        return full is null ? null : Path.TrimEndingDirectorySeparator(full);
    }

    public static string? NormalizeFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var cleaned = path.Trim().Trim('"').Replace('/', Path.DirectorySeparatorChar);
        if (cleaned.Length == 0)
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(cleaned);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or SecurityException)
        {
            return null;
        }
    }
}
