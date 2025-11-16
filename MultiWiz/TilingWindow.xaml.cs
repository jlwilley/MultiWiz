using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MultiWiz.Services;
using MultiWiz.Win32;

namespace MultiWiz;

public partial class TilingWindow : Window
{
    private DWMThumbnailManager _thumbnailManager;
    private TilingLayout _currentLayout = TilingLayout.Grid2x2;
    private List<IntPtr> _windowHandles = new();
    private int _focusedWindowIndex = 0;
    private Func<List<IntPtr>>? _getWindowHandles;
    private Action<IntPtr>? _onWindowFocused;
    private List<WindowTile> _currentTiles = new();
    private Dictionary<IntPtr, Rect> _thumbnailBounds = new(); // Maps window handle to thumbnail bounds
    private Dictionary<IntPtr, Rect> _actualBounds = new(); // Maps window handle to actual 16:9 bounds

    public event EventHandler? LayoutChanged;

    public TilingWindow()
    {
        InitializeComponent();
        _thumbnailManager = new DWMThumbnailManager();
    }

    /// <summary>
    /// Sets the window handle provider function
    /// </summary>
    public void SetWindowHandleProvider(Func<List<IntPtr>> getWindowHandles, Action<IntPtr> onWindowFocused)
    {
        _getWindowHandles = getWindowHandles;
        _onWindowFocused = onWindowFocused;
    }

    /// <summary>
    /// Sets the current layout
    /// </summary>
    public void SetLayout(TilingLayout layout)
    {
        _currentLayout = layout;
        UpdateLayoutButtonStyles();
        RefreshLayout();
    }

    /// <summary>
    /// Cycles to the next window (for hotkey support)
    /// </summary>
    public void CycleNextWindow()
    {
        if (_windowHandles.Count == 0) return;

        _focusedWindowIndex = (_focusedWindowIndex + 1) % _windowHandles.Count;
        SetActiveWindow(_windowHandles[_focusedWindowIndex]);
    }

    /// <summary>
    /// Cycles to the previous window
    /// </summary>
    public void CyclePreviousWindow()
    {
        if (_windowHandles.Count == 0) return;

        _focusedWindowIndex = (_focusedWindowIndex - 1 + _windowHandles.Count) % _windowHandles.Count;
        SetActiveWindow(_windowHandles[_focusedWindowIndex]);
    }

    /// <summary>
    /// Sets a specific window as active by index (for number key support 1-9)
    /// </summary>
    public void FocusWindowByIndex(int index)
    {
        if (index < 0 || index >= _windowHandles.Count) return;

        _focusedWindowIndex = index;
        SetActiveWindow(_windowHandles[_focusedWindowIndex]);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _thumbnailManager.Initialize(this);
            UpdateLayoutButtonStyles();
            RefreshLayout();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to initialize tiling window: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        // Clean up thumbnails
        _thumbnailManager?.Dispose();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        // Handle Ctrl+1 through Ctrl+9 for direct window selection
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            // Check for number keys (both main keyboard and numpad)
            int windowIndex = -1;

            if (e.Key >= Key.D1 && e.Key <= Key.D9)
            {
                windowIndex = (int)(e.Key - Key.D1);
            }
            else if (e.Key >= Key.NumPad1 && e.Key <= Key.NumPad9)
            {
                windowIndex = (int)(e.Key - Key.NumPad1);
            }

            if (windowIndex >= 0)
            {
                FocusWindowByIndex(windowIndex);
                e.Handled = true;
                return;
            }
        }

