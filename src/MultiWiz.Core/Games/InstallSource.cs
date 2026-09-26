namespace MultiWiz.Core.Games;

public enum InstallSource
{
    /// <summary>KingsIsle's own installer (defaults to C:\ProgramData\KingsIsle Entertainment\...).</summary>
    Standalone = 0,
    /// <summary>Installed through a Steam library.</summary>
    Steam = 1,
    /// <summary>A folder the user picked by hand.</summary>
    Custom = 2,
}
