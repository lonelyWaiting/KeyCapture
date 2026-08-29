using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KeyCapture.Interop;

/// <summary>
/// System-wide low-level mouse hook that reports left-button presses and marks the
/// second press of a double click. A WH_MOUSE_LL hook never receives WM_LBUTTONDBLCLK
/// (double clicks are synthesised further up the message pipeline), so the click
/// pairing is done here using the system double-click time and slop rectangle.
/// </summary>
internal sealed class MouseHookManager : IDisposable
{
    private IntPtr _hookId = IntPtr.Zero;
    private readonly NativeMethods.LowLevelMouseProc _hookProc;

    private uint _lastDownTime;
    private NativeMethods.POINT _lastDownPoint;

    /// <summary>Raised for every left-button press; the flag marks the second click of a pair.</summary>
    public event Action<NativeMethods.POINT, bool>? LeftButtonDown;

    public MouseHookManager()
    {
        // Keep the delegate rooted: native code holds the pointer for the hook's lifetime.
        _hookProc = HookCallback;
    }

    public bool IsInstalled => _hookId != IntPtr.Zero;

    public void Install()
    {
        if (_hookId != IntPtr.Zero)
            return;

        _hookId = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_MOUSE_LL,
            _hookProc,
            NativeMethods.GetModuleHandle(null),
            0);

        if (_hookId == IntPtr.Zero)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // This runs for every mouse message in the system, including the flood of
        // WM_MOUSEMOVE, so bail out before doing any marshalling for anything else.
        if (nCode >= 0 && wParam == NativeMethods.WM_LBUTTONDOWN)
        {
            try
            {
                var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                bool isDoubleClick = IsSecondClickOfPair(data.pt, data.time);
                LeftButtonDown?.Invoke(data.pt, isDoubleClick);
            }
            catch (Exception ex)
            {
                // Never throw out of a hook callback.
                Debug.WriteLine($"Mouse hook error: {ex}");
            }
        }

        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private bool IsSecondClickOfPair(NativeMethods.POINT point, uint time)
    {
        int slopX = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXDOUBLECLK) / 2;
        int slopY = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYDOUBLECLK) / 2;

        bool isDouble = _lastDownTime != 0
            && time - _lastDownTime <= NativeMethods.GetDoubleClickTime()
            && Math.Abs(point.X - _lastDownPoint.X) <= slopX
            && Math.Abs(point.Y - _lastDownPoint.Y) <= slopY;

        // Reset after a pair so a triple click does not produce a second double click.
        _lastDownTime = isDouble ? 0 : time;
        _lastDownPoint = point;
        return isDouble;
    }

    public void Uninstall()
    {
        if (_hookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }

        _lastDownTime = 0;
    }

    public void Dispose() => Uninstall();
}
