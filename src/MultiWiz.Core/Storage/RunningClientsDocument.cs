namespace MultiWiz.Core.Storage;

/// <summary>On-disk shape of <see cref="AppPaths.RunningClientsFile"/>.</summary>
public sealed class RunningClientsDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public List<RunningClient> Clients { get; set; } = [];
}

/// <summary>A client process MultiWiz started for an account, identified by its id and start time (ids are reused).</summary>
public sealed record RunningClient
{
    public required Guid AccountId { get; init; }
    public required int ProcessId { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
}
