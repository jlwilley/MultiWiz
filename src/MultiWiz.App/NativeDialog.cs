using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MultiWiz.App;

/// <summary>
/// Plain Win32 message boxes for the moments when Avalonia cannot show a dialog: before the app has started, after it
/// crashed, or in a second process that is about to exit. Blocks until the user closes the box.
/// </summary>
internal static class NativeDialog
{
    private const string Caption = "MultiWiz";

    public static void ShowError(string text) =>
        Show(text, MESSAGEBOX_STYLE.MB_OK | MESSAGEBOX_STYLE.MB_ICONERROR | MESSAGEBOX_STYLE.MB_SETFOREGROUND);

    public static void ShowInformation(string text) =>
        Show(text, MESSAGEBOX_STYLE.MB_OK | MESSAGEBOX_STYLE.MB_ICONINFORMATION | MESSAGEBOX_STYLE.MB_SETFOREGROUND);

    private static void Show(string text, MESSAGEBOX_STYLE style) => _ = PInvoke.MessageBox(HWND.Null, text, Caption, style);
}
