namespace JetPane;

// Shared state transitions used by the UI and deterministic regression tests.
static class StateRules
{
    public static void LeaveSnap(Config config)
    {
        config.Snap = 0;
        config.Locks.Clear();
    }
    public static void AssignProfileShortcut(Config config, Profile current, int number)
    {
        void Apply(Config c, Profile selected)
        {
            if (number > 0)
                foreach (var profile in c.Profiles.Where(p => p.Shortcut == number))
                {
                    profile.Shortcut = 0;
                    c.Shortcuts.Remove("profile:" + profile.Name);
                }
            selected.Shortcut = number;
            c.Shortcuts.Remove("profile:" + selected.Name);
        }
        var candidate = new Config
        {
            Profiles = config.Profiles.Select(p => new Profile { Name = p.Name, Shortcut = p.Shortcut }).ToList(),
            Locations = config.Locations,
            Shortcuts = new(config.Shortcuts)
        };
        Apply(candidate, candidate.Profiles.First(p => p.Name == current.Name));
        ShortcutKeys.Validate(ShortcutKeys.Actions(candidate).Select(a => (a.Id, ShortcutKeys.Binding(candidate, a.Id, a.Default))), candidate);
        Apply(config, current);
    }

    public static IEnumerable<LiveWindow> ClosedWindows(Config config, IEnumerable<LiveWindow> previous, ISet<nint> current)
        => previous.Where(w => !current.Contains(w.Handle) && !config.Excluded.Contains(w.App, StringComparer.OrdinalIgnoreCase));

    public static bool TakePending(Dictionary<nint, DateTime> pending, nint handle, DateTime now, bool defer)
    {
        if (!pending.TryGetValue(handle, out var due) || due > now || defer) return false;
        pending.Remove(handle);
        return true;
    }
}
