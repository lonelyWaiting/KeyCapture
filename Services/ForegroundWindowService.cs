using System.Diagnostics;
using KeyCapture.Interop;

namespace KeyCapture.Services;

internal sealed class ForegroundWindowService
{
    private IntPtr _lastHwnd;
    private string _lastName = string.Empty;

    public string GetActiveApplicationName()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        return GetApplicationName(hwnd);
    }

    public string GetApplicationName(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return "Desktop";

        if (hwnd == _lastHwnd && !string.IsNullOrEmpty(_lastName))
            return _lastName;

        _lastHwnd = hwnd;

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        _lastName = GetProcessDisplayName(pid);
        return _lastName;
    }

    public static string GetProcessDisplayName(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);

            try
            {
                var description = process.MainModule?.FileVersionInfo.FileDescription;
                if (!string.IsNullOrWhiteSpace(description))
                    return Truncate(description);
            }
            catch
            {
                // Access denied for elevated processes
            }

            return process.ProcessName;
        }
        catch
        {
            return "Unknown";
        }
    }

    private static string Truncate(string value, int maxLength = 40)
    {
        if (value.Length <= maxLength) return value;
        return string.Concat(value.AsSpan(0, maxLength - 1), "\u2026");
    }
}
