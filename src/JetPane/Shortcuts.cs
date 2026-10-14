namespace JetPane;

static class ShortcutKeys
{
    public static (uint Modifiers, uint Key) Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return (0, 0);
        uint modifiers = 0; Keys key = Keys.None;
        foreach (string part in value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": case "control": modifiers |= 2; break;
                case "alt": modifiers |= 1; break;
                case "shift": modifiers |= 4; break;
                case "win": case "windows": modifiers |= 8; break;
                default:
                    if (key != Keys.None) throw new ArgumentException("Use one main key with Ctrl, Alt, Shift or Win.");
                    string name = part.Length == 1 && char.IsDigit(part[0]) ? "D" + part : part;
                    if (!Enum.TryParse(name, true, out key) || key is Keys.None or Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin || (uint)key > 255)
                        throw new ArgumentException("Unknown key: " + part + ". Use letters, digits, F1–F24, Space, Home, End, or NumPad0–9.");
                    break;
            }
        }
        if (key == Keys.None || (modifiers & 11) == 0) throw new ArgumentException("Include a main key and at least Ctrl, Alt or Win. Shift alone is not enough.");
        return (modifiers, (uint)key);
    }
    public static List<(string Id, string Label, string Default)> Actions(Config c)
    {
        var actions = new List<(string, string, string)> { ("save", "Save all positions", "Ctrl+Alt+S"), ("restore", "Restore all positions", "Ctrl+Alt+R"), ("preview", "Arrangement preview", "Ctrl+Alt+P") };
        foreach (var p in c.Profiles) actions.Add(("profile:" + p.Name, "Load profile: " + p.Name, p.Shortcut > 0 ? "Ctrl+Alt+" + p.Shortcut : ""));
        string[] layouts = { "Left / right", "Top / bottom", "Three columns", "Four quadrants" };
        for (int i = 1; i <= 4; i++) actions.Add(("snap:" + i, "Snap: " + layouts[i - 1], ""));
        actions.Add(("snap:clear", "Clear snap layout", ""));
        foreach (var location in c.Locations) actions.Add(("location:" + location.Id, "Move active window: " + location.Name, ""));
        return actions;
    }
    public static string Binding(Config c, string id, string fallback) => c.Shortcuts.TryGetValue(id, out string? custom) ? custom : fallback;
    public static Dictionary<string, string> Validate(IEnumerable<(string Id, string Value)> values)
    {
        var result = new Dictionary<string, string>(); var used = new HashSet<(uint, uint)>();
        foreach (var (id, value) in values)
        {
            var parsed = Parse(value);
            if (parsed.Key != 0 && !used.Add(parsed)) throw new ArgumentException("Two actions use the same shortcut: " + value);
            result[id] = value.Trim();
        }
        return result;
    }
}
