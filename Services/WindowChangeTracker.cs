using System.Diagnostics;
using System.Windows.Threading;
using KeyCapture.Interop;

namespace KeyCapture.Services;

internal sealed class WindowChangeTracker : IDisposable
{
    private IntPtr _hookHandle = IntPtr.Zero;
    private readonly NativeMethods.WinEventDelegate _winEventProc;
    private readonly DispatcherTimer _trackingTimer;

    private bool _tracking;
    private IntPtr _originalHwnd;
    private string _originalAppName = string.Empty;
    private string _keyText = string.Empty;

    public event Action<string>? ForegroundSwitched;

    public WindowChangeTracker()
    {
        // Must keep delegate alive to prevent GC
        _winEventProc = OnWinEvent;

        _trackingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _trackingTimer.Tick += (_, _) => StopTracking();
    }

    public void Install()
    {
        _hookHandle = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _winEventProc,
            0, 0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);
    }

    public void BeginTracking(string keyText, string originalAppName)
    {
        _keyText = keyText;
        _originalAppName = originalAppName;
        _originalHwnd = NativeMethods.GetForegroundWindow();
        _tracking = true;

        _trackingTimer.Stop();
        _trackingTimer.Start();
    }

    private void StopTracking()
    {
        _tracking = false;
        _trackingTimer.Stop();
    }

    private void OnWinEvent(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (!_tracking || hwnd == IntPtr.Zero || hwnd == _originalHwnd)
            return;

        try
        {
            // A different window got focus after the key press
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            var newAppName = ForegroundWindowService.GetProcessDisplayName(pid);

            // Avoid showing our own overlay or duplicate of the same app
            if (newAppName == _originalAppName || newAppName == "KeyCapture")
                return;

            StopTracking();

            // Notify: show the chain
            ForegroundSwitched?.Invoke($"{_keyText} : {_originalAppName} \u2192 {newAppName}");
        }
        catch (Exception ex)
        {
            // Log exception but don't crash - WinEvent hook must not throw
            Debug.WriteLine($"Window change tracker error: {ex}");
            StopTracking();
        }
    }

    public void Dispose()
    {
        StopTracking();
        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }
}
