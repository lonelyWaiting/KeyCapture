using System.Windows.Input;
using KeyCapture.Interop;

namespace KeyCapture.Services;

internal sealed class KeyDisplayFormatter
{
    private static readonly Dictionary<Key, string> FriendlyNames = new()
    {
        [Key.Return] = "Enter",
        [Key.Back] = "Backspace",
        [Key.Capital] = "CapsLock",
        [Key.Escape] = "Esc",
        [Key.Space] = "Space",
        [Key.Tab] = "Tab",
        [Key.Delete] = "Del",
        [Key.Insert] = "Ins",
        [Key.Home] = "Home",
        [Key.End] = "End",
        [Key.PageUp] = "PgUp",
        [Key.PageDown] = "PgDn",
        [Key.Left] = "Left",
        [Key.Right] = "Right",
        [Key.Up] = "Up",
        [Key.Down] = "Down",
        [Key.PrintScreen] = "PrtSc",
        [Key.Scroll] = "ScrLk",
        [Key.Pause] = "Pause",
        [Key.NumLock] = "NumLk",
        [Key.OemPlus] = "=",
        [Key.OemMinus] = "-",
        [Key.OemPeriod] = ".",
        [Key.OemComma] = ",",
        [Key.OemQuestion] = "/",
        [Key.OemOpenBrackets] = "[",
        [Key.OemCloseBrackets] = "]",
        [Key.OemPipe] = "\\",
        [Key.OemSemicolon] = ";",
        [Key.OemQuotes] = "'",
        [Key.OemTilde] = "`",
        [Key.Multiply] = "Num*",
        [Key.Add] = "Num+",
        [Key.Subtract] = "Num-",
        [Key.Divide] = "Num/",
        [Key.Decimal] = "Num.",
        [Key.Apps] = "Menu",
    };

    public string Format(KeyPressedEventArgs args)
    {
        var parts = new List<string>(4);

        if (args.Modifiers.HasFlag(ActiveModifiers.Ctrl)) parts.Add("Ctrl");
        if (args.Modifiers.HasFlag(ActiveModifiers.Alt)) parts.Add("Alt");
        if (args.Modifiers.HasFlag(ActiveModifiers.Shift)) parts.Add("Shift");
        if (args.Modifiers.HasFlag(ActiveModifiers.Win)) parts.Add("Win");

        // If it's a modifier-only press, show the modifier name
        if (ModifierKeyTracker.IsModifierKey((uint)args.VirtualKeyCode))
        {
            if (parts.Count == 0)
                parts.Add(GetModifierName(args.VirtualKeyCode));
            return string.Join("+", parts);
        }

        parts.Add(GetKeyName(args.Key, args.VirtualKeyCode));
        return string.Join("+", parts);
    }

    private static string GetKeyName(Key key, int vkCode)
    {
        if (FriendlyNames.TryGetValue(key, out var friendly))
            return friendly;

        // Letter keys
        if (key >= Key.A && key <= Key.Z)
            return key.ToString();

        // Number keys (D0-D9)
        if (key >= Key.D0 && key <= Key.D9)
            return ((int)key - (int)Key.D0).ToString();

        // Numpad keys
        if (key >= Key.NumPad0 && key <= Key.NumPad9)
            return "Num" + ((int)key - (int)Key.NumPad0);

        // Function keys
        if (key >= Key.F1 && key <= Key.F24)
            return key.ToString();

        return $"[VK:0x{vkCode:X2}]";
    }

    private static string GetModifierName(int vkCode) => vkCode switch
    {
        0xA0 or 0xA1 => "Shift",
        0xA2 or 0xA3 => "Ctrl",
        0xA4 or 0xA5 => "Alt",
        0x5B or 0x5C => "Win",
        _ => $"[VK:0x{vkCode:X2}]"
    };
}
