using MultiWiz.Core.Storage;

namespace MultiWiz.Core.Tests.Support;

/// <summary>A unique folder under the system temp directory, deleted on dispose.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Root = Path.Combine(Path.GetTempPath(), "MultiWiz.Core.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string Combine(params string[] parts)
    {
        var path = Root;
        foreach (var part in parts)
        {
            path = Path.Combine(path, part);
        }

        return path;
    }

    public AppPaths CreateAppPaths() => new(Combine("Roaming", "MultiWiz"), Combine("Local", "MultiWiz"));

    /// <summary>Creates <c>&lt;root&gt;\Bin\&lt;client exe&gt;</c> (empty) so the folder looks like a game install.</summary>
    public string CreateGameFolder(string name, string executableName)
    {
        var root = Combine(name);
        Directory.CreateDirectory(Path.Combine(root, "Bin"));
        File.WriteAllBytes(Path.Combine(root, "Bin", executableName), Array.Empty<byte>());
        return root;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: the OS cleans the temp folder eventually.
        }
    }
}
