using Microsoft.Extensions.Logging;
using MultiWiz.Core.Platform;
using MultiWiz.Core.Sessions;

namespace MultiWiz.Core.Teams;

/// <summary>Moves (and optionally resizes) session windows into the cells of a layout.</summary>
public sealed class WindowArranger : IWindowArranger
{
    private readonly IDisplayService _display;
    private readonly IWindowService _windows;
    private readonly ILogger<WindowArranger> _logger;

    public WindowArranger(IDisplayService display, IWindowService windows, ILogger<WindowArranger> logger)
    {
        _display = display;
        _windows = windows;
        _logger = logger;
    }

    /// <summary>
    /// Session N goes to cell N (cells repeat). Sessions without a window keep their cell but are skipped.
    /// Returns how many windows were placed.
    /// </summary>
    public int Arrange(IReadOnlyList<ClientSession> orderedSessions, WindowLayout layout, bool resize)
    {
        ArgumentNullException.ThrowIfNull(orderedSessions);
        ArgumentNullException.ThrowIfNull(layout);

        if (orderedSessions.Count == 0 || layout.Cells.Count == 0)
        {
            return 0;
        }

        var rects = LayoutCalculator.Arrange(layout, _display.GetMonitors(), orderedSessions.Count);
        if (rects.Count == 0)
        {
            _logger.LogWarning("No monitors were reported; windows were not arranged");
            return 0;
        }

        var placed = 0;
        for (var i = 0; i < orderedSessions.Count; i++)
        {
            var session = orderedSessions[i];
            if (!session.HasWindow)
            {
                continue;
            }

            if (_windows.SetBounds(session.WindowHandle, rects[i], resize))
            {
                placed++;
            }
            else
            {
                _logger.LogDebug("Could not move the window of account {AccountId}", session.AccountId);
            }
        }

        _logger.LogInformation("Arranged {Placed} windows with layout {LayoutId}", placed, layout.Id);
        return placed;
    }
}
