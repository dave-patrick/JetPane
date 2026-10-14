using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace JetPane;

static class AppIcon
{
    public static readonly Icon Value = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application;
}

public class Placement
{
    public string App { get; set; } = "";
    public string Path { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Maximized { get; set; }
    public bool Minimized { get; set; }
    public int Order { get; set; }
    public DateTime LastSeen { get; set; }
}
public class Profile
{
    public List<DisplayGeometry> Displays { get; set; } = new();
    public string Name { get; set; } = "Default";
    public int Shortcut { get; set; }
    public List<Placement> Windows { get; set; } = new();
}
public class Config
{
    public List<SavedLocation> Locations { get; set; } = new();
    public Dictionary<string, string> Shortcuts { get; set; } = new();
    public int Version { get; set; } = 1;
    public List<Profile> Profiles { get; set; } = new() { new() };
    public string Active { get; set; } = "Default";
    public List<string> Excluded { get; set; } = new() { "explorer", "ApplicationFrameHost" };
    public List<string> SnapExcluded { get; set; } = new();
    public Dictionary<string, string> Monitors { get; set; } = new();
    public bool AutoSave { get; set; }
    public bool AutoRestore { get; set; } = true;
    public bool DisableHotkeys { get; set; }
    public bool CleanSwitch { get; set; }
    public bool Dark { get; set; } = true;
    public string StartupMode { get; set; } = "None";
    public string StartupProfile { get; set; } = "Default";
    public int Snap { get; set; }
    public bool DisableSnapping { get; set; }
    public string SnapMonitor { get; set; } = "";
    public Dictionary<int, string> Locks { get; set; } = new();
    public Dictionary<string, int> SlotMemory { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
public record LiveWindow(nint Handle, string App, string Path, Placement Position);

static class Native
{
    public delegate bool EnumProc(nint hwnd, nint param);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct WINDOWPLACEMENT
    { public int Length, Flags, ShowCmd; public POINT Min, Max; public RECT Normal; }
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, nint param);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] public static extern bool IsZoomed(nint hwnd);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(nint hwnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetWindowPlacement(nint hwnd, ref WINDOWPLACEMENT p);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(nint hwnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern nint GetWindowLong(nint hwnd, int index);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);

    public static List<LiveWindow> Scan(IEnumerable<string> excluded)
    {
        var ignored = new HashSet<string>(excluded, StringComparer.OrdinalIgnoreCase);
        ignored.Add("explorer"); ignored.Add("ApplicationFrameHost");
        var result = new List<LiveWindow>();
        EnumWindows((h, _) =>
        {
            try
            {
                if (!IsWindowVisible(h) || GetWindowTextLength(h) == 0 || (GetWindowLong(h, -20).ToInt64() & 0x80) != 0) return true;
                if (DwmGetWindowAttribute(h, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
                GetWindowThreadProcessId(h, out uint pid);
                if (pid == Environment.ProcessId) return true;
                using var process = Process.GetProcessById((int)pid);
                string app = process.ProcessName;
                if (ignored.Contains(app)) return true;
                var wp = new WINDOWPLACEMENT { Length = Marshal.SizeOf<WINDOWPLACEMENT>() };
                if (!GetWindowPlacement(h, ref wp)) return true;
                var r = wp.Normal;
                // WINDOWPLACEMENT uses work-area coordinates for ordinary top-level windows.
                // Convert to physical virtual-desktop coordinates, including left/top taskbars.
                var screen = Screen.FromHandle(h);
                int dx = screen.WorkingArea.Left - screen.Bounds.Left;
                int dy = screen.WorkingArea.Top - screen.Bounds.Top;
                r.Left += dx; r.Right += dx; r.Top += dy; r.Bottom += dy;
                if (!IsIconic(h) && !IsZoomed(h) && GetWindowRect(h, out var actual)) r = actual;
                if (r.Right - r.Left < 400 || r.Bottom - r.Top < 300) return true;
                string path = "";
                try { path = process.MainModule?.FileName ?? ""; } catch { }
                result.Add(new(h, app, path, new Placement
                {
                    App = app, Path = path, X = r.Left, Y = r.Top, Width = r.Right - r.Left,
                    Height = r.Bottom - r.Top, Maximized = IsZoomed(h), Minimized = IsIconic(h),
                    Order = result.Count, LastSeen = DateTime.UtcNow
                }));
            }
            catch { /* A process can disappear during enumeration. */ }
            return true;
        }, 0);
        return result;
    }

    public static Rectangle SafeBounds(Placement p)
    {
        var desired = new Rectangle(p.X, p.Y, Math.Max(100, p.Width), Math.Max(100, p.Height));
        var screen = Screen.AllScreens.OrderByDescending(s => Area(Rectangle.Intersect(s.WorkingArea, desired)))
            .ThenBy(s => Math.Pow(s.Bounds.Left + s.Bounds.Width / 2.0 - desired.X - desired.Width / 2.0, 2) + Math.Pow(s.Bounds.Top + s.Bounds.Height / 2.0 - desired.Y - desired.Height / 2.0, 2)).First();
        var area = screen.WorkingArea;
        int width = Math.Min(desired.Width, area.Width), height = Math.Min(desired.Height, area.Height);
        return new(Math.Clamp(desired.X, area.Left, area.Right - width), Math.Clamp(desired.Y, area.Top, area.Bottom - height), width, height);
    }
    static long Area(Rectangle r) => (long)Math.Max(0, r.Width) * Math.Max(0, r.Height);
    public static bool Move(nint h, Placement p, bool keepMinimized)
    {
        var r = SafeBounds(p);
        ShowWindow(h, 4); // Show without activating before resizing.
        bool success = SetWindowPos(h, 0, r.X, r.Y, r.Width, r.Height, 0x14); // NOZORDER | NOACTIVATE
        if (keepMinimized && p.Minimized) ShowWindow(h, 6);
        else if (p.Maximized) ShowWindow(h, 3);
        return success;
    }
}

static class Program
{
    [STAThread] static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--self-test")) { SelfTest.Run(); return; }
        using var mutex = new Mutex(true, "Local\\WindowPilot.Portable", out bool first); // Shared with previous branding to prevent competing managers.
        if (!first) { MessageBox.Show("JetPane is already running. Look for its icon in the system tray."); return; }
        try { Application.Run(new Pilot(args.Contains("--smoke-test"))); }
        catch (Exception e) { MessageBox.Show(e.Message, "JetPane could not start"); }
    }
}

sealed partial class Pilot : Form
{
    readonly string folder = AppContext.BaseDirectory;
    string ConfigPath => System.IO.Path.Combine(folder, "settings.json");
    Config config = new();
    Profile Current => config.Profiles.First(p => p.Name == config.Active);
    readonly NotifyIcon tray;
    readonly System.Windows.Forms.Timer timer = new() { Interval = 900 };
    Dictionary<nint, LiveWindow> previous = new();
    readonly Dictionary<nint, DateTime> pending = new();
    readonly Dictionary<string, (Placement Position, DateTime Deadline)> launches = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<int, nint> slots = new();
    string monitorSignature = "";
    int quietTicks;
    bool exiting;
    readonly Dictionary<int, string> hotkeyActions = new();
    readonly List<string> hotkeyConflicts = new();

    public Pilot(bool smokeTest = false)
    {
        Text = "JetPane"; Icon = AppIcon.Value; ShowInTaskbar = false; WindowState = FormWindowState.Minimized;
        try
        {
            string probe = System.IO.Path.Combine(folder, ".write-test");
            File.WriteAllText(probe, ""); File.Delete(probe);
            if (File.Exists(ConfigPath)) config = ReadConfig(ConfigPath);
        }
        catch (Exception e) { throw new IOException("Keep JetPane in a writable folder. Settings could not be loaded: " + e.Message); }
        if (config.StartupMode != "Snap") { config.Snap = 0; config.Locks.Clear(); }
        tray = new NotifyIcon { Icon = AppIcon.Value, Text = "JetPane — portable window layouts", Visible = true };
        tray.DoubleClick += (_, _) => Settings();
        _ = Handle;
        Hotkeys(); RebuildMenu();
        timer.Tick += (_, _) => { if (!smokeTest) Tick(); }; timer.Start();
        Shown += (_, _) =>
        {
            if (smokeTest)
            {
                Hide();
                var finish = new System.Windows.Forms.Timer { Interval = 1200 };
                finish.Tick += (_, _) => { finish.Stop(); finish.Dispose(); File.WriteAllText(System.IO.Path.Combine(folder, "smoke-test.txt"), "PASS: tray, menus, hotkey registration, event loop and clean shutdown"); exiting = true; Close(); };
                finish.Start(); return;
            }
            Hide(); monitorSignature = Signature();
            if (config.Monitors.TryGetValue(monitorSignature, out string? assigned)) Switch(assigned, false);
            else if (config.StartupMode == "Profile") Switch(config.StartupProfile, false);
            else if (config.StartupMode == "Snap" && !config.DisableSnapping && config.Snap != 0) ActivateSnap(config.Snap, false);
            if (Current.Windows.Count == 0 && config.Profiles.Count == 1)
            {
                if (MessageBox.Show("Save your current windows to the Default profile now?", "Welcome to JetPane", MessageBoxButtons.YesNo) == DialogResult.Yes) SaveLayout();
            }
            Notify("Running in the tray. Double-click the icon for settings.");
        };
    }
    public static Config ReadConfig(string path)
    {
        var c = JsonSerializer.Deserialize<Config>(File.ReadAllText(path)) ?? throw new InvalidDataException("Empty settings file.");
        if (c.Version != 1 || c.Profiles == null || c.Profiles.Count == 0 || c.Profiles.Any(p => string.IsNullOrWhiteSpace(p.Name) || p.Windows == null) || c.Profiles.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != c.Profiles.Count)
            throw new InvalidDataException("Unsupported or invalid JetPane settings.");
        if (!c.Profiles.Any(p => p.Name == "Default")) c.Profiles.Insert(0, new());
        if (!c.Profiles.Any(p => p.Name == c.Active)) c.Active = "Default";
        if (!c.Profiles.Any(p => p.Name == c.StartupProfile)) c.StartupProfile = "Default";
        if (c.Excluded == null || c.SnapExcluded == null || c.Monitors == null || c.Locks == null || c.SlotMemory == null || c.Snap < 0 || c.Snap > 4)
            throw new InvalidDataException("Invalid settings collections or snap layout.");
        if (c.StartupMode is not ("None" or "Profile" or "Snap") || c.Profiles.Any(p => p.Shortcut < 0 || p.Shortcut > 9)
            || c.Profiles.Where(p => p.Shortcut > 0).GroupBy(p => p.Shortcut).Any(g => g.Count() > 1)
            || c.Locks.Any(kv => kv.Key < 0 || kv.Key >= (c.Snap == 3 ? 3 : c.Snap == 4 ? 4 : 2) || string.IsNullOrWhiteSpace(kv.Value))
            || c.Monitors.Any(kv => !c.Profiles.Any(p => p.Name == kv.Value))) throw new InvalidDataException("Invalid startup, shortcut, monitor or slot assignments.");
        if (c.Locations == null || c.Locations.Any(l => string.IsNullOrWhiteSpace(l.Id) || string.IsNullOrWhiteSpace(l.Name) || l.Position == null || l.Position.Width <= 0 || l.Position.Height <= 0 || l.Displays == null) || c.Locations.Select(l => l.Id).Distinct().Count() != c.Locations.Count) throw new InvalidDataException("Invalid location data.");
        if (c.Shortcuts == null || c.Profiles.Any(p => p.Displays == null || p.Displays.Any(d => d.Width <= 0 || d.Height <= 0))) throw new InvalidDataException("Invalid shortcut or monitor data.");
        ShortcutKeys.Validate(ShortcutKeys.Actions(c).Select(a => (a.Id, ShortcutKeys.Binding(c, a.Id, a.Default))));
        if (c.Profiles.Any(p => p.Windows.Any(w => string.IsNullOrWhiteSpace(w.App) || w.Width < 1 || w.Height < 1))) throw new InvalidDataException("Invalid saved window.");
        c.Excluded = c.Excluded.Concat(new[] { "explorer", "ApplicationFrameHost" }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        c.SlotMemory = new(c.SlotMemory, StringComparer.OrdinalIgnoreCase);
        return c;
    }
    void Persist()
    {
        try
        {
            string temp = ConfigPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
            if (File.Exists(ConfigPath)) File.Copy(ConfigPath, ConfigPath + ".bak", true);
            File.Move(temp, ConfigPath, true);
        }
        catch (Exception e) { Log(e); Notify("Settings could not be saved: " + e.Message); }
    }
    void Log(Exception e) { try { File.AppendAllText(System.IO.Path.Combine(folder, "crash.log"), DateTime.UtcNow.ToString("O") + " " + e + Environment.NewLine); } catch { } }
    void Notify(string message) { tray.BalloonTipTitle = "JetPane"; tray.BalloonTipText = message; tray.ShowBalloonTip(3000); }
    List<LiveWindow> Windows() => Native.Scan(config.Excluded);
    static string Signature() => string.Join(";", Screen.AllScreens.Select(s => $"{s.Bounds.X},{s.Bounds.Y},{s.Bounds.Width},{s.Bounds.Height}").Order());
    void SaveLayout()
    {
        Current.Displays = DisplayGeometry.Capture();
        Current.Windows = Windows().GroupBy(w => w.App, StringComparer.OrdinalIgnoreCase).Select(g => g.First().Position).ToList();
        Persist(); RebuildMenu(); Notify($"Saved {Current.Windows.Count} applications to {Current.Name}.");
    }
    void Restore(bool launch, bool keepMinimized)
    {
        nint foreground = Native.GetForegroundWindow();
        quietTicks = 3;
        var windows = Windows();
        foreach (var p in Current.Windows.OrderByDescending(p => p.Order))
        {
            var w = windows.FirstOrDefault(w => w.App.Equals(p.App, StringComparison.OrdinalIgnoreCase));
            if (w != null)
            {
                if (!Native.Move(w.Handle, p, keepMinimized)) Notify($"Could not move {p.App}. It may be running as administrator.");
                if (!p.Minimized || !keepMinimized) Native.SetWindowPos(w.Handle, 0, 0, 0, 0, 0, 0x13); // reorder without focus
            }
            else if (launch && !p.Minimized && !string.IsNullOrEmpty(p.Path))
            {
                try
                {
                    if (!File.Exists(p.Path)) { Notify($"Application path is missing: {p.App}"); continue; }
                    if (!launches.ContainsKey(p.App)) Process.Start(new ProcessStartInfo(p.Path) { UseShellExecute = true });
                    launches[p.App] = (p, DateTime.UtcNow.AddSeconds(30));
                }
                catch (Exception e) { Log(e); Notify($"Could not launch {p.App}: {e.Message}"); }
            }
        }
        if (foreground != 0 && !Native.IsIconic(foreground)) Native.SetForegroundWindow(foreground);
    }
    void Switch(string name, bool manual = true)
    {
        if (!config.Profiles.Any(p => p.Name == name)) return;
        config.Active = name; config.Snap = 0; slots.Clear(); pending.Clear(); launches.Clear();
        if (manual && config.CleanSwitch)
        {
            var otherApps = config.Profiles.Where(p => p.Name != name).SelectMany(p => p.Windows).Select(p => p.App).ToHashSet(StringComparer.OrdinalIgnoreCase);
            otherApps.ExceptWith(Current.Windows.Select(p => p.App));
            foreach (var w in Windows().Where(w => otherApps.Contains(w.App))) Native.ShowWindow(w.Handle, 6);
        }
        Restore(true, true); Persist(); RebuildMenu();
    }
    void Tick()
    {
        try
        {
            string signature = Signature();
            if (signature != monitorSignature)
            {
                monitorSignature = signature;
                if (config.Monitors.TryGetValue(signature, out string? profile)) Switch(profile, false);
                else if (config.Snap != 0 && !config.DisableSnapping) ActivateSnap(config.Snap, false);
                RebuildMenu();
            }
            var live = Windows(); var now = live.ToDictionary(w => w.Handle);
            if (now.ContainsKey(Native.GetForegroundWindow())) lastExternalWindow = Native.GetForegroundWindow();
            if (config.AutoSave && config.Snap == 0 && quietTicks == 0)
            {
                bool changed = false;
                foreach (var old in previous.Values.Where(w => !now.ContainsKey(w.Handle)))
                {
                    Current.Windows.RemoveAll(p => p.App.Equals(old.App, StringComparison.OrdinalIgnoreCase));
                    Current.Windows.Add(old.Position); changed = true;
                }
                if (changed) Persist();
            }
            foreach (var entry in launches.ToList())
            {
                var w = live.FirstOrDefault(w => w.App.Equals(entry.Key, StringComparison.OrdinalIgnoreCase));
                if (w != null) { Native.Move(w.Handle, entry.Value.Position, true); launches.Remove(entry.Key); }
                else if (DateTime.UtcNow > entry.Value.Deadline) { launches.Remove(entry.Key); Notify($"No manageable window appeared for {entry.Key} within 30 seconds."); }
            }
            foreach (var w in live)
            {
                bool appeared = !previous.TryGetValue(w.Handle, out var old);
                bool reopened = !appeared && old!.Position.Minimized && !w.Position.Minimized;
                if (appeared || reopened) pending[w.Handle] = DateTime.UtcNow.AddSeconds(1.2);
            }
            foreach (var item in pending.ToList())
            {
                if (!now.TryGetValue(item.Key, out var w)) { pending.Remove(item.Key); continue; }
                if (DateTime.UtcNow < item.Value) continue;
                pending.Remove(item.Key);
                if (config.Snap != 0 && !config.DisableSnapping) SnapWindow(w);
                else if (config.AutoRestore && quietTicks == 0)
                {
                    var p = Current.Windows.FirstOrDefault(p => p.App.Equals(w.App, StringComparison.OrdinalIgnoreCase));
                    if (p != null) Native.Move(w.Handle, p, true);
                }
            }
            foreach (int slot in slots.Keys.ToList()) if (!now.ContainsKey(slots[slot]) || Native.IsIconic(slots[slot])) slots.Remove(slot);
            previous = now;
            if (quietTicks > 0) quietTicks--;
        }
        catch (Exception e) { Log(e); }
    }
    public static Rectangle[] BuildLayout(Rectangle a, int layout)
    {
        int count = layout == 3 ? 3 : layout == 4 ? 4 : 2;
        return Enumerable.Range(0, count).Select(i =>
        {
            int columns = layout == 2 ? 1 : layout == 3 ? 3 : 2;
            int rows = layout == 2 || layout == 4 ? 2 : 1;
            int col = i % columns, row = i / columns;
            int left = a.Left + col * a.Width / columns, top = a.Top + row * a.Height / rows;
            return new Rectangle(left, top, a.Left + (col + 1) * a.Width / columns - left, a.Top + (row + 1) * a.Height / rows - top);
        }).ToArray();
    }
    Rectangle[] Areas()
    {
        var screen = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == config.SnapMonitor) ?? Screen.PrimaryScreen ?? Screen.AllScreens[0];
        return BuildLayout(screen.WorkingArea, config.Snap);
    }
    bool SnapAllowed(LiveWindow w) => !w.Position.Minimized && !config.SnapExcluded.Contains(w.App, StringComparer.OrdinalIgnoreCase);
    void ActivateSnap(int layout, bool chooseMonitor = true)
    {
        config.Snap = layout; config.DisableSnapping = false; slots.Clear(); launches.Clear(); quietTicks = 3;
        if (chooseMonitor) config.SnapMonitor = Screen.FromPoint(Cursor.Position).DeviceName;
        var live = Windows().Where(SnapAllowed).OrderByDescending(w => w.Handle == Native.GetForegroundWindow()).ToList();
        foreach (var w in live.Where(w => config.Locks.Values.Contains(w.App, StringComparer.OrdinalIgnoreCase))) SnapWindow(w);
        foreach (var w in live.Where(w => !config.Locks.Values.Contains(w.App, StringComparer.OrdinalIgnoreCase))) SnapWindow(w, true);
        Persist(); RebuildMenu();
    }
    void SnapWindow(LiveWindow w, bool nearest = false)
    {
        if (!SnapAllowed(w)) return;
        var areas = Areas();
        foreach (int i in slots.Keys.ToList()) if (Native.IsIconic(slots[i]) || !Native.IsWindowVisible(slots[i])) slots.Remove(i);
        var locked = config.Locks.FirstOrDefault(kv => kv.Value.Equals(w.App, StringComparison.OrdinalIgnoreCase));
        int slot;
        if (locked.Value != null && locked.Key >= 0 && locked.Key < areas.Length) slot = locked.Key;
        else
        {
            var free = Enumerable.Range(0, areas.Length).Where(i => !config.Locks.ContainsKey(i) && !slots.ContainsKey(i)).ToList();
            if (config.SlotMemory.TryGetValue(w.App, out int remembered) && free.Contains(remembered)) slot = remembered;
            else if (free.Count > 0) slot = nearest ? free.OrderBy(i => Distance(areas[i], w.Position)).First() : free[0];
            else
            {
                // New foreground apps may reuse an unlocked slot; locked applications remain untouched.
                var unlocked = Enumerable.Range(0, areas.Length).Where(i => !config.Locks.ContainsKey(i)).ToList();
                if (unlocked.Count == 0 || nearest) return;
                slot = config.SlotMemory.TryGetValue(w.App, out remembered) && unlocked.Contains(remembered) ? remembered : unlocked[0];
            }
        }
        foreach (int oldSlot in slots.Where(kv => kv.Value == w.Handle).Select(kv => kv.Key).ToList()) slots.Remove(oldSlot);
        slots[slot] = w.Handle; config.SlotMemory[w.App] = slot;
        var a = areas[slot];
        Native.Move(w.Handle, new Placement { X = a.X, Y = a.Y, Width = a.Width, Height = a.Height }, false);
    }
    static double Distance(Rectangle r, Placement p) => Math.Pow(r.X + r.Width / 2.0 - p.X - p.Width / 2.0, 2) + Math.Pow(r.Y + r.Height / 2.0 - p.Y - p.Height / 2.0, 2);
    void Swap()
    {
        var a = Areas(); var keys = slots.Keys.Where(i => !config.Locks.ContainsKey(i)).Order().ToArray();
        var handles = keys.Select(i => slots[i]).Reverse().ToArray();
        for (int i = 0; i < keys.Length; i++)
        {
            slots[keys[i]] = handles[i]; var r = a[keys[i]];
            Native.Move(handles[i], new Placement { X = r.X, Y = r.Y, Width = r.Width, Height = r.Height }, false);
            var w = Windows().FirstOrDefault(w => w.Handle == handles[i]); if (w != null) config.SlotMemory[w.App] = keys[i];
        }
        Persist();
    }
    void Hotkeys()
    {
        foreach (int id in hotkeyActions.Keys) Native.UnregisterHotKey(Handle, id);
        hotkeyActions.Clear(); hotkeyConflicts.Clear();
        if (config.DisableHotkeys) return;
        int nextId = 1;
        foreach (var action in ShortcutKeys.Actions(config))
        {
            string binding = ShortcutKeys.Binding(config, action.Id, action.Default);
            try
            {
                var parsed = ShortcutKeys.Parse(binding); if (parsed.Key == 0) continue;
                int id = nextId++; hotkeyActions[id] = action.Id;
                if (!Native.RegisterHotKey(Handle, id, parsed.Modifiers | 0x4000, parsed.Key)) hotkeyConflicts.Add(action.Label + ": " + binding);
                // Legacy numbered profile defaults also accept numpad; custom bindings are exact.
                if (action.Id.StartsWith("profile:") && !config.Shortcuts.ContainsKey(action.Id) && parsed.Key >= (uint)Keys.D1 && parsed.Key <= (uint)Keys.D9)
                {
                    id = nextId++; hotkeyActions[id] = action.Id;
                    if (!Native.RegisterHotKey(Handle, id, parsed.Modifiers | 0x4000, (uint)Keys.NumPad0 + parsed.Key - (uint)Keys.D0)) hotkeyConflicts.Add(action.Label + ": numpad");
                }
            }
            catch (ArgumentException e) { hotkeyConflicts.Add(action.Label + ": " + e.Message); }
        }
        if (hotkeyConflicts.Count > 0) Notify("Some shortcuts could not register. See Settings → Keyboard shortcuts.");
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x312)
        {
            if (hotkeyActions.TryGetValue((int)m.WParam, out string? action))
            {
                if (action == "save") SaveLayout();
                else if (action == "restore") Restore(false, false);
                else if (action == "preview") Preview();
                else if (action.StartsWith("profile:")) Switch(action[8..]);
                else if (action.StartsWith("location:")) MoveToLocation(action[9..]);
                else if (action == "snap:clear") { config.Snap = 0; config.Locks.Clear(); slots.Clear(); Persist(); RebuildMenu(); }
                else if (action.StartsWith("snap:") && int.TryParse(action[5..], out int layout))
                { if (config.Snap != layout) { config.Locks.Clear(); config.SlotMemory.Clear(); } ActivateSnap(layout); }
            }
        }
        base.WndProc(ref m);
    }
    ToolStripMenuItem Item(string text, Action action, bool check = false)
    { var item = new ToolStripMenuItem(text) { Checked = check }; item.Click += (_, _) => { try { action(); } catch (Exception e) { Log(e); MessageBox.Show(e.Message, "JetPane"); } }; return item; }
    void Toggle(string text, Func<bool> get, Action<bool> set, ToolStripItemCollection items)
    { items.Add(Item(text, () => { set(!get()); Persist(); RebuildMenu(); }, get())); }
    void RebuildMenu()
    {
        var old = tray.ContextMenuStrip; var menu = new ContextMenuStrip();
        if (config.Dark) { menu.BackColor = Color.FromArgb(35, 38, 44); menu.ForeColor = Color.White; menu.Renderer = new ToolStripProfessionalRenderer(new DarkColors()); }
        menu.Items.Add(Item("Arrangement preview…  " + ShortcutKeys.Binding(config, "preview", "Ctrl+Alt+P"), Preview));
        var locations = new ToolStripMenuItem("Locations"); menu.Items.Add(locations);
        foreach (var location in config.Locations)
        {
            string id = location.Id; string binding = ShortcutKeys.Binding(config, "location:" + id, "");
            locations.DropDownItems.Add(Item(location.Name + (binding.Length > 0 ? "  " + binding : ""), () => MoveToLocation(id)));
        }
        locations.DropDownItems.Add(Item("Create / edit locations…", () => SettingsPage("Locations")));
        var profiles = new ToolStripMenuItem("Profiles"); menu.Items.Add(profiles);
        foreach (var p in config.Profiles)
        {
            string name = p.Name;
            string binding = ShortcutKeys.Binding(config, "profile:" + name, p.Shortcut > 0 ? "Ctrl+Alt+" + p.Shortcut : "");
            profiles.DropDownItems.Add(Item(name + (binding.Length > 0 ? "  " + binding : "") + (config.Monitors.GetValueOrDefault(Signature()) == name ? "  ■" : ""), () => Switch(name), config.Snap == 0 && name == config.Active));
        }
        profiles.DropDownItems.Add(new ToolStripSeparator());
        profiles.DropDownItems.Add(Item("Save to current profile", SaveLayout));
        profiles.DropDownItems.Add(Item("New profile…", () =>
        {
            string? name = Ask("New profile name"); if (string.IsNullOrWhiteSpace(name)) return;
            if (config.Profiles.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("That name already exists.");
            config.Profiles.Add(new Profile { Name = name }); config.Active = name; config.Snap = 0; slots.Clear(); SaveLayout();
        }));
        profiles.DropDownItems.Add(Item("Rename current profile…", () =>
        {
            if (Current.Name == "Default") { Notify("Default cannot be renamed."); return; }
            string? name = Ask("Rename profile", Current.Name); if (string.IsNullOrWhiteSpace(name)) return;
            if (config.Profiles.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("That name already exists.");
            string before = Current.Name; Current.Name = name; config.Active = name;
            if (config.Shortcuts.Remove("profile:" + before, out string? shortcut)) config.Shortcuts["profile:" + name] = shortcut;
            foreach (string key in config.Monitors.Keys.ToList()) if (config.Monitors[key] == before) config.Monitors[key] = name;
            if (config.StartupProfile == before) config.StartupProfile = name;
            Persist(); Hotkeys(); RebuildMenu();
        }));
        profiles.DropDownItems.Add(Item("Delete current profile…", () =>
        {
            if (Current.Name == "Default") { Notify("Default cannot be deleted."); return; }
            if (!Confirm($"Delete {Current.Name}?")) return;
            string name = Current.Name; config.Profiles.Remove(Current); config.Shortcuts.Remove("profile:" + name);
            foreach (string key in config.Monitors.Keys.ToList()) if (config.Monitors[key] == name) config.Monitors.Remove(key);
            if (config.StartupProfile == name) config.StartupProfile = "Default";
            Switch("Default"); Hotkeys();
        }));
        var shortcuts = new ToolStripMenuItem("Assign shortcut to current profile"); profiles.DropDownItems.Add(shortcuts);
        for (int n = 0; n <= 9; n++) { int number = n; shortcuts.DropDownItems.Add(Item(n == 0 ? "None" : $"Ctrl+Alt+{n}", () => { foreach (var p in config.Profiles.Where(p => p.Shortcut == number)) { p.Shortcut = 0; config.Shortcuts.Remove("profile:" + p.Name); } Current.Shortcut = number; config.Shortcuts.Remove("profile:" + Current.Name); Persist(); Hotkeys(); RebuildMenu(); }, Current.Shortcut == n)); }
        profiles.DropDownItems.Add(Item("Assign current profile to these monitors", () => { config.Monitors[Signature()] = Current.Name; Persist(); RebuildMenu(); }));
        profiles.DropDownItems.Add(Item("Clear assignment for these monitors", () => { config.Monitors.Remove(Signature()); Persist(); RebuildMenu(); }));
        var startup = new ToolStripMenuItem("Startup profile"); profiles.DropDownItems.Add(startup);
        foreach (var p in config.Profiles) { string name = p.Name; startup.DropDownItems.Add(Item(name, () => { config.StartupProfile = name; SetStartup("Profile"); }, config.StartupProfile == name)); }
        var snap = new ToolStripMenuItem("Snap layouts"); menu.Items.Add(snap);
        string[] labels = { "Left / right", "Top / bottom", "Three columns", "Four quadrants" };
        for (int i = 1; i <= 4; i++) { int layout = i; snap.DropDownItems.Add(Item(labels[i - 1], () => { if (config.Snap != layout) { config.Locks.Clear(); config.SlotMemory.Clear(); } ActivateSnap(layout); }, config.Snap == i && !config.DisableSnapping)); }
        if (config.Snap != 0)
        {
            snap.DropDownItems.Add(Item("Swap unlocked windows", Swap));
            var locks = new ToolStripMenuItem("Lock application to slot"); snap.DropDownItems.Add(locks);
            foreach (var w in Windows().Where(SnapAllowed).GroupBy(w => w.App).Select(g => g.First()))
            {
                var app = new ToolStripMenuItem(w.App); locks.DropDownItems.Add(app);
                for (int i = 0; i < Areas().Length; i++) { int slot = i; app.DropDownItems.Add(Item($"Slot {i + 1}", () => { foreach (int key in config.Locks.Where(kv => kv.Value.Equals(w.App, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList()) config.Locks.Remove(key); config.Locks[slot] = w.App; ActivateSnap(config.Snap, false); }, config.Locks.GetValueOrDefault(i) == w.App)); }
            }
            foreach (var pair in config.Locks.ToList()) snap.DropDownItems.Add(Item($"Unlock slot {pair.Key + 1} ({pair.Value})", () => { config.Locks.Remove(pair.Key); Persist(); RebuildMenu(); }));
        }
        Toggle("Disable snapping (keep saved layout)", () => config.DisableSnapping, b => { config.DisableSnapping = b; if (!b && config.Snap != 0) ActivateSnap(config.Snap, false); }, snap.DropDownItems);
        snap.DropDownItems.Add(Item("Clear snap layout", () => { config.Snap = 0; config.Locks.Clear(); slots.Clear(); Persist(); RebuildMenu(); }));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Save all positions  " + ShortcutKeys.Binding(config, "save", "Ctrl+Alt+S"), SaveLayout));
        menu.Items.Add(Item("Restore all positions  " + ShortcutKeys.Binding(config, "restore", "Ctrl+Alt+R"), () => Restore(false, false)));
        menu.Items.Add(Item("Clear saved positions…", () => { if (Confirm("Clear all positions in the current profile?")) { Current.Windows.Clear(); Persist(); } }));
        menu.Items.Add(Item("Start with Windows (profile)", () => SetStartup(config.StartupMode == "Profile" ? "None" : "Profile"), config.StartupMode == "Profile"));
        menu.Items.Add(Item("Start with Windows (snap layout)", () => SetStartup(config.StartupMode == "Snap" ? "None" : "Snap"), config.StartupMode == "Snap"));
        Toggle("Auto save to profile", () => config.AutoSave, b => config.AutoSave = b, menu.Items);
        Toggle("Auto restore new windows", () => config.AutoRestore, b => config.AutoRestore = b, menu.Items);
        Toggle("Disable global hotkeys", () => config.DisableHotkeys, b => { config.DisableHotkeys = b; Hotkeys(); }, menu.Items);
        Toggle("Clean desktop on manual profile switch", () => config.CleanSwitch, b => config.CleanSwitch = b, menu.Items);
        menu.Items.Add(Item(config.Dark ? "Switch to light tray menu" : "Switch to dark tray menu", () => { config.Dark = !config.Dark; Persist(); RebuildMenu(); }));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Settings…", Settings));
        menu.Items.Add(Item("Detailed help (offline)", Help));
        menu.Items.Add(Item("Open config folder", () => Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true })));
        menu.Items.Add(Item("Import settings…", Import)); menu.Items.Add(Item("Export settings…", Export));
        menu.Items.Add(Item("Open debug log", () => { string path = System.IO.Path.Combine(folder, "crash.log"); if (File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); else Notify("No issues have been logged."); }));
        menu.Items.Add(Item("About", () => MessageBox.Show("JetPane 1.1\nPortable Windows window manager.\nSettings are stored beside the executable.\nNo subscription, account, telemetry or network calls.\nOne saved position per application.\nFull source is included in the download.", "JetPane")));
        menu.Items.Add(Item("Exit", () => { exiting = true; Close(); }));
        tray.ContextMenuStrip = menu; old?.Dispose();
    }
    void SetStartup(string mode)
    {
        // Only this explicit menu action changes Windows login startup.
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        key.DeleteValue("WindowPilot", false);
        if (mode == "None") key.DeleteValue("JetPane", false);
        else key.SetValue("JetPane", "\"" + Environment.ProcessPath + "\"");
        config.StartupMode = mode; Persist(); RebuildMenu();
    }
    static bool Confirm(string text) => MessageBox.Show(text, "JetPane", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
    static string? Ask(string title, string initial = "")
    {
        using var f = new Form { Icon = AppIcon.Value, Text = title, Width = 420, Height = 155, StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false };
        var box = new TextBox { Left = 16, Top = 16, Width = 370, Text = initial };
        var ok = new Button { Text = "OK", Left = 220, Top = 55, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 310, Top = 55, DialogResult = DialogResult.Cancel };
        f.Controls.AddRange(new Control[] { box, ok, cancel }); f.AcceptButton = ok; f.CancelButton = cancel;
        return f.ShowDialog() == DialogResult.OK ? box.Text.Trim() : null;
    }
    void Export()
    {
        using var d = new SaveFileDialog { Filter = "JetPane settings|*.wpilot", FileName = $"JetPane-{DateTime.Now:yyyyMMdd-HHmmss}.wpilot" };
        if (d.ShowDialog() == DialogResult.OK) File.WriteAllText(d.FileName, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
    }
    void Import()
    {
        using var d = new OpenFileDialog { Filter = "JetPane settings|*.wpilot" };
        if (d.ShowDialog() != DialogResult.OK) return;
        var incoming = ReadConfig(d.FileName);
        if (!Confirm("Replace all profiles and settings? A backup of your current settings will be kept beside the executable. Imported application paths can launch programs when you load profiles. Only import files you trust.")) return;
        File.WriteAllText(ConfigPath + ".pre-import.bak", JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
        string startupMode = config.StartupMode; config = incoming; config.StartupMode = startupMode; // import does not change OS startup
        slots.Clear(); launches.Clear(); pending.Clear(); previous.Clear(); Persist(); Hotkeys();
        if (config.Monitors.TryGetValue(Signature(), out string? profile)) Switch(profile, false);
        else if (config.Snap != 0 && !config.DisableSnapping) ActivateSnap(config.Snap, false);
        RebuildMenu();
    }
    void Settings()
    { SettingsPage(null); }
    void SettingsPage(string? initialTab)
    {
        using var f = new Form { Icon = AppIcon.Value, Text = "JetPane settings", Width = 880, Height = 570, StartPosition = FormStartPosition.CenterScreen, MinimumSize = new Size(650, 400) };
        var tabs = new TabControl { Dock = DockStyle.Fill }; f.Controls.Add(tabs);
        var positions = new TabPage("Saved positions"); tabs.TabPages.Add(positions);
        var combo = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
        combo.Items.AddRange(config.Profiles.Select(p => p.Name).Cast<object>().ToArray()); combo.SelectedItem = config.Active;
        var list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true };
        foreach (var col in new[] { "Order", "Application", "Position", "Size", "State", "Last seen (local)" }) list.Columns.Add(col, col == "Application" || col.StartsWith("Last") ? 180 : 95);
        void RefreshPositions()
        {
            list.Items.Clear(); var profile = config.Profiles.First(p => p.Name == (string)combo.SelectedItem!);
            foreach (var p in profile.Windows.OrderBy(p => p.Order)) list.Items.Add(new ListViewItem(new[] { (p.Order + 1).ToString(), p.App, $"{p.X}, {p.Y}", $"{p.Width}×{p.Height}", p.Minimized ? "Minimized" : p.Maximized ? "Maximized" : "Normal", p.LastSeen.ToLocalTime().ToString("g") }) { Tag = p });
        }
        var remove = new Button { Text = "Remove selected from profile", Dock = DockStyle.Bottom, Height = 34 };
        remove.Click += (_, _) => { var p = config.Profiles.First(p => p.Name == (string)combo.SelectedItem!); foreach (ListViewItem item in list.SelectedItems) p.Windows.Remove((Placement)item.Tag!); Persist(); RefreshPositions(); };
        combo.SelectedIndexChanged += (_, _) => RefreshPositions(); positions.Controls.Add(list); positions.Controls.Add(combo); positions.Controls.Add(remove); RefreshPositions();
        void ExclusionTab(string title, List<string> apps, bool global)
        {
            var tab = new TabPage(title); tabs.TabPages.Add(tab); var lb = new ListBox { Dock = DockStyle.Fill };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42 };
            var add = new Button { Text = "Add open app…", AutoSize = true }; var del = new Button { Text = "Remove", AutoSize = true };
            var manual = new Button { Text = "Add process name…", AutoSize = true };
            void Refresh() { lb.Items.Clear(); lb.Items.AddRange(apps.Cast<object>().ToArray()); }
            void AddName(string? name)
            {
                if (string.IsNullOrWhiteSpace(name)) return; name = System.IO.Path.GetFileNameWithoutExtension(name);
                if (!apps.Contains(name, StringComparer.OrdinalIgnoreCase)) apps.Add(name);
                if (global) foreach (var p in config.Profiles) p.Windows.RemoveAll(w => w.App.Equals(name, StringComparison.OrdinalIgnoreCase));
                Persist(); Refresh();
            }
            add.Click += (_, _) =>
            {
                using var picker = new Form { Icon = AppIcon.Value, Text = "Choose an open application", Width = 380, Height = 370, StartPosition = FormStartPosition.CenterParent };
                var choices = new ListBox { Dock = DockStyle.Fill }; choices.Items.AddRange(Native.Scan(Array.Empty<string>()).Select(w => w.App).Distinct().Cast<object>().ToArray());
                var choose = new Button { Text = "Add selected", Dock = DockStyle.Bottom, DialogResult = DialogResult.OK }; picker.Controls.Add(choices); picker.Controls.Add(choose);
                if (picker.ShowDialog(f) == DialogResult.OK) AddName(choices.SelectedItem as string);
            };
            manual.Click += (_, _) => AddName(Ask("Process name (for example chrome)"));
            del.Click += (_, _) => { if (lb.SelectedItem is string app) { if (global && (app.Equals("explorer", StringComparison.OrdinalIgnoreCase) || app.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))) return; apps.Remove(app); Persist(); Refresh(); } };
            bar.Controls.AddRange(new Control[] { add, manual, del }); tab.Controls.Add(lb); tab.Controls.Add(bar); Refresh();
        }
        ExclusionTab("Exclude list", config.Excluded, true); ExclusionTab("Snap exclude", config.SnapExcluded, false);
        var monitors = new TabPage("Monitor profiles"); tabs.TabPages.Add(monitors);
        var ml = new ListBox { Dock = DockStyle.Fill };
        void RefreshMonitors() { ml.Items.Clear(); foreach (var pair in config.Monitors) ml.Items.Add(pair.Key + " → " + pair.Value); }
        var mb = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 65 };
        var pc = new ComboBox { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList }; pc.Items.AddRange(config.Profiles.Select(p => p.Name).Cast<object>().ToArray()); pc.SelectedItem = config.Active;
        var assign = new Button { Text = "Assign current monitors", AutoSize = true }; assign.Click += (_, _) => { config.Monitors[Signature()] = (string)pc.SelectedItem!; Persist(); RefreshMonitors(); RebuildMenu(); };
        var clear = new Button { Text = "Remove selected", AutoSize = true }; clear.Click += (_, _) => { if (ml.SelectedIndex >= 0) { config.Monitors.Remove(config.Monitors.Keys.ElementAt(ml.SelectedIndex)); Persist(); RefreshMonitors(); RebuildMenu(); } };
        mb.Controls.AddRange(new Control[] { pc, assign, clear }); monitors.Controls.Add(ml); monitors.Controls.Add(new Label { Dock = DockStyle.Top, Height = 50, Text = $"Current monitor geometry ({Screen.AllScreens.Length} screens):\n{Signature()}" }); monitors.Controls.Add(mb); RefreshMonitors();
        var about = new TabPage("About & help"); tabs.TabPages.Add(about);
        about.Controls.Add(new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Text = "JetPane 1.1 — portable Windows layouts\r\n\r\nCtrl+Alt+S: Save. Ctrl+Alt+R: Restore. Ctrl+Alt+1–9: assigned profile.\r\nRight-click the tray icon for profiles, snapping, locks and toggles.\r\n\r\nProfiles save one window per application, including executable path, size, state and stacking order. Loading a profile launches missing applications, except entries saved as minimized. Manual Restore brings minimized entries back onto the screen.\r\n\r\nMonitor assignments take priority at startup. Geometry identifies a monitor setup. Snap layouts and profiles are separate modes. Locked snap slots remain reserved for their applications.\r\n\r\nOnly visible desktop windows with a saved normal size of at least 400×300 are managed. Explorer, ApplicationFrameHost, tool windows and cloaked windows are skipped. Administrator apps may reject movement.\r\n\r\nSettings, backups and crash.log stay beside this executable. Exit before moving the folder. Startup is optional and only changes through the tray menu. Disable it before moving or deleting this app.\r\n\r\nNo account, licence check, Store dependency, telemetry or built-in updates. Export/import uses .wpilot files; PositionPro .ppro files are not supported.\r\n\r\nJetPane is an independent implementation from public documentation, not PositionPro source code." });
        var helpButton = new Button { Text = "Open detailed offline help", Dock = DockStyle.Bottom, Height = 38 };
        helpButton.Click += (_, _) => Help(); about.Controls.Add(helpButton);
        ShortcutsTab(tabs);
        LocationsTab(tabs);
        if (initialTab != null) tabs.SelectedTab = tabs.TabPages.Cast<TabPage>().FirstOrDefault(t => t.Text == initialTab);
        f.ShowDialog(); RebuildMenu();
    }
    void Help()
    {
        string path = System.IO.Path.Combine(folder, "Help.html");
        if (!File.Exists(path)) { MessageBox.Show("Help.html is missing. Extract the complete portable ZIP into this folder.", "JetPane"); return; }
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
    void ShortcutsTab(TabControl tabs)
    {
        var tab = new TabPage("Keyboard shortcuts"); tabs.TabPages.Add(tab);
        var grid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AccessibleName = "Shortcut editor" };
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Action", ReadOnly = true, FillWeight = 60 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Shortcut", FillWeight = 40 });
        void LoadBindings()
        {
            grid.Rows.Clear();
            foreach (var a in ShortcutKeys.Actions(config)) { int row = grid.Rows.Add(a.Label, ShortcutKeys.Binding(config, a.Id, a.Default)); grid.Rows[row].Tag = a.Id; }
        }
        var instructions = new Label { Dock = DockStyle.Top, Height = 58, Text = "Edit a combination, then click Save shortcuts. Example: Ctrl+Shift+S or Alt+F8.\nLeave a cell blank to disable that action. Win shortcuts may be reserved by Windows." };
        var status = new TextBox { Dock = DockStyle.Bottom, Height = 65, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, AccessibleName = "Shortcut registration status" };
        void Status() { status.Text = config.DisableHotkeys ? "Global shortcuts are currently disabled in the tray menu." : hotkeyConflicts.Count == 0 ? "All configured shortcuts are registered." : "Could not register:\r\n" + string.Join("\r\n", hotkeyConflicts); }
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42 };
        var save = new Button { Text = "Save shortcuts", AutoSize = true }; var reset = new Button { Text = "Reset to defaults", AutoSize = true };
        save.Click += (_, _) =>
        {
            try
            {
                grid.EndEdit();
                config.Shortcuts = ShortcutKeys.Validate(grid.Rows.Cast<DataGridViewRow>().Select(r => ((string)r.Tag!, Convert.ToString(r.Cells[1].Value) ?? "")));
                Hotkeys(); Persist(); RebuildMenu(); Status();
            }
            catch (ArgumentException e) { MessageBox.Show(e.Message, "Invalid shortcut"); }
        };
        reset.Click += (_, _) => { if (!Confirm("Reset custom shortcuts? Legacy profile number assignments will be kept.")) return; config.Shortcuts.Clear(); Hotkeys(); Persist(); LoadBindings(); RebuildMenu(); Status(); };
        buttons.Controls.AddRange(new Control[] { save, reset }); tab.Controls.Add(grid); tab.Controls.Add(instructions); tab.Controls.Add(status); tab.Controls.Add(buttons);
        LoadBindings(); Status();
        tab.Enter += (_, _) => { if (!grid.Rows.Cast<DataGridViewRow>().Select(r => Convert.ToString(r.Cells[0].Value)).SequenceEqual(ShortcutKeys.Actions(config).Select(a => a.Label))) LoadBindings(); Status(); };
    }
    void Preview()
    {
        using var preview = new ArrangementPreview(config, name => Switch(name), (layout, monitor) =>
        {
            if (config.Snap != layout) { config.Locks.Clear(); config.SlotMemory.Clear(); }
            config.SnapMonitor = monitor; ActivateSnap(layout, false);
        }, MoveToLocation, Help);
        preview.ShowDialog();
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!exiting) { e.Cancel = true; Hide(); return; }
        timer.Stop(); Persist(); foreach (int id in hotkeyActions.Keys) Native.UnregisterHotKey(Handle, id);
        tray.Visible = false; tray.Dispose(); base.OnFormClosing(e);
    }
}

