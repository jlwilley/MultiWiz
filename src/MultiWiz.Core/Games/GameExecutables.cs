namespace MultiWiz.Core.Games;

public static class GameExecutables
{
    public static string ClientExecutableName(GameKind game) => game switch
    {
        GameKind.Pirate101 => "PirateGraphicalClient.exe",
        _ => "WizardGraphicalClient.exe",
    };

    /// <summary>Process name as reported by the OS (executable name without extension).</summary>
    public static string ClientProcessName(GameKind game) =>
        Path.GetFileNameWithoutExtension(ClientExecutableName(game));

    /// <summary>Default root folder used by KingsIsle's standalone installer.</summary>
    public static string DefaultStandaloneRoot(GameKind game) => game switch
    {
        GameKind.Pirate101 => @"C:\ProgramData\KingsIsle Entertainment\Pirate101",
        _ => @"C:\ProgramData\KingsIsle Entertainment\Wizard101",
    };

    /// <summary>Folder name under <c>steamapps\common</c>.</summary>
    public static string SteamInstallDirName(GameKind game) => game switch
    {
        GameKind.Pirate101 => "Pirate101",
        _ => "Wizard101",
    };
}
