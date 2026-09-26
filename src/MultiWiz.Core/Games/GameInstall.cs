using System.Text.Json.Serialization;
using MultiWiz.Core.Storage;

namespace MultiWiz.Core.Games;

/// <summary>A game installation on disk that clients can be launched from.</summary>
public sealed record GameInstall
{
    /// <summary>
    /// Stable identifier. Discovered installs use deterministic ids (e.g. "standalone-wizard101",
    /// "steam-wizard101-&lt;hash of root&gt;"); custom installs use "custom-&lt;guid&gt;".
    /// </summary>
    public required string Id { get; init; }
    /// <summary>A game or source this build does not know (from a newer build's settings) reads as an undefined value.</summary>
    [JsonConverter(typeof(TolerantEnumConverter<GameKind>))]
    public required GameKind Game { get; init; }

    [JsonConverter(typeof(TolerantEnumConverter<InstallSource>))]
    public required InstallSource Source { get; init; }

    /// <summary>Game root folder, e.g. C:\ProgramData\KingsIsle Entertainment\Wizard101. The client lives in Bin\.</summary>
    public required string RootPath { get; init; }

    public string? DisplayName { get; init; }

    /// <summary>Extra client arguments appended after the realm's <c>-L host port</c>.</summary>
    public string? ExtraArguments { get; init; }

    /// <summary>
    /// Steam app id for Steam installs (Wizard101 is 799960). When set, the client is started in Steam mode:
    /// <c>-ST</c> is passed, Steam must be running and signed in, and Bin\steam_appid.txt must contain the id.
    /// </summary>
    public string? SteamAppId { get; init; }

    [JsonIgnore]
    public string BinPath => Path.Combine(RootPath, "Bin");

    [JsonIgnore]
    public string ExecutablePath => Path.Combine(BinPath, GameExecutables.ClientExecutableName(Game));
}
