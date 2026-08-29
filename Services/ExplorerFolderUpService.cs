using System.Diagnostics;
using System.Windows.Automation;
using KeyCapture.Interop;

namespace KeyCapture.Services;

/// <summary>
/// Navigates a Windows Explorer folder window to its parent folder when the user
/// double clicks an empty spot in the file list.
///
/// The "up" step is performed by sending Explorer's own Alt+Up shortcut, which means
/// a window that is already at a root (Desktop, This PC) simply stays where it is.
/// </summary>
internal sealed class ExplorerFolderUpService : IDisposable
{
    private const int HitTestTimeoutMs = 400;
    private const int ForegroundWaitMs = 300;
    private const int ForegroundPollMs = 25;

    private readonly MouseHookManager _mouseHook;

    // Hit testing is started on the *first* click of a pair: by the time the second
    // click is delivered Explorer may already have opened the item under the cursor,
    // which would make a hit test at that moment report empty space.
    private Task<bool>? _pendingHitTest;
    private IntPtr _pendingRoot;
    private NativeMethods.POINT _pendingPoint;

    public ExplorerFolderUpService(MouseHookManager mouseHook)
    {
        _mouseHook = mouseHook;
        _mouseHook.LeftButtonDown += OnLeftButtonDown;
    }

    private void OnLeftButtonDown(NativeMethods.POINT point, bool isDoubleClick)
    {
        if (isDoubleClick)
        {
            var hitTest = _pendingHitTest;
            var root = _pendingRoot;
            var origin = _pendingPoint;
            ClearPending();

            if (hitTest is null || root == IntPtr.Zero || !IsSamePosition(point, origin))
                return;

            _ = NavigateUpIfBlankAsync(hitTest, root);
            return;
        }

        ClearPending();

        // Cheap Win32 filter first — the UI Automation hit test only runs for clicks
        // that actually landed inside a shell folder view.
        if (!TryGetFolderWindow(point, out var folderWindow))
            return;

        _pendingRoot = folderWindow;
        _pendingPoint = point;
        _pendingHitTest = Task.Run(() => IsBlankArea(point));
    }

    private void ClearPending()
    {
        _pendingHitTest = null;
        _pendingRoot = IntPtr.Zero;
    }

    private static bool IsSamePosition(NativeMethods.POINT a, NativeMethods.POINT b)
    {
        int slopX = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXDOUBLECLK) / 2;
        int slopY = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYDOUBLECLK) / 2;
        return Math.Abs(a.X - b.X) <= slopX && Math.Abs(a.Y - b.Y) <= slopY;
    }

    private static async Task NavigateUpIfBlankAsync(Task<bool> hitTest, IntPtr folderWindow)
    {
        try
        {
            var finished = await Task.WhenAny(hitTest, Task.Delay(HitTestTimeoutMs)).ConfigureAwait(false);
            if (finished != hitTest || !await hitTest.ConfigureAwait(false))
                return;

            // The click that triggered us also activates the window; give focus a moment
            // to settle so the synthesised keystroke cannot leak into another window.
            for (int waited = 0; waited <= ForegroundWaitMs; waited += ForegroundPollMs)
            {
                if (NativeMethods.GetForegroundWindow() == folderWindow)
                {
                    SendGoUpShortcut();
                    return;
                }

                await Task.Delay(ForegroundPollMs).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ExplorerFolderUp] Navigation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns the top-level folder window when the point is inside its file list
    /// (SHELLDLL_DefView), excluding the desktop.
    /// </summary>
    private static bool TryGetFolderWindow(NativeMethods.POINT point, out IntPtr folderWindow)
    {
        folderWindow = IntPtr.Zero;

        var hwnd = NativeMethods.WindowFromPoint(point);
        if (hwnd == IntPtr.Zero)
            return false;

        bool insideShellView = false;
        var current = hwnd;
        for (int depth = 0; depth < 8 && current != IntPtr.Zero; depth++)
        {
            if (NativeMethods.GetWindowClassName(current) == "SHELLDLL_DefView")
            {
                insideShellView = true;
                break;
            }

            current = NativeMethods.GetParent(current);
        }

        if (!insideShellView)
            return false;

        var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        var rootClass = NativeMethods.GetWindowClassName(root);

        // "Progman"/"WorkerW" host the desktop, which has no parent folder to go to.
        // "#32770" covers the shell view embedded in file open/save dialogs.
        if (rootClass is not ("CabinetWClass" or "ExploreWClass" or "#32770"))
            return false;

        folderWindow = root;
        return true;
    }

    /// <summary>
    /// Uses UI Automation to decide whether the point sits on empty space rather than on
    /// a file, folder, column header or scrollbar. Win32 hit testing cannot answer this:
    /// modern Explorer draws its items in a single DirectUI window.
    /// </summary>
    private static bool IsBlankArea(NativeMethods.POINT point)
    {
        try
        {
            var element = AutomationElement.FromPoint(new System.Windows.Point(point.X, point.Y));
            if (element is null)
                return false;

            var walker = TreeWalker.ControlViewWalker;
            for (int depth = 0; depth < 8 && element is not null; depth++)
            {
                var controlType = element.Current.ControlType;

                if (controlType == ControlType.ListItem || controlType == ControlType.DataItem
                    || controlType == ControlType.TreeItem || controlType == ControlType.HeaderItem
                    || controlType == ControlType.Header || controlType == ControlType.Button
                    || controlType == ControlType.SplitButton || controlType == ControlType.MenuItem
                    || controlType == ControlType.Edit || controlType == ControlType.ComboBox
                    || controlType == ControlType.TabItem || controlType == ControlType.Hyperlink
                    || controlType == ControlType.ScrollBar || controlType == ControlType.Thumb)
                {
                    return false;
                }

                if (controlType == ControlType.List || controlType == ControlType.DataGrid
                    || controlType == ControlType.Window)
                {
                    return true;
                }

                element = walker.GetParent(element);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ExplorerFolderUp] Hit test failed: {ex.Message}");
        }

        return false;
    }

    private static void SendGoUpShortcut()
    {
        var inputs = new NativeMethods.INPUT[4];

        inputs[0] = KeyInput(NativeMethods.VK_MENU, keyUp: false, extended: false);
        inputs[1] = KeyInput(NativeMethods.VK_UP, keyUp: false, extended: true);
        inputs[2] = KeyInput(NativeMethods.VK_UP, keyUp: true, extended: true);
        inputs[3] = KeyInput(NativeMethods.VK_MENU, keyUp: true, extended: false);

        NativeMethods.SendInput((uint)inputs.Length, inputs, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static NativeMethods.INPUT KeyInput(ushort virtualKey, bool keyUp, bool extended)
    {
        uint flags = 0;
        if (keyUp) flags |= NativeMethods.KEYEVENTF_KEYUP;
        if (extended) flags |= NativeMethods.KEYEVENTF_EXTENDEDKEY;

        return new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            u = new NativeMethods.INPUTUNION
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = virtualKey,
                    dwFlags = flags,
                    dwExtraInfo = NativeMethods.SyntheticInputSignature
                }
            }
        };
    }

    public void Dispose()
    {
        _mouseHook.LeftButtonDown -= OnLeftButtonDown;
        ClearPending();
    }
}
