using System.Text.Json;

namespace JetPane;

static partial class SelfTest
{
    public static void RunStateTests()
    {
        var results = new List<string>();
        try { StateTests.Run((ok, name) => { if (!ok) throw new Exception("FAIL: " + name); results.Add("PASS: " + name); }); }
        catch (Exception e) { results.Add(e.ToString()); Environment.ExitCode = 1; }
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "state-test.txt"), results);
    }
}

static class StateTests
{
    static Config Reload(Config config)
    {
        string path = Path.Combine(Path.GetTempPath(), "jetpane-state-" + Guid.NewGuid().ToString("N") + ".json");
        try { File.WriteAllText(path, JsonSerializer.Serialize(config)); return Pilot.ReadConfig(path); }
        finally { File.Delete(path); }
    }
    public static void Run(Action<bool, string> check)
    {
        var c = new Config { Snap = 4 }; c.Locks[3] = "test";
        StateRules.LeaveSnap(c);
        check(c.Snap == 0 && c.Locks.Count == 0 && Reload(c).Locks.Count == 0, "Leaving quadrants clears locks and survives reload");
        c = new Config { Snap = 3 }; c.Locks[2] = "test"; StateRules.LeaveSnap(c);
        check(Reload(c).Snap == 0, "Leaving thirds survives reload");
        c = new Config { Snap = 0 }; c.Locks[3] = "test";
        check(Reload(c).Locks.Count == 0, "Legacy inactive snap locks recover without losing profiles");
        c = new Config { Snap = 1 }; c.Locks[3] = "test";
        bool rejected = false; try { Reload(c); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "Active layout still rejects out-of-range locks");
        c = new Config(); c.Profiles[0].Name = "default";
        rejected = false; try { Reload(c); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "Reserved Default spelling rejected before import can persist duplicates");
        c = new Config(); c.Profiles[0].Name = "Work";
        check(Reload(Reload(c)).Profiles.Count == 2, "Inserted Default remains valid across repeated reloads");
        c = new Config(); var work = new Profile { Name = "Work" }; var other = new Profile { Name = "Other" };
        c.Profiles.AddRange(new[] { work, other }); c.Shortcuts["profile:Work"] = "Ctrl+Alt+F8"; c.Shortcuts["profile:Other"] = "Ctrl+Alt+F9";
        StateRules.AssignProfileShortcut(c, c.Profiles[0], 0);
        check(c.Shortcuts.Count == 2, "None preserves unrelated custom profile bindings");
        StateRules.AssignProfileShortcut(c, work, 0);
        check(!c.Shortcuts.ContainsKey("profile:Work") && c.Shortcuts.ContainsKey("profile:Other"), "None clears only the selected custom profile binding");
        work.Shortcut = 2; StateRules.AssignProfileShortcut(c, c.Profiles[0], 2);
        check(work.Shortcut == 0 && c.Profiles[0].Shortcut == 2 && c.Shortcuts.ContainsKey("profile:Other"), "Numeric reassignment releases only its conflicting profile");
        var now = DateTime.UtcNow; var pending = new Dictionary<nint, DateTime> { [1] = now.AddSeconds(-1) };
        check(!StateRules.TakePending(pending, 1, now, true) && pending.ContainsKey(1), "Quiet period retains a due restore");
        check(StateRules.TakePending(pending, 1, now, false) && !pending.ContainsKey(1), "Deferred restore runs after quiet period");
        check(!StateRules.TakePending(pending, 1, now, false), "Completed restore cannot run twice");
        pending[1] = now.AddSeconds(1);
        check(!StateRules.TakePending(pending, 1, now, false) && pending.ContainsKey(1), "Settling delay retains future restore");
        c = new Config(); c.Excluded.Add("excluded");
        var old = new[] { new LiveWindow(1, "Excluded", "", new()), new LiveWindow(2, "closed", "", new()), new LiveWindow(3, "open", "", new()) };
        var closed = StateRules.ClosedWindows(c, old, new HashSet<nint> { 3 }).ToList();
        check(closed.Count == 1 && closed[0].App == "closed", "Auto-save ignores excluded windows and still saves genuinely closed apps");
        c = new Config(); c.Profiles[0].Shortcut = 1; c.Shortcuts["snap:1"] = "Ctrl+Alt+NumPad1";
        rejected = false; try { Reload(c); } catch (ArgumentException) { rejected = true; }
        check(rejected, "Implicit profile numpad collision rejected during settings validation");
        c.Profiles[0].Shortcut = 0;
        rejected = false; try { StateRules.AssignProfileShortcut(c, c.Profiles[0], 1); } catch (ArgumentException) { rejected = true; }
        check(rejected && c.Profiles[0].Shortcut == 0 && c.Shortcuts["snap:1"] == "Ctrl+Alt+NumPad1", "Conflicting numeric menu assignment is rejected without changing settings");
        c.Profiles[0].Shortcut = 1;
        c.Shortcuts["profile:Default"] = "Ctrl+Alt+1";
        check(Reload(c).Shortcuts.Count == 2, "Explicit profile binding has no implicit numpad alias");
        check(ShortcutKeys.EffectiveBindings(new Config(), "profile:Default", "Ctrl+Alt+1").Count() == 2, "Legacy registration expands numeric and numpad bindings");
        check(ShortcutKeys.EffectiveBindings(c, "profile:Default", "Ctrl+Alt+1").Count() == 1, "Custom registration uses the same exact binding rules as validation");
    }
}
