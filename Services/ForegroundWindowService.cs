using System.Diagnostics;
using System.IO;
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

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        _lastName = GetProcessDisplayName(pid);
        _lastHwnd = hwnd;
        return _lastName;
    }

    // Resolving a display name means reading the executable's version info, which is far too
    // expensive to repeat per keystroke or per foreground change, so results are cached by
    // process id. Entries expire because Windows recycles process ids.
    private static readonly Dictionary<uint, (string Name, long Timestamp)> NameCache = new();
    private static readonly Lock CacheLock = new();
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);
    private const int MaxCacheEntries = 256;

    public static string GetProcessDisplayName(uint pid)
    {
        long now = Environment.TickCount64;

        lock (CacheLock)
        {
            if (NameCache.TryGetValue(pid, out var cached)
                && now - cached.Timestamp < CacheLifetime.TotalMilliseconds)
            {
                return cached.Name;
            }
        }

        var name = ResolveProcessDisplayName(pid);

        lock (CacheLock)
        {
            if (NameCache.Count >= MaxCacheEntries)
                NameCache.Clear();
            NameCache[pid] = (name, now);
        }

        return name;
    }

    private static string ResolveProcessDisplayName(uint pid)
    {
        var imagePath = TryGetProcessImagePath(pid);
        if (imagePath is null)
            return "Unknown";

        try
        {
            var description = FileVersionInfo.GetVersionInfo(imagePath).FileDescription;
            if (!string.IsNullOrWhiteSpace(description))
                return Truncate(description);
        }
        catch
        {
            // Some executables have no readable version resource.
        }

        return Truncate(Path.GetFileNameWithoutExtension(imagePath));
    }

    /// <summary>
    /// Reads the executable path with a limited-information handle. Process.MainModule was
    /// used before, but it snapshots every loaded module and throws for elevated or
    /// cross-bitness processes, which made it both slow and exception-heavy.
    /// </summary>
    private static string? TryGetProcessImagePath(uint pid)
    {
        var handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero)
            return null;

        try
        {
            var buffer = new char[520];
            uint size = (uint)buffer.Length;
            return NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref size)
                ? new string(buffer, 0, (int)size)
                : null;
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    private static string Truncate(string value, int maxLength = 40)
    {
        if (value.Length <= maxLength) return value;
        return string.Concat(value.AsSpan(0, maxLength - 1), "\u2026");
    }
}
