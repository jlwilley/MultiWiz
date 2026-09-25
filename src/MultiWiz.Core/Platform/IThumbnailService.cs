using MultiWiz.Core.Primitives;

namespace MultiWiz.Core.Platform;

/// <summary>Live window previews via DWM thumbnails (the same mechanism Alt-Tab uses; no screen capture).</summary>
public interface IThumbnailService
{
    /// <summary>Creates a thumbnail of <paramref name="sourceWindow"/> drawn inside <paramref name="destinationWindow"/>, or null on failure.</summary>
    IWindowThumbnail? Create(nint destinationWindow, nint sourceWindow);
}

public interface IWindowThumbnail : IDisposable
{
    /// <summary>Size of the source window in pixels.</summary>
    PixelSize SourceSize { get; }

    /// <summary>
    /// Positions the thumbnail in the destination window's client area (physical pixels, relative to the
    /// client area's top-left). Aspect ratio is preserved inside the rectangle.
    /// </summary>
    void Update(PixelRect destination, bool visible, byte opacity = 255);
}
