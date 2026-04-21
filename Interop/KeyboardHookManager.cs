using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;
using KeyCapture.Services;

namespace KeyCapture.Interop;

internal sealed class KeyPressedEventArgs(Key key, int virtualKeyCode, ActiveModifiers modifiers)
{
    public Key Key { get; } = key;
    public int VirtualKeyCode { get; } = virtualKeyCode;
    public ActiveModifiers Modifiers { get; } = modifiers;
}

internal sealed class KeyboardHookManager : IDisposable
{
    private IntPtr _hookId = IntPtr.Zero;
    private readonly NativeMethods.LowLevelKeyboardProc _hookProc;
    private readonly ModifierKeyTracker _modifierTracker;

    public event Action<KeyPressedEventArgs>? KeyPressed;

    public KeyboardHookManager(ModifierKeyTracker modifierTracker)
    {
        _modifierTracker = modifierTracker;
        _hookProc = HookCallback;
    }

    public void Install()
    {
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _hookId = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _hookProc,
            NativeMethods.GetModuleHandle(curModule.ModuleName),
            0);

        if (_hookId == IntPtr.Zero)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var hookStruct = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                int msg = wParam.ToInt32();

                bool isKeyDown = msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN;

                _modifierTracker.UpdateKeyState(hookStruct.vkCode, isKeyDown);

                if (isKeyDown)
                {
                    var key = KeyInterop.KeyFromVirtualKey((int)hookStruct.vkCode);
                    var modifiers = _modifierTracker.CurrentModifiers;
                    KeyPressed?.Invoke(new KeyPressedEventArgs(key, (int)hookStruct.vkCode, modifiers));
                }
            }
            catch (Exception ex)
            {
                // Log exception but don't crash - keyboard hook must not throw
                Debug.WriteLine($"Keyboard hook error: {ex}");
            }
        }

        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }
}
