using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace MultiWiz.App.Services;

/// <summary>Opens web links and folders with the user's default handlers.</summary>
public static class ShellLauncher
{
    public static void OpenUrl(string url, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        Start(new ProcessStartInfo(url) { UseShellExecute = true }, logger);
    }

    public static void OpenFolder(string path, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not create folder {Path}", path);
        }

        Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = false }, logger);
    }

    private static void Start(ProcessStartInfo startInfo, ILogger logger)
    {
        try
        {
            using var process = Process.Start(startInfo);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            logger.LogWarning(ex, "Could not open {Target}", startInfo.FileName);
        }
    }
}
