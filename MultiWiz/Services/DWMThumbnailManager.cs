using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using MultiWiz.Win32;

namespace MultiWiz.Services;

/// <summary>
/// Manages DWM thumbnails for displaying live window previews
/// </summary>
public class DWMThumbnailManager : IDisposable
{
    private readonly Dictionary<IntPtr, IntPtr> _thumbnails = new(); // sourceWindow -> thumbnailHandle
    private IntPtr _destinationWindow = IntPtr.Zero;
    private bool _isInitialized = false;

    /// <summary>
    /// Initializes the thumbnail manager with a destination window
    /// </summary>
    /// <param name="destinationWindow">The WPF window that will host the thumbnails</param>
    public void Initialize(Window destinationWindow)
    {
        if (_isInitialized)
        {
            Debug.WriteLine("DWMThumbnailManager already initialized");
            return;
        }

        try
        {
            var helper = new WindowInteropHelper(destinationWindow);
            _destinationWindow = helper.Handle;

            if (_destinationWindow == IntPtr.Zero)
            {
                throw new Exception("Failed to get window handle");
            }

            _isInitialized = true;
            Debug.WriteLine("DWMThumbnailManager initialized successfully");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error initializing DWMThumbnailManager: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Registers a thumbnail for a source window
    /// </summary>
    /// <param name="sourceWindow">The window to create a thumbnail for</param>
    /// <returns>The thumbnail handle, or IntPtr.Zero if registration failed</returns>
    public IntPtr RegisterThumbnail(IntPtr sourceWindow)
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException("DWMThumbnailManager not initialized");
        }

        if (sourceWindow == IntPtr.Zero || !DwmInterop.IsWindow(sourceWindow))
        {
            Debug.WriteLine("Invalid source window handle");
            return IntPtr.Zero;
        }

        // Check if already registered
        if (_thumbnails.ContainsKey(sourceWindow))
        {
            Debug.WriteLine($"Thumbnail already registered for window {sourceWindow}");
            return _thumbnails[sourceWindow];
        }

        try
        {
            int result = DwmInterop.DwmRegisterThumbnail(_destinationWindow, sourceWindow, out IntPtr thumbnailHandle);

            if (result == 0 && thumbnailHandle != IntPtr.Zero) // S_OK
            {
                _thumbnails[sourceWindow] = thumbnailHandle;
                Debug.WriteLine($"Thumbnail registered: {thumbnailHandle} for source {sourceWindow}");
                return thumbnailHandle;
            }
            else
            {
                Debug.WriteLine($"Failed to register thumbnail. HRESULT: 0x{result:X8}");
                return IntPtr.Zero;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error registering thumbnail: {ex.Message}");
            return IntPtr.Zero;
        }
    }

    /// <summary>
    /// Updates the properties of a thumbnail (position, size, visibility, etc.)
    /// </summary>
    /// <param name="thumbnailHandle">The thumbnail to update</param>
    /// <param name="destinationRect">The rectangle where the thumbnail should be rendered</param>
    /// <param name="opacity">Opacity (0-255)</param>
    /// <param name="visible">Whether the thumbnail should be visible</param>
    /// <param name="sourceClientAreaOnly">Whether to show only the client area</param>
    public bool UpdateThumbnail(IntPtr thumbnailHandle, Rect destinationRect, byte opacity = 255, bool visible = true, bool sourceClientAreaOnly = true)
    {
        if (thumbnailHandle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var props = new DwmInterop.DWM_THUMBNAIL_PROPERTIES
            {
                dwFlags = DwmInterop.DWM_TNP.RECTDESTINATION |
                          DwmInterop.DWM_TNP.OPACITY |
                          DwmInterop.DWM_TNP.VISIBLE |
                          DwmInterop.DWM_TNP.SOURCECLIENTAREAONLY,
                rcDestination = new DwmInterop.RECT(
                    (int)destinationRect.Left,
                    (int)destinationRect.Top,
                    (int)destinationRect.Right,
                    (int)destinationRect.Bottom
                ),
                opacity = opacity,
                fVisible = visible,
                fSourceClientAreaOnly = sourceClientAreaOnly
            };

            int result = DwmInterop.DwmUpdateThumbnailProperties(thumbnailHandle, ref props);

            if (result == 0) // S_OK
            {
                return true;
            }
            else
            {
                Debug.WriteLine($"Failed to update thumbnail. HRESULT: 0x{result:X8}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error updating thumbnail: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Updates thumbnail with source rectangle cropping
    /// </summary>
    public bool UpdateThumbnailWithSource(IntPtr thumbnailHandle, Rect destinationRect, Rect? sourceRect = null, byte opacity = 255, bool visible = true, bool sourceClientAreaOnly = true)
    {
        if (thumbnailHandle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var flags = DwmInterop.DWM_TNP.RECTDESTINATION |
                       DwmInterop.DWM_TNP.OPACITY |
                       DwmInterop.DWM_TNP.VISIBLE |
                       DwmInterop.DWM_TNP.SOURCECLIENTAREAONLY;

            var props = new DwmInterop.DWM_THUMBNAIL_PROPERTIES
            {
                dwFlags = flags,
                rcDestination = new DwmInterop.RECT(
                    (int)destinationRect.Left,
                    (int)destinationRect.Top,
                    (int)destinationRect.Right,
                    (int)destinationRect.Bottom
                ),
                opacity = opacity,
                fVisible = visible,
                fSourceClientAreaOnly = sourceClientAreaOnly
            };

            if (sourceRect.HasValue)
            {
                props.dwFlags |= DwmInterop.DWM_TNP.RECTSOURCE;
                props.rcSource = new DwmInterop.RECT(
                    (int)sourceRect.Value.Left,
                    (int)sourceRect.Value.Top,
                    (int)sourceRect.Value.Right,
                    (int)sourceRect.Value.Bottom
                );
            }

            int result = DwmInterop.DwmUpdateThumbnailProperties(thumbnailHandle, ref props);
            return result == 0; // S_OK
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error updating thumbnail with source: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Gets the source size of a thumbnail
    /// </summary>
    public Size GetThumbnailSourceSize(IntPtr thumbnailHandle)
    {
        if (thumbnailHandle == IntPtr.Zero)
        {
            return Size.Empty;
        }

        try
        {
            int result = DwmInterop.DwmQueryThumbnailSourceSize(thumbnailHandle, out DwmInterop.SIZE size);

            if (result == 0) // S_OK
            {
                return new Size(size.cx, size.cy);
            }
            else
            {
                Debug.WriteLine($"Failed to query thumbnail source size. HRESULT: 0x{result:X8}");
                return Size.Empty;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error querying thumbnail source size: {ex.Message}");
            return Size.Empty;
        }
    }

    /// <summary>
    /// Unregisters a thumbnail for a specific source window
    /// </summary>
    public void UnregisterThumbnail(IntPtr sourceWindow)
    {
        if (_thumbnails.TryGetValue(sourceWindow, out IntPtr thumbnailHandle))
        {
            try
            {
                int result = DwmInterop.DwmUnregisterThumbnail(thumbnailHandle);
                if (result == 0) // S_OK
                {
                    Debug.WriteLine($"Thumbnail unregistered: {thumbnailHandle}");
                }
                else
                {
                    Debug.WriteLine($"Failed to unregister thumbnail. HRESULT: 0x{result:X8}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error unregistering thumbnail: {ex.Message}");
            }
            finally
            {
                _thumbnails.Remove(sourceWindow);
            }
        }
    }

    /// <summary>
    /// Gets the thumbnail handle for a source window
    /// </summary>
    public IntPtr GetThumbnailHandle(IntPtr sourceWindow)
    {
        return _thumbnails.TryGetValue(sourceWindow, out IntPtr handle) ? handle : IntPtr.Zero;
    }

    /// <summary>
    /// Checks if a thumbnail is registered for a source window
    /// </summary>
    public bool IsThumbnailRegistered(IntPtr sourceWindow)
    {
        return _thumbnails.ContainsKey(sourceWindow);
    }

    /// <summary>
    /// Unregisters all thumbnails
    /// </summary>
    public void UnregisterAllThumbnails()
    {
        var sourceWindows = new List<IntPtr>(_thumbnails.Keys);
        foreach (var sourceWindow in sourceWindows)
        {
            UnregisterThumbnail(sourceWindow);
        }

        _thumbnails.Clear();
        Debug.WriteLine("All thumbnails unregistered");
    }

    /// <summary>
    /// Gets the count of registered thumbnails
    /// </summary>
    public int ThumbnailCount => _thumbnails.Count;

    /// <summary>
    /// Cleans up invalid thumbnails (where source window no longer exists)
    /// </summary>
    public void CleanupInvalidThumbnails()
    {
        var invalidWindows = new List<IntPtr>();

        foreach (var sourceWindow in _thumbnails.Keys)
        {
            if (!DwmInterop.IsWindow(sourceWindow) || !DwmInterop.IsWindowVisible(sourceWindow))
            {
                invalidWindows.Add(sourceWindow);
            }
        }

        foreach (var window in invalidWindows)
        {
            Debug.WriteLine($"Cleaning up invalid thumbnail for window {window}");
            UnregisterThumbnail(window);
        }

        if (invalidWindows.Count > 0)
        {
            Debug.WriteLine($"Cleaned up {invalidWindows.Count} invalid thumbnails");
        }
    }

    /// <summary>
    /// Disposes the thumbnail manager and unregisters all thumbnails
    /// </summary>
    public void Dispose()
    {
        UnregisterAllThumbnails();
        _isInitialized = false;
        GC.SuppressFinalize(this);
    }

    ~DWMThumbnailManager()
    {
        UnregisterAllThumbnails();
    }
}
