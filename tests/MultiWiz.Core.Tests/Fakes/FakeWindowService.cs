using MultiWiz.Core.Platform;
using MultiWiz.Core.Primitives;

namespace MultiWiz.Core.Tests.Fakes;

/// <summary>Every process gets the window handle <c>pid * 16</c> once it has been polled <see cref="PollsBeforeWindow"/> times.</summary>
internal sealed class FakeWindowService : IWindowService
{
    private readonly Lock _lock = new();
    private readonly Dictionary<int, int> _pollsByProcess = new();
    private readonly List<nint> _focusCalls = [];
    private readonly List<(nint Window, PixelRect Bounds, bool Resize)> _setBoundsCalls = [];
    private nint _foreground;

    public int PollsBeforeWindow { get; set; } = 1;

    public bool WindowsNeverAppear { get; set; }

    public bool FocusSucceeds { get; set; } = true;

    public IReadOnlyList<nint> FocusCalls
    {
        get
        {
            lock (_lock)
            {
                return _focusCalls.ToArray();
            }
        }
    }

    public IReadOnlyList<(nint Window, PixelRect Bounds, bool Resize)> SetBoundsCalls
    {
        get
        {
            lock (_lock)
            {
                return _setBoundsCalls.ToArray();
            }
        }
    }

    public static nint WindowFor(int processId) => processId * 16;

    public nint FindMainWindow(int processId)
    {
        lock (_lock)
        {
            if (WindowsNeverAppear)
            {
                return 0;
            }

            var polls = _pollsByProcess.GetValueOrDefault(processId);
            _pollsByProcess[processId] = polls + 1;
            return polls >= PollsBeforeWindow ? WindowFor(processId) : 0;
        }
    }

    public bool IsWindowAlive(nint hwnd) => hwnd != 0;

    public int GetProcessId(nint hwnd) => (int)(hwnd / 16);

    public nint GetForegroundWindow()
    {
        lock (_lock)
        {
            return _foreground;
        }
    }

    public string GetTitle(nint hwnd) => "Wizard101";

    public bool IsMinimized(nint hwnd) => false;

    public bool Focus(nint hwnd)
    {
        lock (_lock)
        {
            _focusCalls.Add(hwnd);
            if (FocusSucceeds)
            {
                _foreground = hwnd;
            }

            return FocusSucceeds;
        }
    }

    public PixelRect? GetBounds(nint hwnd) => null;

    public PixelRect? GetClientBounds(nint hwnd) => null;

    public bool SetBounds(nint hwnd, PixelRect bounds, bool resize)
    {
        lock (_lock)
        {
            _setBoundsCalls.Add((hwnd, bounds, resize));
            return true;
        }
    }

    public bool SetBorderless(nint hwnd, bool borderless) => true;
}
