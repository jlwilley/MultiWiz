using MultiWiz.Core.Games;

namespace MultiWiz.Core.Platform;

public sealed record SteamReadiness(bool Ready, string? Error);

/// <summary>Prepares Steam for launching a Steam install's client directly (with <c>-ST</c>).</summary>
public interface ISteamSupport
{
    /// <summary>
    /// Ensures Steam is running and signed in (starting it and waiting up to <paramref name="timeout"/> if needed)
    /// and that <c>Bin\steam_appid.txt</c> contains <see cref="GameInstall.SteamAppId"/>.
    /// </summary>
    Task<SteamReadiness> EnsureReadyAsync(GameInstall install, TimeSpan timeout, CancellationToken cancellationToken = default);
}
