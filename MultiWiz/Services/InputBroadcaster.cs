using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using MultiWiz.Win32;

namespace MultiWiz.Services;

/// <summary>
/// Service that broadcasts keyboard and mouse input to multiple game windows simultaneously
/// </summary>
public class InputBroadcaster : IDisposable
{
    private IntPtr _keyboardHookHandle = IntPtr.Zero;
    private IntPtr _mouseHookHandle = IntPtr.Zero;
    private InputHookInterop.LowLevelHookProc? _keyboardHookProc;
    private InputHookInterop.LowLevelHookProc? _mouseHookProc;
    private bool _isBroadcastEnabled = false;
    private readonly object _lockObject = new object();
    private Func<IEnumerable<IntPtr>>? _getTargetWindows;

    public event EventHandler<bool>? BroadcastStateChanged;

    /// <summary>
    /// Gets whether broadcast mode is currently enabled
    /// </summary>
    public bool IsBroadcastEnabled
    {
        get => _isBroadcastEnabled;
        private set
        {
            if (_isBroadcastEnabled != value)
            {
                _isBroadcastEnabled = value;
                BroadcastStateChanged?.Invoke(this, value);
            }
        }
    }

    /// <summary>
    /// Initializes the input broadcaster
    /// </summary>
    /// <param name="getTargetWindows">Function that returns the list of window handles to broadcast to</param>
    public InputBroadcaster(Func<IEnumerable<IntPtr>> getTargetWindows)
    {
        _getTargetWindows = getTargetWindows;
    }

