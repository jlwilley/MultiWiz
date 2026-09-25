namespace MultiWiz.Core.Platform;

/// <summary>Posts keyboard input to a specific window without requiring it to be focused.</summary>
public interface IInputSender
{
    /// <summary>Posts each character as WM_CHAR, waiting <paramref name="perCharacterDelay"/> between them.</summary>
    Task SendTextAsync(nint hwnd, string text, TimeSpan perCharacterDelay, CancellationToken cancellationToken = default);

    /// <summary>Posts WM_KEYDOWN/WM_KEYUP for a virtual key (e.g. 0x09 Tab, 0x0D Enter) with a correct lParam.</summary>
    Task SendKeyAsync(nint hwnd, int virtualKey, CancellationToken cancellationToken = default);
}
