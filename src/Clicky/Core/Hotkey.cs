namespace Clicky.Core;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
    Meta = 8,
}

/// <summary>A global shortcut such as "Ctrl+Alt+Shift+M".</summary>
public sealed record Hotkey(HotkeyModifiers Modifiers, string Key)
{
    public static Hotkey Parse(string text)
    {
        var mods = HotkeyModifiers.None;
        string? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var mod = raw.ToLowerInvariant() switch
            {
                "ctrl" or "control" => HotkeyModifiers.Ctrl,
                "alt" => HotkeyModifiers.Alt,
                "shift" => HotkeyModifiers.Shift,
                "meta" or "win" or "super" => HotkeyModifiers.Meta,
                _ => HotkeyModifiers.None,
            };
            if (mod != HotkeyModifiers.None)
            {
                mods |= mod;
                continue;
            }
            if (key is not null)
                throw new FormatException($"hotkey '{text}' has more than one non-modifier key");
            key = NormalizeKey(raw) ?? throw new FormatException($"unsupported key '{raw}' in hotkey '{text}'");
        }
        if (key is null)
            throw new FormatException($"hotkey '{text}' has no key");
        return new Hotkey(mods, key);
    }

    private static string? NormalizeKey(string raw)
    {
        if (raw.Length == 1 && char.IsAsciiLetterOrDigit(raw[0]))
            return raw.ToUpperInvariant();
        if (raw.Equals("space", StringComparison.OrdinalIgnoreCase))
            return "Space";
        if (raw.Length is 2 or 3 && raw[0] is 'F' or 'f' && int.TryParse(raw.AsSpan(1), out int n) && n is >= 1 and <= 24)
            return $"F{n}";
        return null;
    }

    private int? FunctionKeyNumber => Key.Length > 1 && Key[0] == 'F' ? int.Parse(Key.AsSpan(1)) : null;

    /// <summary>Qt/KDE key code (QKeyCombination::toCombined) as used by kglobalaccel.</summary>
    public int ToQtKey()
    {
        int code = Key switch
        {
            "Space" => 0x20,
            _ when FunctionKeyNumber is int n => 0x01000030 + n - 1,
            _ => Key[0],
        };
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) code |= 0x02000000;
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) code |= 0x04000000;
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) code |= 0x08000000;
        if (Modifiers.HasFlag(HotkeyModifiers.Meta)) code |= 0x10000000;
        return code;
    }

    /// <summary>Win32 RegisterHotKey modifiers (with MOD_NOREPEAT) and virtual-key code.</summary>
    public (uint Modifiers, uint VirtualKey) ToWin32()
    {
        uint vk = Key switch
        {
            "Space" => 0x20,
            _ when FunctionKeyNumber is int n => (uint)(0x70 + n - 1),
            _ => Key[0],
        };
        uint mods = 0x4000; // MOD_NOREPEAT
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) mods |= 0x1;
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) mods |= 0x2;
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) mods |= 0x4;
        if (Modifiers.HasFlag(HotkeyModifiers.Meta)) mods |= 0x8;
        return (mods, vk);
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Meta)) parts.Add("Meta");
        parts.Add(Key);
        return string.Join('+', parts);
    }
}
