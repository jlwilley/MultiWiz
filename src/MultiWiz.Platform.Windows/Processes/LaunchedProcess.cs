using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MultiWiz.Core.Platform;

namespace MultiWiz.Platform.Windows.Processes;

/// <summary>A game client started (or re-opened after a restart) by <see cref="ProcessLauncher"/>.</summary>
internal sealed class LaunchedProcess : ILaunchedProcess
{
    private readonly Process _process;
    private readonly ILogger _logger;

    public LaunchedProcess(Process process, ILogger logger)
    {
        _process = process;
        _logger = logger;
        Id = process.Id;
    }

    public int Id { get; }

    public bool HasExited
    {
        get
        {
            try
            {
                return _process.HasExited;
            }
            catch (InvalidOperationException)
            {
                // No process is associated any more (disposed).
                return true;
            }
        }
    }

    public DateTimeOffset? StartTime
    {
        get
        {
            try
            {
                return new DateTimeOffset(_process.StartTime);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                // Exited, or not ours to inspect (access denied).
                return null;
            }
        }
    }

    public string? ProcessName
    {
        get
        {
            try
            {
                return _process.ProcessName;
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                return null;
            }
        }
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken = default) =>
        _process.WaitForExitAsync(cancellationToken);

    public void Kill()
    {
        try
        {
            _process.Kill(entireProcessTree: false);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
        catch (Win32Exception) when (HasExited)
        {
            // Exited while being killed.
        }
        catch (Win32Exception ex)
        {
            _logger.LogWarning(ex, "Could not stop process {ProcessId}.", Id);
        }
    }

    public void Dispose() => _process.Dispose();
}
