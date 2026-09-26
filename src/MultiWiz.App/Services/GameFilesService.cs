using System.Diagnostics;
using MultiWiz.Core.Patching;
using MultiWiz.Core.Sessions;

namespace MultiWiz.App.Services;

/// <summary>
/// App-side guard rails for game file downloads: files can only be replaced while no client (and no official
/// launcher, which patches the same files) is running.
/// </summary>
public sealed class GameFilesService
{
    /// <summary>The game client and KingsIsle's launcher/patcher (Wizard101.exe in the install root).</summary>
    private static readonly string[] BlockingProcesses = ["WizardGraphicalClient", "Wizard101"];

    private readonly ISessionManager _sessions;

    public GameFilesService(IGameDownloader downloader, ISessionManager sessions)
    {
        Downloader = downloader;
        _sessions = sessions;
    }

    public IGameDownloader Downloader { get; }

    /// <summary>Null when it's safe to replace game files, else a message for the user.</summary>
    public string? GetBlockingReason()
    {
        if (_sessions.Sessions.Any(session => session.IsAlive))
        {
            return "Close every game client first: game files can't be replaced while Wizard101 is running.";
        }

        foreach (var name in BlockingProcesses)
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(name);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            var running = processes.Length > 0;
            foreach (var process in processes)
            {
                process.Dispose();
            }

            if (running)
            {
                return name == "Wizard101"
                    ? "Close the Wizard101 launcher first: it patches the same files."
                    : "Close every game client first (including ones started outside MultiWiz): game files can't be replaced while Wizard101 is running.";
            }
        }

        return null;
    }

    /// <summary>"1.2 GB", "340 MB", "12 KB".</summary>
    public static string FormatBytes(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0} MB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):0} KB",
        _ => $"{bytes} bytes",
    };
}
