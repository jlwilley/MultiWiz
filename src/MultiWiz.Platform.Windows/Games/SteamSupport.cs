using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MultiWiz.Core.Games;
using MultiWiz.Core.Platform;

namespace MultiWiz.Platform.Windows.Games;

/// <summary>
/// Gets Steam ready for launching a Steam install's client directly: Steam running and signed in, and
/// <c>Bin\steam_appid.txt</c> naming the game's app id (the client refuses Steam mode without it).
/// </summary>
internal sealed class SteamSupport : ISteamSupport
{
    private const string AppIdFileName = "steam_appid.txt";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    // Steam reports the signed-in user slightly before it is ready to hand out game sessions.
    private static readonly TimeSpan SignInSettleDelay = TimeSpan.FromSeconds(3);

    private static readonly SteamReadiness Ready = new(true, null);

    private readonly ILogger<SteamSupport> _logger;

    public SteamSupport(ILogger<SteamSupport> logger)
    {
        _logger = logger;
    }

    public async Task<SteamReadiness> EnsureReadyAsync(GameInstall install, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(install);
        if (string.IsNullOrWhiteSpace(install.SteamAppId))
        {
            return Ready;
        }

        var appIdError = EnsureAppIdFile(install.BinPath, install.SteamAppId.Trim());
        if (appIdError is not null)
        {
            return new SteamReadiness(false, appIdError);
        }

        if (IsSignedIn())
        {
            return Ready;
        }

        if (!IsSteamRunning())
        {
            var startError = StartSteam();
            if (startError is not null)
            {
                return new SteamReadiness(false, startError);
            }
        }

        _logger.LogInformation("Waiting up to {Timeout} for Steam to finish signing in.", timeout);
        var waitStarted = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(waitStarted) < timeout)
        {
            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            if (IsSignedIn())
            {
                await Task.Delay(SignInSettleDelay, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Steam is signed in.");
                return Ready;
            }
        }

        return new SteamReadiness(
            false,
            $"Steam didn't finish signing in within {FormatDuration(timeout)}. Sign in to Steam, then launch again.");
    }

    private string? EnsureAppIdFile(string binPath, string appId)
    {
        var path = Path.Combine(binPath, AppIdFileName);
        try
        {
            if (File.Exists(path) && string.Equals(File.ReadAllText(path).Trim(), appId, StringComparison.Ordinal))
            {
                return null;
            }

            File.WriteAllText(path, appId);
            _logger.LogInformation("Wrote Steam app id {AppId} to {Path}.", appId, path);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not write {Path}.", path);
            return $"MultiWiz couldn't write {AppIdFileName} in {binPath}: {ex.Message}";
        }
    }

    private static bool IsSignedIn() => IsSteamRunning() && SteamRegistry.GetActiveUser() != 0;

    private static bool IsSteamRunning()
    {
        var processId = SteamRegistry.GetActiveProcessId();
        if (processId <= 0)
        {
            return false;
        }

        // The registry keeps the last pid after a crash, and pids get reused, so check the process is really Steam.
        try
        {
            using var process = Process.GetProcessById(processId);
            return string.Equals(process.ProcessName, "steam", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false; // Not running.
        }
        catch (InvalidOperationException)
        {
            return false; // Exited while being inspected.
        }
    }

    private string? StartSteam()
    {
        var steamExe = SteamRegistry.GetSteamExecutable();
        if (steamExe is null)
        {
            return "Steam doesn't seem to be installed. Install Steam, or launch from a standalone game folder instead.";
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = steamExe,
                WorkingDirectory = Path.GetDirectoryName(steamExe) ?? string.Empty,
                UseShellExecute = false,
            });
            _logger.LogInformation("Started Steam from {Path}.", steamExe);
            return null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Could not start Steam from {Path}.", steamExe);
            return $"MultiWiz couldn't start Steam: {ex.Message}";
        }
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalMinutes >= 1)
        {
            var minutes = (int)Math.Round(duration.TotalMinutes);
            return minutes == 1 ? "1 minute" : $"{minutes} minutes";
        }

        var seconds = Math.Max(1, (int)Math.Round(duration.TotalSeconds));
        return seconds == 1 ? "1 second" : $"{seconds} seconds";
    }
}
