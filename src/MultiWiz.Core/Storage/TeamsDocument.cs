using MultiWiz.Core.Teams;

namespace MultiWiz.Core.Storage;

/// <summary>On-disk shape of <see cref="AppPaths.TeamsFile"/>.</summary>
public sealed class TeamsDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public List<Team> Teams { get; set; } = [];
}
