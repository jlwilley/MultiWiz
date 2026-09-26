using System.Runtime.InteropServices;
using MultiWiz.Core.Platform;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace MultiWiz.Platform.Windows.Input;

/// <summary>
/// Posts keyboard messages straight into a window's message queue, so typing works without the window
/// having focus and without touching the global keyboard state.
/// </summary>
internal sealed class InputSender : IInputSender
{
    private static readonly TimeSpan KeyHoldTime = TimeSpan.FromMilliseconds(30);

    // lParam bits for keyboard messages: repeat count in 0-15, scan code in 16-23, extended-key flag in 24,
    // previous key state in 30 and transition state in 31.
    private const uint RepeatCountOne = 1;
    private const uint ExtendedKeyFlag = 1u << 24;
    private const uint KeyUpFlags = (1u << 30) | (1u << 31);

    public async Task SendTextAsync(nint hwnd, string text, TimeSpan perCharacterDelay, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        EnsureWindow(hwnd);

        for (var i = 0; i < text.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (i > 0 && perCharacterDelay > TimeSpan.Zero)
            {
                await Task.Delay(perCharacterDelay, cancellationToken).ConfigureAwait(false);
            }

            // One WM_CHAR per UTF-16 code unit; surrogate pairs arrive as two messages, as with real typing.
            Post(hwnd, PInvoke.WM_CHAR, text[i], RepeatCountOne);
        }
    }

    public async Task SendKeyAsync(nint hwnd, int virtualKey, CancellationToken cancellationToken = default)
    {
        EnsureWindow(hwnd);
        cancellationToken.ThrowIfCancellationRequested();

        var scanCode = PInvoke.MapVirtualKey((uint)virtualKey, MAP_VIRTUAL_KEY_TYPE.MAPVK_VK_TO_VSC_EX);
        var keyData = RepeatCountOne | ((scanCode & 0xFF) << 16);
        if ((scanCode & 0xFF00) is 0xE000 or 0xE100)
        {
            keyData |= ExtendedKeyFlag;
        }

        Post(hwnd, PInvoke.WM_KEYDOWN, (uint)virtualKey, keyData);

        // Always release the key, even if cancellation is requested while it is held down.
        await Task.Delay(KeyHoldTime, CancellationToken.None).ConfigureAwait(false);
        Post(hwnd, PInvoke.WM_KEYUP, (uint)virtualKey, keyData | KeyUpFlags);
    }

    private static void EnsureWindow(nint hwnd)
    {
        if (!NativeWindowHelpers.IsAlive(hwnd))
        {
            throw new InvalidOperationException("The game window is no longer open.");
        }
    }

    private static void Post(nint hwnd, uint message, uint wParam, uint keyData)
    {
        // Keyboard lParam is a 32-bit value; sign-extend it the way the system does on 64-bit Windows.
        var lParam = (nint)unchecked((int)keyData);
        if (!PInvoke.PostMessage((HWND)hwnd, message, (nuint)wParam, lParam))
        {
            throw new InvalidOperationException(
                $"Could not send input to the game window (error {Marshal.GetLastPInvokeError()}).");
        }
    }
}
