using System.Reflection;

namespace MultiWiz.App;

/// <summary>Static facts about this build and where the project lives.</summary>
public static class AppInfo
{
    public const string RepositoryUrl = "https://github.com/jlwilley/MultiWiz";
    public const string ReleasesUrl = RepositoryUrl + "/releases";
    public const string IssuesUrl = RepositoryUrl + "/issues";

    /// <summary>Product version without build metadata, e.g. "4.1.0-beta.2".</summary>
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var informational = typeof(AppInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
        {
            return typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }

        var metadataStart = informational.IndexOf('+');
        return metadataStart > 0 ? informational[..metadataStart] : informational;
    }
}
