using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using MultiWiz.Core.Storage;

namespace MultiWiz.App.Services;

/// <summary>Where a window was last shown. <see cref="X"/>/<see cref="Y"/> are physical pixels, the size is in DIPs.</summary>
public sealed record WindowPlacement(int X, int Y, double Width, double Height, bool IsMaximized);

[JsonSerializable(typeof(Dictionary<string, WindowPlacement>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
internal sealed partial class WindowPlacementJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Remembers window sizes and positions per machine (in %LocalAppData%\MultiWiz\state, because monitor layouts are
/// machine specific). UI thread only.
/// </summary>
public sealed class WindowPlacementStore
{
    private readonly string _path;
    private readonly ILogger<WindowPlacementStore> _logger;
    private Dictionary<string, WindowPlacement>? _placements;

    public WindowPlacementStore(AppPaths paths, ILogger<WindowPlacementStore> logger)
    {
        _path = Path.Combine(paths.LocalRoot, "state", "window-placements.json");
        _logger = logger;
    }

    public WindowPlacement? Get(string windowName) =>
        Placements.TryGetValue(windowName, out var placement) ? placement : null;

    public void Set(string windowName, WindowPlacement placement)
    {
        if (Placements.TryGetValue(windowName, out var existing) && existing == placement)
        {
            return;
        }

        Placements[windowName] = placement;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporaryPath = _path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(Placements, WindowPlacementJsonContext.Default.DictionaryStringWindowPlacement));
            File.Move(temporaryPath, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not save window placement to {Path}", _path);
        }
    }

    private Dictionary<string, WindowPlacement> Placements => _placements ??= Load();

    private Dictionary<string, WindowPlacement> Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                var loaded = JsonSerializer.Deserialize(json, WindowPlacementJsonContext.Default.DictionaryStringWindowPlacement);
                if (loaded is not null)
                {
                    return new Dictionary<string, WindowPlacement>(loaded, StringComparer.Ordinal);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(ex, "Could not read window placements from {Path}; using defaults", _path);
        }

        return new Dictionary<string, WindowPlacement>(StringComparer.Ordinal);
    }
}
