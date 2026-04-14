namespace KeyCapture.Services;

[Flags]
internal enum ActiveModifiers
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
    Win = 8
}

internal sealed class ModifierKeyTracker
{
    private const uint VK_LCONTROL = 0xA2;
    private const uint VK_RCONTROL = 0xA3;
    private const uint VK_LMENU = 0xA4;
    private const uint VK_RMENU = 0xA5;
    private const uint VK_LSHIFT = 0xA0;
    private const uint VK_RSHIFT = 0xA1;
    private const uint VK_LWIN = 0x5B;
    private const uint VK_RWIN = 0x5C;

    private bool _lCtrl, _rCtrl;
    private bool _lAlt, _rAlt;
    private bool _lShift, _rShift;
    private bool _lWin, _rWin;

    public ActiveModifiers CurrentModifiers
    {
        get
        {
            var m = ActiveModifiers.None;
            if (_lCtrl || _rCtrl) m |= ActiveModifiers.Ctrl;
            if (_lAlt || _rAlt) m |= ActiveModifiers.Alt;
            if (_lShift || _rShift) m |= ActiveModifiers.Shift;
            if (_lWin || _rWin) m |= ActiveModifiers.Win;
            return m;
        }
    }

    public void UpdateKeyState(uint vkCode, bool isKeyDown)
    {
        switch (vkCode)
        {
            case VK_LCONTROL: _lCtrl = isKeyDown; break;
            case VK_RCONTROL: _rCtrl = isKeyDown; break;
            case VK_LMENU: _lAlt = isKeyDown; break;
            case VK_RMENU: _rAlt = isKeyDown; break;
            case VK_LSHIFT: _lShift = isKeyDown; break;
            case VK_RSHIFT: _rShift = isKeyDown; break;
            case VK_LWIN: _lWin = isKeyDown; break;
            case VK_RWIN: _rWin = isKeyDown; break;
        }
    }

    public static bool IsModifierKey(uint vkCode) => vkCode switch
    {
        VK_LCONTROL or VK_RCONTROL or VK_LMENU or VK_RMENU
            or VK_LSHIFT or VK_RSHIFT or VK_LWIN or VK_RWIN => true,
        _ => false
    };

    /// <summary>
    /// Returns true for keys that produce regular text input:
    /// letters (A-Z), digits (0-9), numpad digits, space, and symbol/punctuation keys.
    /// </summary>
    public static bool IsRegularTypingKey(uint vkCode)
    {
        // Letters A-Z: 0x41 - 0x5A
        if (vkCode >= 0x41 && vkCode <= 0x5A) return true;

        // Top-row digits 0-9: 0x30 - 0x39
        if (vkCode >= 0x30 && vkCode <= 0x39) return true;

        // Numpad digits 0-9: 0x60 - 0x69
        if (vkCode >= 0x60 && vkCode <= 0x69) return true;

        // Numpad operators: multiply, add, separator, subtract, decimal, divide
        if (vkCode >= 0x6A && vkCode <= 0x6F) return true;

        // Space bar, Backspace, Enter (common editing keys)
        if (vkCode == 0x20 || vkCode == 0x08 || vkCode == 0x0D) return true;

        // OEM keys (punctuation/symbols): ; = , - . / ` [ \ ] '
        if (vkCode >= 0xBA && vkCode <= 0xC0) return true; // ;=,-./`
        if (vkCode >= 0xDB && vkCode <= 0xDE) return true; // [\]'

        return false;
    }
}
