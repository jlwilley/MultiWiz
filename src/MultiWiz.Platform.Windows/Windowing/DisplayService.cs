using Microsoft.Extensions.Logging;
using MultiWiz.Core.Platform;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;

namespace MultiWiz.Platform.Windows.Windowing;

/// <summary>Connected monitors in physical pixels, primary first, then left-to-right and top-to-bottom.</summary>
internal sealed unsafe class DisplayService : IDisplayService
{
    private const uint MonitorInfoFlagPrimary = 1; // MONITORINFOF_PRIMARY
    private const double DefaultDpi = 96.0;

    private readonly ILogger<DisplayService> _logger;

    public DisplayService(ILogger<DisplayService> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var handles = new List<HMONITOR>();
        if (!PInvoke.EnumDisplayMonitors(HDC.Null, (RECT?)null, (monitor, _, _, _) =>
            {
                handles.Add(monitor);
                return true;
            }, default))
        {
            _logger.LogWarning("EnumDisplayMonitors failed.");
        }

        var monitors = new List<MonitorInfo>(handles.Count);
        foreach (var handle in handles)
        {
            MONITORINFOEXW info = default;
            info.monitorInfo.cbSize = (uint)sizeof(MONITORINFOEXW);
            if (!PInvoke.GetMonitorInfo(handle, (MONITORINFO*)&info))
            {
                continue;
            }

            var dpiResult = PInvoke.GetDpiForMonitor(handle, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out uint dpiX, out _);
            var scale = dpiResult.Succeeded && dpiX > 0 ? dpiX / DefaultDpi : 1.0;

            monitors.Add(new MonitorInfo(
                Index: 0,
                DeviceName: info.szDevice.ToString(),
                Bounds: NativeWindowHelpers.ToPixelRect(info.monitorInfo.rcMonitor),
                WorkArea: NativeWindowHelpers.ToPixelRect(info.monitorInfo.rcWork),
                Scale: scale,
                IsPrimary: (info.monitorInfo.dwFlags & MonitorInfoFlagPrimary) != 0));
        }

        return monitors
            .OrderByDescending(m => m.IsPrimary)
            .ThenBy(m => m.Bounds.X)
            .ThenBy(m => m.Bounds.Y)
            .Select((m, index) => m with { Index = index })
            .ToList();
    }
}