        // Forward all other keyboard input to the focused game window
        ForwardKeyboardInput(e, isKeyDown: true);
        e.Handled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        // Forward key up events to focused window
        ForwardKeyboardInput(e, isKeyDown: false);
        e.Handled = true;
    }

    private void ForwardKeyboardInput(KeyEventArgs e, bool isKeyDown)
    {
        // Get the currently focused window
        if (_focusedWindowIndex < 0 || _focusedWindowIndex >= _windowHandles.Count)
            return;

        var windowHandle = _windowHandles[_focusedWindowIndex];
        if (windowHandle == IntPtr.Zero || !InputHookInterop.IsWindow(windowHandle))
            return;

        // Convert WPF key to virtual key code
        int virtualKey = KeyInterop.VirtualKeyFromKey(e.Key);
        if (virtualKey == 0)
            return;

        // Get scan code
        uint scanCode = InputHookInterop.MapVirtualKey((uint)virtualKey, 0);

        // Build lParam
        int lParam;
        if (isKeyDown)
        {
            lParam = 1 | ((int)scanCode << 16);
        }
        else
        {
            lParam = 1 | ((int)scanCode << 16) | (1 << 30) | (1 << 31);
        }

        // Send key message
        uint message = isKeyDown ? InputHookInterop.WM_KEYDOWN : InputHookInterop.WM_KEYUP;
        InputHookInterop.PostMessage(windowHandle, message, (IntPtr)virtualKey, (IntPtr)lParam);

        Debug.WriteLine($"Forwarded key {e.Key} ({virtualKey:X}) to window {_focusedWindowIndex} ({(isKeyDown ? "DOWN" : "UP")})");
    }

    private void LayoutButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string layoutTag)
        {
            if (Enum.TryParse<TilingLayout>(layoutTag, out var layout))
            {
                SetLayout(layout);
                LayoutChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    /// <summary>
    /// Updates the visual state of layout buttons
    /// </summary>
    private void UpdateLayoutButtonStyles()
    {
        // Reset all buttons to normal style
        BtnSingle.Style = (Style)FindResource("LayoutButtonStyle");
        BtnSideBySide.Style = (Style)FindResource("LayoutButtonStyle");
        BtnGrid2x2.Style = (Style)FindResource("LayoutButtonStyle");
        BtnThreeColumn.Style = (Style)FindResource("LayoutButtonStyle");
        BtnPrimarySecondary.Style = (Style)FindResource("LayoutButtonStyle");
        BtnSidebar.Style = (Style)FindResource("LayoutButtonStyle");

        // Set active button style
        var activeButton = _currentLayout switch
        {
            TilingLayout.Single => BtnSingle,
            TilingLayout.SideBySide => BtnSideBySide,
            TilingLayout.Grid2x2 => BtnGrid2x2,
            TilingLayout.ThreeColumn => BtnThreeColumn,
            TilingLayout.PrimarySecondary => BtnPrimarySecondary,
            TilingLayout.Sidebar => BtnSidebar,
            _ => BtnGrid2x2
        };

        activeButton.Style = (Style)FindResource("ActiveLayoutButtonStyle");
    }

    /// <summary>
    /// Refreshes the layout and thumbnails
    /// </summary>
    public void RefreshLayout()
    {
        try
        {
            // Get current window handles
            _windowHandles = _getWindowHandles?.Invoke() ?? new List<IntPtr>();

            // Update window count
            TxtWindowCount.Text = $"{_windowHandles.Count} Window{(_windowHandles.Count != 1 ? "s" : "")}";

            // Show empty state if no windows
            if (_windowHandles.Count == 0)
            {
                EmptyStatePanel.Visibility = Visibility.Visible;
                OverlayCanvas.Children.Clear();
                _thumbnailManager.UnregisterAllThumbnails();
                return;
            }

            EmptyStatePanel.Visibility = Visibility.Collapsed;

            // Clamp focused index
            _focusedWindowIndex = Math.Max(0, Math.Min(_focusedWindowIndex, _windowHandles.Count - 1));

            // Clean up invalid thumbnails
            _thumbnailManager.CleanupInvalidThumbnails();

            // Calculate layout
            var containerSize = new Size(ThumbnailContainer.ActualWidth, ThumbnailContainer.ActualHeight);
            if (containerSize.Width <= 0 || containerSize.Height <= 0)
            {
                return; // Window not ready yet
            }

            var tiles = LayoutEngine.CalculateLayout(_currentLayout, containerSize, _windowHandles, _focusedWindowIndex);

            // Update thumbnails
            UpdateThumbnails(tiles);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error refreshing layout: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates or creates thumbnails for all tiles
    /// </summary>
    private void UpdateThumbnails(List<WindowTile> tiles)
    {
        // Clear overlay and tracking
        OverlayCanvas.Children.Clear();
        _currentTiles = tiles;
        _thumbnailBounds.Clear();
        _actualBounds.Clear();

        foreach (var tile in tiles)
        {
            // Register or get existing thumbnail
            var thumbnailHandle = _thumbnailManager.GetThumbnailHandle(tile.WindowHandle);
            if (thumbnailHandle == IntPtr.Zero)
            {
                thumbnailHandle = _thumbnailManager.RegisterThumbnail(tile.WindowHandle);
            }

            if (thumbnailHandle != IntPtr.Zero)
            {
                // Calculate 16:9 letterboxed bounds
                var aspectRatioBounds = CalculateAspectRatioBounds(tile.Bounds, 16.0 / 9.0);

                // Store bounds for input forwarding
                _thumbnailBounds[tile.WindowHandle] = tile.Bounds; // Original tile bounds
                _actualBounds[tile.WindowHandle] = aspectRatioBounds; // 16:9 bounds where thumbnail actually renders

                // Update thumbnail position and size
                _thumbnailManager.UpdateThumbnail(
                    thumbnailHandle,
                    aspectRatioBounds,
                    opacity: 255,
                    visible: true,
                    sourceClientAreaOnly: true
                );

                // Add overlay elements (badges, borders) using original tile bounds
                AddOverlayElements(tile);
            }
        }
    }

    /// <summary>
    /// Calculates letterboxed bounds to maintain a specific aspect ratio
    /// </summary>
    private Rect CalculateAspectRatioBounds(Rect originalBounds, double targetAspectRatio)
    {
        double containerWidth = originalBounds.Width;
        double containerHeight = originalBounds.Height;
        double containerAspectRatio = containerWidth / containerHeight;

        double newWidth, newHeight;

        if (containerAspectRatio > targetAspectRatio)
        {
            // Container is wider than target - letterbox horizontally
            newHeight = containerHeight;
            newWidth = newHeight * targetAspectRatio;
        }
        else
        {
            // Container is taller than target - letterbox vertically
            newWidth = containerWidth;
            newHeight = newWidth / targetAspectRatio;
        }

        // Center the content
        double offsetX = originalBounds.Left + (containerWidth - newWidth) / 2;
        double offsetY = originalBounds.Top + (containerHeight - newHeight) / 2;

        return new Rect(offsetX, offsetY, newWidth, newHeight);
    }

    /// <summary>
    /// Adds visual overlay elements (badges, focus borders) for a tile
    /// </summary>
    private void AddOverlayElements(WindowTile tile)
    {
        // Create container for this tile's overlays
        var container = new Canvas
        {
            Width = tile.Bounds.Width,
            Height = tile.Bounds.Height
        };
        Canvas.SetLeft(container, tile.Bounds.Left);
        Canvas.SetTop(container, tile.Bounds.Top);

        // Add focus border if this window is focused
        if (tile.IsFocused)
        {
            var focusBorder = new Border
            {
                Width = tile.Bounds.Width,
                Height = tile.Bounds.Height,
                Style = (Style)FindResource("FocusBorderStyle"),
                IsHitTestVisible = false
            };
            container.Children.Add(focusBorder);
        }

        // Add window number badge
        var badge = new Border
        {
            Style = (Style)FindResource("WindowBadgeStyle")
        };

        var badgeText = new TextBlock
        {
            Text = (tile.Index + 1).ToString(),
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x2e))
        };

        badge.Child = badgeText;
        container.Children.Add(badge);

        // Add input forwarding area (transparent overlay)
        var inputArea = new Rectangle
        {
            Width = tile.Bounds.Width,
            Height = tile.Bounds.Height,
            Fill = Brushes.Transparent,
            Cursor = Cursors.Arrow,
            Tag = tile.WindowHandle // Store window handle for input forwarding
        };

        // Handle all mouse events for input forwarding
        inputArea.MouseDown += TileInputArea_MouseDown;
        inputArea.MouseUp += TileInputArea_MouseUp;
        inputArea.MouseMove += TileInputArea_MouseMove;
        inputArea.MouseWheel += TileInputArea_MouseWheel;
        inputArea.MouseEnter += TileInputArea_MouseEnter;

        container.Children.Add(inputArea);

        OverlayCanvas.Children.Add(container);
    }

    private void TileInputArea_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Rectangle rect && rect.Tag is IntPtr windowHandle)
        {
            // Set this as the focused window for keyboard input
            var tile = _currentTiles.FirstOrDefault(t => t.WindowHandle == windowHandle);
            if (tile != null)
            {
                _focusedWindowIndex = tile.Index;
                RefreshLayout(); // Update visual focus indicator
            }
        }
    }

    private void TileInputArea_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Rectangle rect && rect.Tag is IntPtr windowHandle)
        {
            var position = e.GetPosition(rect);
            ForwardMouseInput(windowHandle, position, GetMouseMessage(e, isDown: true), e);
            e.Handled = true;
        }
    }

    private void TileInputArea_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Rectangle rect && rect.Tag is IntPtr windowHandle)
        {
            var position = e.GetPosition(rect);
            ForwardMouseInput(windowHandle, position, GetMouseMessage(e, isDown: false), e);
            e.Handled = true;
        }
    }

    private void TileInputArea_MouseMove(object sender, MouseEventArgs e)
    {
        // Don't forward mouse move - causes spam and most games don't need it for thumbnails
        // Only the MouseEnter sets the focused window for keyboard input
    }

    private void TileInputArea_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is Rectangle rect && rect.Tag is IntPtr windowHandle)
        {
            var position = e.GetPosition(rect);
            ForwardMouseWheel(windowHandle, position, e.Delta);
            e.Handled = true;
        }
    }

    private uint GetMouseMessage(MouseButtonEventArgs e, bool isDown)
    {
        if (e.ChangedButton == MouseButton.Left)
            return isDown ? InputHookInterop.WM_LBUTTONDOWN : InputHookInterop.WM_LBUTTONUP;
        else if (e.ChangedButton == MouseButton.Right)
            return isDown ? InputHookInterop.WM_RBUTTONDOWN : InputHookInterop.WM_RBUTTONUP;
        else if (e.ChangedButton == MouseButton.Middle)
            return isDown ? InputHookInterop.WM_MBUTTONDOWN : InputHookInterop.WM_MBUTTONUP;

        return InputHookInterop.WM_LBUTTONDOWN;
    }

    private void ForwardMouseInput(IntPtr windowHandle, Point thumbnailPosition, uint message, MouseEventArgs e)
    {
        if (!_actualBounds.ContainsKey(windowHandle) || !_thumbnailBounds.ContainsKey(windowHandle))
        {
            Debug.WriteLine($"[MOUSE] Forward failed: window {windowHandle} not in bounds dictionary");
            return;
        }

        // Get the actual thumbnail bounds (16:9 letterboxed)
        var actualBounds = _actualBounds[windowHandle];
        var tileBounds = _thumbnailBounds[windowHandle];

        Debug.WriteLine($"[MOUSE] ===== Mouse Input Forwarding =====");
        Debug.WriteLine($"[MOUSE] Tile bounds: {tileBounds}");
        Debug.WriteLine($"[MOUSE] Actual bounds: {actualBounds}");
        Debug.WriteLine($"[MOUSE] Thumbnail position: {thumbnailPosition}");

        // Transform from tile coordinates to thumbnail coordinates
        double relativeX = thumbnailPosition.X - (actualBounds.Left - tileBounds.Left);
        double relativeY = thumbnailPosition.Y - (actualBounds.Top - tileBounds.Top);

        Debug.WriteLine($"[MOUSE] Relative position in thumbnail: ({relativeX}, {relativeY})");

        // Check if click is within the actual thumbnail (not letterbox area)
        if (relativeX < 0 || relativeY < 0 || relativeX > actualBounds.Width || relativeY > actualBounds.Height)
        {
            Debug.WriteLine($"[MOUSE] Click REJECTED - outside thumbnail bounds (in letterbox area)");
            return; // Click was in letterbox area
        }

        // Get the actual window client size
        if (!DwmInterop.GetClientRect(windowHandle, out var clientRect))
        {
            Debug.WriteLine($"[MOUSE] ERROR: GetClientRect failed for window {windowHandle}");
            return;
        }

        int windowWidth = clientRect.Width;
        int windowHeight = clientRect.Height;

        Debug.WriteLine($"[MOUSE] Window client size: {windowWidth}x{windowHeight}");

        // Transform from thumbnail coordinates to window coordinates
        int windowX = (int)(relativeX / actualBounds.Width * windowWidth);
        int windowY = (int)(relativeY / actualBounds.Height * windowHeight);

        // Clamp to window bounds
        windowX = Math.Max(0, Math.Min(windowX, windowWidth - 1));
        windowY = Math.Max(0, Math.Min(windowY, windowHeight - 1));

        Debug.WriteLine($"[MOUSE] Target window client position: ({windowX}, {windowY})");

        // Convert client coordinates to screen coordinates
        var point = new InputHookInterop.POINT { x = windowX, y = windowY };
        if (!InputHookInterop.ClientToScreen(windowHandle, ref point))
        {
            Debug.WriteLine($"[MOUSE] ERROR: ClientToScreen failed");
            return;
        }

        Debug.WriteLine($"[MOUSE] Target screen position: ({point.x}, {point.y})");

        // Attempt 1: Try SendInput (modern API, most reliable)
        bool success = TrySendInputMethod(windowHandle, point, message);

        if (success)
        {
            Debug.WriteLine($"[MOUSE] ✓ SendInput method succeeded");
            return;
        }

        Debug.WriteLine($"[MOUSE] ⚠ SendInput method failed, trying PostMessage fallback");

        // Attempt 2: Fallback to PostMessage (might work for some games)
        TryPostMessageMethod(windowHandle, windowX, windowY, message, e);
    }

    private bool TrySendInputMethod(IntPtr windowHandle, InputHookInterop.POINT screenPoint, uint message)
    {
        try
        {
            // Move cursor to the target position
            if (!InputHookInterop.SetCursorPos(screenPoint.x, screenPoint.y))
            {
                Debug.WriteLine($"[MOUSE] ERROR: SetCursorPos failed");
                return false;
            }

            // Small delay to let cursor position update
            System.Threading.Thread.Sleep(5);

            // Prepare SendInput structure
            var input = new InputHookInterop.INPUT
            {
                type = InputHookInterop.INPUT_MOUSE,
                u = new InputHookInterop.InputUnion
                {
                    mi = new InputHookInterop.MOUSEINPUT
                    {
                        dx = 0,
                        dy = 0,
                        mouseData = 0,
                        dwFlags = GetSendInputFlags(message),
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };

            var inputs = new InputHookInterop.INPUT[] { input };
            uint result = InputHookInterop.SendInput(1, inputs, Marshal.SizeOf(typeof(InputHookInterop.INPUT)));

            if (result == 0)
            {
                int error = Marshal.GetLastWin32Error();
                Debug.WriteLine($"[MOUSE] ERROR: SendInput failed with error code {error}");
                return false;
            }

            Debug.WriteLine($"[MOUSE] SendInput sent: {GetMessageName(message)}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MOUSE] EXCEPTION in SendInput: {ex.Message}");
            return false;
        }
    }

    private void TryPostMessageMethod(IntPtr windowHandle, int clientX, int clientY, uint message, MouseEventArgs e)
    {
        try
        {
            int lParam = MakeLParam(clientX, clientY);
            int wParam = GetMouseWParam(e);

            bool result = InputHookInterop.PostMessage(windowHandle, message, (IntPtr)wParam, (IntPtr)lParam);

            if (result)
            {
                Debug.WriteLine($"[MOUSE] ✓ PostMessage sent: {GetMessageName(message)} at ({clientX}, {clientY})");
            }
            else
            {
                Debug.WriteLine($"[MOUSE] ERROR: PostMessage failed");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MOUSE] EXCEPTION in PostMessage: {ex.Message}");
        }
    }

    private uint GetSendInputFlags(uint message)
    {
        return message switch
        {
            InputHookInterop.WM_LBUTTONDOWN => InputHookInterop.MOUSEEVENTF_LEFTDOWN,
            InputHookInterop.WM_LBUTTONUP => InputHookInterop.MOUSEEVENTF_LEFTUP,
            InputHookInterop.WM_RBUTTONDOWN => InputHookInterop.MOUSEEVENTF_RIGHTDOWN,
            InputHookInterop.WM_RBUTTONUP => InputHookInterop.MOUSEEVENTF_RIGHTUP,
            InputHookInterop.WM_MBUTTONDOWN => InputHookInterop.MOUSEEVENTF_MIDDLEDOWN,
            InputHookInterop.WM_MBUTTONUP => InputHookInterop.MOUSEEVENTF_MIDDLEUP,
            _ => 0
        };
    }

    private string GetMessageName(uint message)
    {
        return message switch
        {
            InputHookInterop.WM_LBUTTONDOWN => "WM_LBUTTONDOWN",
            InputHookInterop.WM_LBUTTONUP => "WM_LBUTTONUP",
            InputHookInterop.WM_RBUTTONDOWN => "WM_RBUTTONDOWN",
            InputHookInterop.WM_RBUTTONUP => "WM_RBUTTONUP",
            InputHookInterop.WM_MBUTTONDOWN => "WM_MBUTTONDOWN",
            InputHookInterop.WM_MBUTTONUP => "WM_MBUTTONUP",
            _ => $"UNKNOWN(0x{message:X})"
        };
    }

    private void ForwardMouseWheel(IntPtr windowHandle, Point thumbnailPosition, int delta)
    {
        if (!_actualBounds.ContainsKey(windowHandle))
            return;

        var actualBounds = _actualBounds[windowHandle];

        // Transform coordinates (same as ForwardMouseInput)
        double relativeX = thumbnailPosition.X - (actualBounds.Left - _thumbnailBounds[windowHandle].Left);
        double relativeY = thumbnailPosition.Y - (actualBounds.Top - _thumbnailBounds[windowHandle].Top);

        if (!DwmInterop.GetClientRect(windowHandle, out var clientRect))
            return;

        int windowX = (int)(relativeX / actualBounds.Width * clientRect.Width);
        int windowY = (int)(relativeY / actualBounds.Height * clientRect.Height);

        int lParam = MakeLParam(windowX, windowY);
        int wParam = MakeWheelWParam(delta);

        InputHookInterop.PostMessage(windowHandle, InputHookInterop.WM_MOUSEWHEEL, (IntPtr)wParam, (IntPtr)lParam);
    }

    private int GetMouseWParam(MouseEventArgs e)
    {
        int wParam = 0;

        if (e.LeftButton == MouseButtonState.Pressed)
            wParam |= 0x0001; // MK_LBUTTON
        if (e.RightButton == MouseButtonState.Pressed)
            wParam |= 0x0002; // MK_RBUTTON
        if (e.MiddleButton == MouseButtonState.Pressed)
            wParam |= 0x0010; // MK_MBUTTON
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            wParam |= 0x0004; // MK_SHIFT
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            wParam |= 0x0008; // MK_CONTROL

        return wParam;
    }

    private int MakeLParam(int x, int y)
    {
        return (y << 16) | (x & 0xFFFF);
    }

    private int MakeWheelWParam(int delta)
    {
        return (delta << 16);
    }

    private void OverlayCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Mouse down on overlay canvas - do nothing (clicks are handled by tile areas)
    }

    /// <summary>
    /// Sets a window as the active target for keyboard input (doesn't actually focus it)
    /// </summary>
    private void SetActiveWindow(IntPtr windowHandle)
    {
        // Find the index of this window
        var index = _windowHandles.IndexOf(windowHandle);
        if (index >= 0)
        {
            _focusedWindowIndex = index;
            RefreshLayout(); // Update visual indicator

            // Notify parent for audio management
            _onWindowFocused?.Invoke(windowHandle);
        }
    }

    /// <summary>
    /// Handles window resize
    /// </summary>
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        RefreshLayout();
    }

    /// <summary>
    /// Gets the current layout
    /// </summary>
    public TilingLayout CurrentLayout => _currentLayout;
}
