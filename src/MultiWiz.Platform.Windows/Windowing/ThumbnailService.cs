using Microsoft.Extensions.Logging;
using MultiWiz.Core.Platform;
using MultiWiz.Core.Primitives;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;

namespace MultiWiz.Platform.Windows.Windowing;

/// <summary>Live DWM thumbnails of game windows drawn inside MultiWiz's own top-level windows.</summary>
internal sealed class ThumbnailService : IThumbnailService
{
    private readonly ILogger<ThumbnailService> _logger;

    public ThumbnailService(ILogger<ThumbnailService> logger)
    {
        _logger = logger;
    }

    public IWindowThumbnail? Create(nint destinationWindow, nint sourceWindow)
    {
        if (!NativeWindowHelpers.IsAlive(destinationWindow) || !NativeWindowHelpers.IsAlive(sourceWindow))
        {
            return null;
        }

        var result = PInvoke.DwmRegisterThumbnail((HWND)destinationWindow, (HWND)sourceWindow, out nint thumbnailId);
        if (result.Failed)
        {
            _logger.LogDebug(
                "DwmRegisterThumbnail failed for window {Source:X} (HRESULT 0x{Result:X8}).", sourceWindow, result.Value);
            return null;
        }

        return new WindowThumbnail(thumbnailId, sourceWindow, _logger);
    }

    private sealed class WindowThumbnail : IWindowThumbnail
    {
        private readonly nint _sourceWindow;
        private readonly ILogger _logger;
        private readonly Lock _gate = new();
        private nint _thumbnailId;

        public WindowThumbnail(nint thumbnailId, nint sourceWindow, ILogger logger)
        {
            _thumbnailId = thumbnailId;
            _sourceWindow = sourceWindow;
            _logger = logger;
        }

        /// <summary>
        /// The source's client-area size, which is exactly what the thumbnail shows
        /// (<c>DWM_TNP_SOURCECLIENTAREAONLY</c>), so letterboxing preserves the game's real aspect ratio.
        /// </summary>
        public PixelSize SourceSize =>
            NativeWindowHelpers.IsAlive(_sourceWindow) && NativeWindowHelpers.GetClientBounds((HWND)_sourceWindow) is { } client
                ? new PixelSize(client.Width, client.Height)
                : default;

        public void Update(PixelRect destination, bool visible, byte opacity = 255)
        {
            lock (_gate)
            {
                if (_thumbnailId == 0)
                {
                    return;
                }

                var target = Letterbox(destination, SourceSize);
                var properties = new DWM_THUMBNAIL_PROPERTIES
                {
                    dwFlags = PInvoke.DWM_TNP_RECTDESTINATION | PInvoke.DWM_TNP_VISIBLE | PInvoke.DWM_TNP_OPACITY |
                              PInvoke.DWM_TNP_SOURCECLIENTAREAONLY,
                    rcDestination = new RECT(target.X, target.Y, target.Right, target.Bottom),
                    opacity = opacity,
                    fVisible = visible && !target.IsEmpty,
                    fSourceClientAreaOnly = true,
                };

                var result = PInvoke.DwmUpdateThumbnailProperties(_thumbnailId, in properties);
                if (result.Failed)
                {
                    _logger.LogDebug(
                        "DwmUpdateThumbnailProperties failed for window {Source:X} (HRESULT 0x{Result:X8}).",
                        _sourceWindow, result.Value);
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_thumbnailId == 0)
                {
                    return;
                }

                PInvoke.DwmUnregisterThumbnail(_thumbnailId);
                _thumbnailId = 0;
            }
        }

        private static PixelRect Letterbox(PixelRect destination, PixelSize source)
        {
            if (destination.IsEmpty || source.Width <= 0 || source.Height <= 0)
            {
                return destination;
            }

            var scale = Math.Min((double)destination.Width / source.Width, (double)destination.Height / source.Height);
            var width = Math.Max(1, (int)Math.Round(source.Width * scale));
            var height = Math.Max(1, (int)Math.Round(source.Height * scale));
            var x = destination.X + ((destination.Width - width) / 2);
            var y = destination.Y + ((destination.Height - height) / 2);
            return new PixelRect(x, y, width, height);
        }
    }
}
