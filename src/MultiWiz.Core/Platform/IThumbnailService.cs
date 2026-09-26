using MultiWiz.Core.Primitives;

namespace MultiWiz.Core.Platform;

/// <summary>Live window previews via DWM thumbnails (the same mechanism Alt-Tab uses; no screen capture).</summary>
public interface IThumbnailService
{
    /// <summary>
    /// Creates a live preview of <paramref name="sourceWindow"/> shown over <paramref name="destinationWindow"/>, or null
    /// on failure. <paramref name="onClick"/>, if given, runs when the preview is clicked (on a background thread).
    /// </summary>
    IWindowThumbnail? Create(nint destinationWindow, nint sourceWindow, Action? onClick = null);
}

public interface IWindowThumbnail : IDisposable
{
    /// <summary>Size of the source window in pixels.</summary>
    PixelSize SourceSize { get; }

    /// <summary>
    /// Positions the preview over the destination window's client area (physical pixels, relative to the client
    /// area's top-left). Aspect ratio is preserved inside the rectangle. Call again after the destination window moves.
    /// </summary>
    void Update(PixelRect destination, bool visible, byte opacity = 255);
}
