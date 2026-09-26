using MultiWiz.Core.Primitives;

namespace MultiWiz.Core.Platform;

/// <summary>
/// A connected display. <see cref="Index"/> is 0 for the primary monitor, then the rest sorted left-to-right,
/// top-to-bottom. All rectangles are physical pixels in virtual-desktop coordinates.
/// </summary>
public sealed record MonitorInfo(int Index, string DeviceName, PixelRect Bounds, PixelRect WorkArea, double Scale, bool IsPrimary);