sealed class DarkColors : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => Color.FromArgb(35, 38, 44);
    public override Color ImageMarginGradientBegin => ToolStripDropDownBackground;
    public override Color ImageMarginGradientMiddle => ToolStripDropDownBackground;
    public override Color ImageMarginGradientEnd => ToolStripDropDownBackground;
    public override Color MenuItemSelected => Color.FromArgb(65, 70, 80);
    public override Color MenuItemBorder => Color.FromArgb(100, 110, 130);
}

static class SelfTest
{
    public static void Run()
    {
        string output = System.IO.Path.Combine(AppContext.BaseDirectory, "self-test.txt");
        var results = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); results.Add("PASS: " + name); }
        try
        {
            var area = new Rectangle(-1920, 0, 1919, 1079);
            for (int layout = 1; layout <= 4; layout++)
            {
                var slots = Pilot.BuildLayout(area, layout);
                Check(slots.All(area.Contains), $"Layout {layout} stays in work area");
                Check(slots.Sum(r => (long)r.Width * r.Height) == (long)area.Width * area.Height, $"Layout {layout} covers odd pixel dimensions exactly");
                Check(!slots.SelectMany((r, i) => slots.Skip(i + 1).Select(s => Rectangle.Intersect(r, s))).Any(r => r.Width > 0 && r.Height > 0), $"Layout {layout} has no overlapping slots");
            }
            var c = new Config(); c.Profiles[0].Windows.Add(new Placement { App = "test", Width = 800, Height = 600, Minimized = true, Order = 2 });
            string path = System.IO.Path.Combine(AppContext.BaseDirectory, "test-settings.tmp");
            File.WriteAllText(path, JsonSerializer.Serialize(c)); var loaded = Pilot.ReadConfig(path);
            Check(loaded.Profiles[0].Windows[0].Minimized && loaded.Profiles[0].Windows[0].Order == 2, "JSON round-trip preserves state and order");
            File.WriteAllText(path, "{\"Version\":2}");
            bool rejected = false; try { Pilot.ReadConfig(path); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Unsupported import schema rejected"); File.Delete(path);
            Check(Native.Scan(Array.Empty<string>()).All(w => w.App != "explorer" && w.App != "ApplicationFrameHost"), "Window enumeration respects built-in exclusions");
            Check(ShortcutKeys.Parse("Ctrl+Shift+F8") == (6u, (uint)Keys.F8), "Custom modifiers and function key parsed");
            Check(ShortcutKeys.Parse("Win+2") == (8u, (uint)Keys.D2), "Windows modifier and numeric key parsed");
            bool duplicateRejected = false; try { ShortcutKeys.Validate(new[] { ("save", "Ctrl+Alt+S"), ("restore", "Alt+Ctrl+S") }); } catch (ArgumentException) { duplicateRejected = true; }
            Check(duplicateRejected, "Equivalent duplicate shortcuts rejected");
            var original = new Rectangle(-1000, 20, 800, 600);
            Check(LocationCanvas.Drag(original, new Point(0, 0), new Point(50, 70), -1) == new Rectangle(-950, 90, 800, 600), "Visual location move preserves size and negative coordinates");
            Check(LocationCanvas.Drag(original, new Point(0, 0), new Point(50, 70), 4) == new Rectangle(-1000, 20, 850, 670), "Visual corner resize changes both dimensions");
            Check(LocationCanvas.Drag(original, new Point(0, 0), new Point(-900, -900), 4).Size == new Size(100, 100), "Visual resize enforces minimum dimensions");
            c.Locations.Add(new SavedLocation { Name = "Left display", Position = new Placement { X = -1200, Y = 40, Width = 600, Height = 700 }, Displays = DisplayGeometry.Capture() });
            c.Shortcuts["location:" + c.Locations[0].Id] = "Ctrl+Shift+F8";
            File.WriteAllText(path, JsonSerializer.Serialize(c)); loaded = Pilot.ReadConfig(path);
            Check(loaded.Locations[0].Position.X == -1200 && loaded.Shortcuts.Values.Contains("Ctrl+Shift+F8"), "Locations and their shortcuts survive JSON round-trip"); File.Delete(path);
            foreach (var screen in Screen.AllScreens)
            {
                results.Add($"DISPLAY: {screen.DeviceName} bounds={screen.Bounds} work={screen.WorkingArea}");
                var work = screen.WorkingArea;
                using var probe = new Form { Icon = AppIcon.Value, Text = "JetPane verification window", StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(work.Left + 20, work.Top + 20, 500, 350) };
                probe.Show(); Application.DoEvents();
                var target = new Placement { X = work.Left + 40, Y = work.Top + 40, Width = Math.Min(640, work.Width - 80), Height = Math.Min(480, work.Height - 80) };
                Check(Native.Move(probe.Handle, target, false), $"Win32 move succeeds on {screen.DeviceName}");
                Application.DoEvents(); Native.GetWindowRect(probe.Handle, out var rect);
                Check(rect.Left == target.X && rect.Top == target.Y && rect.Right - rect.Left == target.Width && rect.Bottom - rect.Top == target.Height, $"Window lands at exact coordinates on {screen.DeviceName}");
                target.Minimized = true; Native.Move(probe.Handle, target, true);
                Check(Native.IsIconic(probe.Handle), $"Minimized state restored on {screen.DeviceName}");
                target.Minimized = false; target.Maximized = true; Native.Move(probe.Handle, target, false);
                Check(Native.IsZoomed(probe.Handle), $"Maximized state restored on {screen.DeviceName}");
                probe.Close();
            }
            var disconnected = Native.SafeBounds(new Placement { X = -50000, Y = -50000, Width = 800, Height = 600 });
            Check(Screen.AllScreens.Any(s => s.WorkingArea.Contains(disconnected)), "Disconnected-monitor positions clamped to a connected work area");
            File.WriteAllLines(output, results);
            ExportPreview();
        }
        catch (Exception e) { results.Add(e.ToString()); File.WriteAllLines(output, results); Environment.ExitCode = 1; }
    }
    static void ExportPreview()
    {
        var displays = new List<DisplayGeometry>
        {
            new() { Name = "Example left", X = -1920, Y = 0, Width = 1920, Height = 1080, WorkX = -1920, WorkY = 0, WorkWidth = 1920, WorkHeight = 1040 },
            new() { Name = "Example right", X = 0, Y = 0, Width = 2560, Height = 1440, WorkX = 0, WorkY = 0, WorkWidth = 2560, WorkHeight = 1400 }
        };
        var sample = new Config { Profiles = new() { new Profile { Name = "Example work layout", Displays = displays, Windows = new() {
            new() { App = "Browser", X = -1920, Y = 0, Width = 960, Height = 1040 },
            new() { App = "Notes", X = -960, Y = 0, Width = 960, Height = 1040, Order = 1 },
            new() { App = "Editor", X = 0, Y = 0, Width = 1700, Height = 1400, Order = 2 },
            new() { App = "Terminal", X = 1700, Y = 0, Width = 860, Height = 700, Order = 3 },
            new() { App = "Mail", X = 1700, Y = 700, Width = 860, Height = 700, Order = 4 }
        } } }, Active = "Example work layout" };
        using var form = new ArrangementPreview(sample, _ => { }, (_, _) => { }, _ => { }, () => { });
        form.Show(); Application.DoEvents();
        using var image = new Bitmap(form.Width, form.Height); form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
        image.Save(System.IO.Path.Combine(AppContext.BaseDirectory, "preview-example.png")); form.Close();
        using var editor = new Form { Icon = AppIcon.Value, Text = "Visual location editor — example", Width = 1080, Height = 650 };
        var canvas = new LocationCanvas { Dock = DockStyle.Fill, Displays = displays, Selection = new Rectangle(-1800, 100, 1000, 800) }; editor.Controls.Add(canvas);
        editor.Show(); Application.DoEvents();
        using var locationImage = new Bitmap(editor.Width, editor.Height); editor.DrawToBitmap(locationImage, new Rectangle(Point.Empty, editor.Size));
        locationImage.Save(System.IO.Path.Combine(AppContext.BaseDirectory, "location-editor-example.png")); editor.Close();
    }
}
