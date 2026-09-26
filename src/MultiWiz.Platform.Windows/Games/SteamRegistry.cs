using System.Security;
using Microsoft.Win32;

namespace MultiWiz.Platform.Windows.Games;

/// <summary>Reads Steam's registry values (install location and the running client's state).</summary>
internal static class SteamRegistry
{
    private const string UserSteamKey = @"Software\Valve\Steam";
    private const string ActiveProcessKey = @"Software\Valve\Steam\ActiveProcess";
    private const string MachineSteamKey = @"SOFTWARE\Valve\Steam";

    /// <summary>Steam's install folder: HKCU <c>SteamPath</c>, falling back to HKLM (32-bit view) <c>InstallPath</c>.</summary>
    public static string? GetSteamPath() =>
        InstallPaths.NormalizeDirectory(ReadString(RegistryHive.CurrentUser, RegistryView.Default, UserSteamKey, "SteamPath"))
        ?? InstallPaths.NormalizeDirectory(ReadString(RegistryHive.LocalMachine, RegistryView.Registry32, MachineSteamKey, "InstallPath"));

    /// <summary>Full path of steam.exe, or null if Steam is not installed.</summary>
    public static string? GetSteamExecutable()
    {
        var registered = InstallPaths.NormalizeFile(
            ReadString(RegistryHive.CurrentUser, RegistryView.Default, UserSteamKey, "SteamExe"));
        if (registered is not null && File.Exists(registered))
        {
            return registered;
        }

        var steamPath = GetSteamPath();
        if (steamPath is null)
        {
            return null;
        }

        var candidate = Path.Combine(steamPath, "steam.exe");
        return File.Exists(candidate) ? candidate : null;
    }

    /// <summary>Process id of the running Steam client as Steam reports it (0 when not running).</summary>
    public static int GetActiveProcessId() =>
        ReadDword(RegistryHive.CurrentUser, RegistryView.Default, ActiveProcessKey, "pid");

    /// <summary>Steam account id of the signed-in user (0 when nobody is signed in).</summary>
    public static int GetActiveUser() =>
        ReadDword(RegistryHive.CurrentUser, RegistryView.Default, ActiveProcessKey, "ActiveUser");

    private static string? ReadString(RegistryHive hive, RegistryView view, string keyPath, string valueName) =>
        ReadValue(hive, view, keyPath, valueName) as string;

    private static int ReadDword(RegistryHive hive, RegistryView view, string keyPath, string valueName) =>
        ReadValue(hive, view, keyPath, valueName) is int value ? value : 0;

    private static object? ReadValue(RegistryHive hive, RegistryView view, string keyPath, string valueName)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(keyPath);
            return key?.GetValue(valueName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return null;
        }
    }
}
