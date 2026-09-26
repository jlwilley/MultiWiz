using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging;
using MultiWiz.App.Services;
using MultiWiz.App.ViewModels;
using MultiWiz.Core.Platform;
using CorePixelRect = MultiWiz.Core.Primitives.PixelRect;
using CorePixelSize = MultiWiz.Core.Primitives.PixelSize;

namespace MultiWiz.App.Views;

/// <summary>
/// Command Center. The view model supplies the tiles; this code-behind places a live DWM thumbnail of each game
/// window over its tile's placeholder, converting the placeholder's DIP bounds to physical pixels.
/// </summary>
public partial class CommandCenterWindow : Window
{
    private const string PlacementKey = "command-center";
    private const string ThumbnailHostClass = "thumb-host";

    private readonly Dictionary<nint, ThumbnailEntry> _thumbnails = new();
    private readonly HashSet<nint> _failedSources = [];

    // A game window can change size without anything here moving (an arrange with resizing, a manual resize, a client
    // that was minimized when its tile appeared), so re-check the previews' aspect ratio while the window is shown.
    private readonly DispatcherTimer _sourceSizeCheck = new() { Interval = TimeSpan.FromSeconds(1) };
    private IThumbnailService? _thumbnailService;
    private WindowPlacementTracker? _placement;
    private ILogger? _logger;
    private bool _wasShown;

    public CommandCenterWindow()
    {
        InitializeComponent();
        LayoutUpdated += (_, _) => SyncThumbnails();
        ScalingChanged += (_, _) => SyncThumbnails();
        _sourceSizeCheck.Tick += (_, _) => SyncThumbnails();
    }

    public void Attach(IThumbnailService thumbnailService, WindowPlacementStore placements, ILogger logger)
    {
        _thumbnailService = thumbnailService;
        _logger = logger;
        _placement = new WindowPlacementTracker(this, placements, PlacementKey);
        _placement.Restore();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty)
        {
            // Minimizing releases the previews; restoring may not trigger a layout pass, so recreate them explicitly.
            Dispatcher.UIThread.Post(SyncThumbnails, DispatcherPriority.Background);
            return;
        }

        if (change.Property != IsVisibleProperty)
        {
            return;
        }

        if (IsVisible)
        {
            _wasShown = true;
            _sourceSizeCheck.Start();

            // Layout may already be settled when the window is shown again, so sync once after it is rendered.
            Dispatcher.UIThread.Post(SyncThumbnails, DispatcherPriority.Background);
        }
        else
        {
            _sourceSizeCheck.Stop();
            SavePlacement();
            ReleaseThumbnails();
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        SavePlacement();
        if (e.CloseReason == WindowCloseReason.WindowClosing && !e.IsProgrammatic)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _sourceSizeCheck.Stop();
        ReleaseThumbnails();
        base.OnClosed(e);
    }

    private void SyncThumbnails()
    {
        if (_thumbnailService is null || !IsVisible || WindowState == WindowState.Minimized
            || TryGetPlatformHandle() is not { } handle)
        {
            ReleaseThumbnails();
            return;
        }

        var scale = RenderScaling;
        var shown = new HashSet<nint>();
        foreach (var host in this.GetVisualDescendants().OfType<Border>())
        {
            if (!host.Classes.Contains(ThumbnailHostClass)
                || host.DataContext is not ThumbnailTileViewModel tile
                || tile.WindowHandle == 0
                || host.TranslatePoint(new Point(0, 0), this) is not { } origin)
            {
                continue;
            }

            var destination = new CorePixelRect(
                (int)Math.Round(origin.X * scale),
                (int)Math.Round(origin.Y * scale),
                (int)Math.Round(host.Bounds.Width * scale),
                (int)Math.Round(host.Bounds.Height * scale));

            if (!_thumbnails.TryGetValue(tile.WindowHandle, out var entry))
            {
                var thumbnail = _thumbnailService.Create(handle.Handle, tile.WindowHandle);
                if (thumbnail is null)
                {
                    if (_failedSources.Add(tile.WindowHandle))
                    {
                        _logger?.LogWarning("Could not create a live preview for window {Window}", tile.WindowHandle);
                    }

                    continue;
                }

                entry = new ThumbnailEntry(thumbnail);
                _thumbnails[tile.WindowHandle] = entry;
            }

            // The letterbox inside the destination follows the game's current client size.
            var sourceSize = entry.Thumbnail.SourceSize;
            if (entry.LastDestination != destination || entry.LastSourceSize != sourceSize)
            {
                entry.Thumbnail.Update(destination, visible: !destination.IsEmpty);
                entry.LastDestination = destination;
                entry.LastSourceSize = sourceSize;
            }

            shown.Add(tile.WindowHandle);
        }

        foreach (var source in _thumbnails.Keys.Where(source => !shown.Contains(source)).ToArray())
        {
            _thumbnails[source].Thumbnail.Dispose();
            _thumbnails.Remove(source);
        }
    }

    private void ReleaseThumbnails()
    {
        foreach (var entry in _thumbnails.Values)
        {
            entry.Thumbnail.Dispose();
        }

        _thumbnails.Clear();
        _failedSources.Clear();
    }

    private void SavePlacement()
    {
        if (_wasShown)
        {
            _placement?.Save();
        }
    }

    private sealed class ThumbnailEntry(IWindowThumbnail thumbnail)
    {
        public IWindowThumbnail Thumbnail { get; } = thumbnail;

        public CorePixelRect? LastDestination { get; set; }

        public CorePixelSize LastSourceSize { get; set; }
    }
}
