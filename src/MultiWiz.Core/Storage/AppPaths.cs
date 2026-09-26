namespace MultiWiz.Core.Storage;

/// <summary>Where MultiWiz keeps its files. Construct with a custom root in tests.</summary>
public sealed class AppPaths
{
    public AppPaths(string roamingRoot, string localRoot)
    {
        RoamingRoot = roamingRoot;
        LocalRoot = localRoot;
    }

    /// <summary>%AppData%\MultiWiz and %LocalAppData%\MultiWiz.</summary>
    public static AppPaths Default { get; } = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MultiWiz"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MultiWiz"));

    /// <summary>%AppData%\MultiWiz — also where v3 kept config.txt / settings.txt.</summary>
    public string RoamingRoot { get; }

    /// <summary>%LocalAppData%\MultiWiz — Velopack installs the app itself under here too, so only use subfolders.</summary>
    public string LocalRoot { get; }

    /// <summary>v4 data folder, separate from v3 files so the two never clash.</summary>
    public string DataDirectory => Path.Combine(RoamingRoot, "v4");

    public string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public string AccountsFile => Path.Combine(DataDirectory, "accounts.json");
    public string TeamsFile => Path.Combine(DataDirectory, "teams.json");

    public string LogsDirectory => Path.Combine(LocalRoot, "logs");

    /// <summary>Machine-local state that is not worth roaming (window positions, running clients).</summary>
    public string StateDirectory => Path.Combine(LocalRoot, "state");

    /// <summary>The clients MultiWiz started that are still running, so they are picked up again after a restart.</summary>
    public string RunningClientsFile => Path.Combine(StateDirectory, "running-clients.json");

    public string LegacyConfigFile => Path.Combine(RoamingRoot, "config.txt");
    public string LegacySettingsFile => Path.Combine(RoamingRoot, "settings.txt");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}
