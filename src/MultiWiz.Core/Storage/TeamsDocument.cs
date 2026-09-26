using System.Text.Json;
using System.Text.Json.Serialization;
using MultiWiz.Core.Teams;

namespace MultiWiz.Core.Storage;

/// <summary>On-disk shape of <see cref="AppPaths.TeamsFile"/>.</summary>
public sealed class TeamsDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public List<Team> Teams { get; set; } = [];

    /// <summary>Properties this build does not know (written by a newer one), kept so a save does not drop them.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
