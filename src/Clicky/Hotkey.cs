namespace Clicky;

[Flags]
public enum Modifiers
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
    Meta = 8,
}

/// <summary>A global shortcut such as "Ctrl+Alt+Shift+M". Key is "A"-"Z", "0"-"9", "F1"-"F24" or "Space".</summary>
public sealed record Hotkey(Modifiers Modifiers, string Key)
{
    public static Hotkey Parse(string text)
    {
        var mods = Modifiers.None;
        string? key = null;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var mod = part.ToLowerInvariant() switch
            {
                "ctrl" or "control" => Modifiers.Ctrl,
                "alt" => Modifiers.Alt,
                "shift" => Modifiers.Shift,
                "meta" or "win" or "super" => Modifiers.Meta,
                _ => Modifiers.None,
            };
            if (mod != Modifiers.None)
                mods |= mod;
            else if (key is not null)
                throw new FormatException($"hotkey '{text}' has more than one non-modifier key");
            else
                key = NormalizeKey(part) ?? throw new FormatException($"unsupported key '{part}' in hotkey '{text}'");
        }
        return new Hotkey(mods, key ?? throw new FormatException($"hotkey '{text}' has no key"));
    }

    private static string? NormalizeKey(string key)
    {
        if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0]))
            return key.ToUpperInvariant();
        if (key.Equals("space", StringComparison.OrdinalIgnoreCase))
            return "Space";
        if (key.Length is 2 or 3 && key[0] is 'F' or 'f' && int.TryParse(key.AsSpan(1), out int n) && n is >= 1 and <= 24)
            return $"F{n}";
        return null;
    }

    /// <summary>1-24 for F1-F24, otherwise null.</summary>
    private int? FunctionKey => Key.Length > 1 && Key[0] == 'F' ? int.Parse(Key.AsSpan(1)) : null;

    /// <summary>Qt/KDE key code (QKeyCombination::toCombined), as kglobalaccel expects.</summary>
    public int ToQtKey()
    {
        int code = Key == "Space" ? 0x20 : FunctionKey is int f ? 0x01000030 + f - 1 : Key[0];
        if (Modifiers.HasFlag(Modifiers.Shift)) code |= 0x02000000;
        if (Modifiers.HasFlag(Modifiers.Ctrl)) code |= 0x04000000;
        if (Modifiers.HasFlag(Modifiers.Alt)) code |= 0x08000000;
        if (Modifiers.HasFlag(Modifiers.Meta)) code |= 0x10000000;
        return code;
    }

    /// <summary>Win32 RegisterHotKey modifiers (with MOD_NOREPEAT) and virtual-key code.</summary>
    public (uint Modifiers, uint VirtualKey) ToWin32()
    {
        uint vk = Key == "Space" ? 0x20u : FunctionKey is int f ? (uint)(0x70 + f - 1) : Key[0];
        uint mods = 0x4000; // MOD_NOREPEAT
        if (Modifiers.HasFlag(Modifiers.Alt)) mods |= 0x1;
        if (Modifiers.HasFlag(Modifiers.Ctrl)) mods |= 0x2;
        if (Modifiers.HasFlag(Modifiers.Shift)) mods |= 0x4;
        if (Modifiers.HasFlag(Modifiers.Meta)) mods |= 0x8;
        return (mods, vk);
    }

    public override string ToString() =>
        string.Join('+', Enum.GetValues<Modifiers>().Where(m => m != Modifiers.None && Modifiers.HasFlag(m))
            .Select(m => m.ToString()).Append(Key));
}