    /// <summary>
    /// Starts the input hooks (but doesn't enable broadcasting)
    /// </summary>
    public void Initialize()
    {
        if (_keyboardHookHandle != IntPtr.Zero || _mouseHookHandle != IntPtr.Zero)
        {
            Debug.WriteLine("Input hooks already initialized");
            return;
        }

        try
        {
            // Store delegates to prevent garbage collection
            _keyboardHookProc = KeyboardHookCallback;
            _mouseHookProc = MouseHookCallback;

            using var currentProcess = Process.GetCurrentProcess();
            using var currentModule = currentProcess.MainModule;

            if (currentModule != null)
            {
                var moduleHandle = InputHookInterop.GetModuleHandle(currentModule.ModuleName);

                // Install keyboard hook
                _keyboardHookHandle = InputHookInterop.SetWindowsHookEx(
                    InputHookInterop.WH_KEYBOARD_LL,
                    _keyboardHookProc,
                    moduleHandle,
                    0
                );

                // Install mouse hook
                _mouseHookHandle = InputHookInterop.SetWindowsHookEx(
                    InputHookInterop.WH_MOUSE_LL,
                    _mouseHookProc,
                    moduleHandle,
                    0
                );

                if (_keyboardHookHandle == IntPtr.Zero || _mouseHookHandle == IntPtr.Zero)
                {
                    var error = Marshal.GetLastWin32Error();
                    throw new Exception($"Failed to install input hooks. Error code: {error}");
                }

                Debug.WriteLine("Input hooks installed successfully");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error initializing input hooks: {ex.Message}");
            Cleanup();
            throw;
        }
    }

    /// <summary>
    /// Toggles broadcast mode on/off
    /// </summary>
    public void ToggleBroadcast()
    {
        lock (_lockObject)
        {
            IsBroadcastEnabled = !IsBroadcastEnabled;
            Debug.WriteLine($"Broadcast mode {(IsBroadcastEnabled ? "ENABLED" : "DISABLED")}");
        }
    }

    /// <summary>
    /// Enables broadcast mode
    /// </summary>
    public void EnableBroadcast()
    {
        lock (_lockObject)
        {
            if (!IsBroadcastEnabled)
            {
                IsBroadcastEnabled = true;
                Debug.WriteLine("Broadcast mode ENABLED");
            }
        }
    }

    /// <summary>
    /// Disables broadcast mode
    /// </summary>
    public void DisableBroadcast()
    {
        lock (_lockObject)
        {
            if (IsBroadcastEnabled)
            {
                IsBroadcastEnabled = false;
                Debug.WriteLine("Broadcast mode DISABLED");
            }
        }
    }

    /// <summary>
    /// Keyboard hook callback - intercepts all keyboard input
    /// </summary>
    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= InputHookInterop.HC_ACTION && IsBroadcastEnabled)
            {
                var hookStruct = Marshal.PtrToStructure<InputHookInterop.KBDLLHOOKSTRUCT>(lParam);
                var targetWindows = _getTargetWindows?.Invoke()?.ToList() ?? new List<IntPtr>();

                // Only broadcast if we have multiple targets and not in MultiWiz window
                if (targetWindows.Count > 0)
                {
                    var foregroundWindow = InputHookInterop.GetForegroundWindow();
                    var currentProcessId = Process.GetCurrentProcess().Id;

                    // Check if foreground window belongs to MultiWiz - if so, don't broadcast
                    if (!IsMultiWizWindow(foregroundWindow))
                    {
                        BroadcastKeyboardInput(targetWindows, wParam, hookStruct);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in keyboard hook: {ex.Message}");
        }

        // Always call next hook
        return InputHookInterop.CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
    }

    /// <summary>
    /// Mouse hook callback - intercepts all mouse input
    /// </summary>
    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= InputHookInterop.HC_ACTION && IsBroadcastEnabled)
            {
                var hookStruct = Marshal.PtrToStructure<InputHookInterop.MSLLHOOKSTRUCT>(lParam);
                var targetWindows = _getTargetWindows?.Invoke()?.ToList() ?? new List<IntPtr>();

                // Only broadcast if we have multiple targets
                if (targetWindows.Count > 0)
                {
                    var foregroundWindow = InputHookInterop.GetForegroundWindow();

                    // Check if foreground window belongs to MultiWiz - if so, don't broadcast
                    if (!IsMultiWizWindow(foregroundWindow))
                    {
                        BroadcastMouseInput(targetWindows, wParam, hookStruct);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in mouse hook: {ex.Message}");
        }

        // Always call next hook
        return InputHookInterop.CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
    }

    /// <summary>
    /// Broadcasts keyboard input to all target windows
    /// </summary>
    private void BroadcastKeyboardInput(List<IntPtr> targetWindows, IntPtr wParam, InputHookInterop.KBDLLHOOKSTRUCT hookStruct)
    {
        uint message = (uint)wParam;
        var vkCode = (int)hookStruct.vkCode;

        // Build lParam for key messages
        uint scanCode = InputHookInterop.MapVirtualKey(hookStruct.vkCode, 0);
        int lParamValue;

        if (message == InputHookInterop.WM_KEYDOWN || message == InputHookInterop.WM_SYSKEYDOWN)
        {
            lParamValue = 1 | ((int)scanCode << 16);
        }
        else // KEYUP or SYSKEYUP
        {
            lParamValue = 1 | ((int)scanCode << 16) | (1 << 30) | (1 << 31);
        }

        // Broadcast to all target windows
        foreach (var hwnd in targetWindows)
        {
            if (InputHookInterop.IsWindow(hwnd))
            {
                InputHookInterop.PostMessage(hwnd, message, (IntPtr)vkCode, new IntPtr(lParamValue));
            }
        }
    }

    /// <summary>
    /// Broadcasts mouse input to all target windows
    /// </summary>
    private void BroadcastMouseInput(List<IntPtr> targetWindows, IntPtr wParam, InputHookInterop.MSLLHOOKSTRUCT hookStruct)
    {
        uint message = (uint)wParam;

        // For now, we'll broadcast clicks but not movement (movement would be confusing across different window positions)
        // You can enable movement broadcasting by removing this filter
        if (message == InputHookInterop.WM_MOUSEMOVE)
        {
            return; // Skip mouse movement
        }

        // Broadcast to all target windows
        foreach (var hwnd in targetWindows)
        {
            if (InputHookInterop.IsWindow(hwnd))
            {
                // Convert screen coordinates to window-relative coordinates
                int lParam = MakeLParam(hookStruct.pt.x, hookStruct.pt.y);
                int wParamValue = (int)(hookStruct.mouseData >> 16); // For wheel delta

                InputHookInterop.PostMessage(hwnd, message, (IntPtr)wParamValue, (IntPtr)lParam);
            }
        }
    }

    /// <summary>
    /// Checks if a window handle belongs to MultiWiz
    /// </summary>
    private bool IsMultiWizWindow(IntPtr hwnd)
    {
        try
        {
            // Get window process ID
            GetWindowThreadProcessId(hwnd, out uint processId);
            var currentProcessId = Process.GetCurrentProcess().Id;
            return processId == currentProcessId;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    /// <summary>
    /// Creates an lParam value from x and y coordinates
    /// </summary>
    private int MakeLParam(int x, int y)
    {
        return (y << 16) | (x & 0xFFFF);
    }

    /// <summary>
    /// Cleanup hooks
    /// </summary>
    private void Cleanup()
    {
        if (_keyboardHookHandle != IntPtr.Zero)
        {
            InputHookInterop.UnhookWindowsHookEx(_keyboardHookHandle);
            _keyboardHookHandle = IntPtr.Zero;
            Debug.WriteLine("Keyboard hook removed");
        }

        if (_mouseHookHandle != IntPtr.Zero)
        {
            InputHookInterop.UnhookWindowsHookEx(_mouseHookHandle);
            _mouseHookHandle = IntPtr.Zero;
            Debug.WriteLine("Mouse hook removed");
        }
    }

    /// <summary>
    /// Disposes the input broadcaster and removes hooks
    /// </summary>
    public void Dispose()
    {
        DisableBroadcast();
        Cleanup();
        GC.SuppressFinalize(this);
    }

    ~InputBroadcaster()
    {
        Cleanup();
    }
}
