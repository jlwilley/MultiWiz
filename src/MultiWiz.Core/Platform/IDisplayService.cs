namespace MultiWiz.Core.Platform;

public interface IDisplayService
{
    /// <summary>Currently connected monitors, ordered by <see cref="MonitorInfo.Index"/>.</summary>
    IReadOnlyList<MonitorInfo> GetMonitors();
}
