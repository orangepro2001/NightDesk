namespace NightDesk;

public sealed record HotkeySpec(uint Modifiers, uint Key, string Text)
{
    private static readonly Dictionary<string, uint> keys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["F1"] = 0x70, ["F2"] = 0x71, ["F3"] = 0x72, ["F4"] = 0x73,
        ["F5"] = 0x74, ["F6"] = 0x75, ["F7"] = 0x76, ["F8"] = 0x77,
        ["F9"] = 0x78, ["F10"] = 0x79, ["F11"] = 0x7A, ["F12"] = 0x7B,
        ["A"] = 0x41, ["B"] = 0x42, ["C"] = 0x43, ["D"] = 0x44,
        ["E"] = 0x45, ["F"] = 0x46, ["G"] = 0x47, ["H"] = 0x48,
        ["I"] = 0x49, ["J"] = 0x4A, ["K"] = 0x4B, ["L"] = 0x4C,
        ["M"] = 0x4D, ["N"] = 0x4E, ["O"] = 0x4F, ["P"] = 0x50,
        ["Q"] = 0x51, ["R"] = 0x52, ["S"] = 0x53, ["T"] = 0x54,
        ["U"] = 0x55, ["V"] = 0x56, ["W"] = 0x57, ["X"] = 0x58,
        ["Y"] = 0x59, ["Z"] = 0x5A, ["0"] = 0x30, ["1"] = 0x31,
        ["2"] = 0x32, ["3"] = 0x33, ["4"] = 0x34, ["5"] = 0x35,
        ["6"] = 0x36, ["7"] = 0x37, ["8"] = 0x38, ["9"] = 0x39
    };

    public static HotkeySpec Parse(string value)
    {
        var parts = value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) throw new ArgumentException("快捷键必须包含至少一个修饰键。");
        uint modifiers = 0;
        foreach (var part in parts[..^1])
        {
            uint bit = part.ToLowerInvariant() switch { "ctrl" or "control" => 2, "alt" => 1, "shift" => 4, "win" => 8, _ => 0 };
            if (bit == 0 || (modifiers & bit) != 0) throw new ArgumentException("快捷键修饰键无效或重复。");
            modifiers |= bit;
        }
        var key = parts[^1];
        if (!keys.TryGetValue(key, out var code)) throw new ArgumentException("主按键暂仅支持字母、数字及 F1–F12。");
        if (modifiers == 0x7 && code == 0x2E) throw new ArgumentException("Ctrl+Alt+Delete 由 Windows 保留。");
        var ordered = new List<string>();
        if ((modifiers & 2) != 0) ordered.Add("Ctrl");
        if ((modifiers & 1) != 0) ordered.Add("Alt");
        if ((modifiers & 4) != 0) ordered.Add("Shift");
        if ((modifiers & 8) != 0) ordered.Add("Win");
        ordered.Add(key.ToUpperInvariant());
        return new HotkeySpec(modifiers, code, string.Join('+', ordered));
    }
}
