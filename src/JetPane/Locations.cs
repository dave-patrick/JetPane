namespace JetPane;

public class SavedLocation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public Placement Position { get; set; } = new();
    public List<DisplayGeometry> Displays { get; set; } = new();
}

sealed partial class Pilot
{
    nint lastExternalWindow;
    void MoveToLocation(string id)
    {
        var location = config.Locations.FirstOrDefault(l => l.Id == id); if (location == null) return;
        var live = Windows();
        var target = live.FirstOrDefault(w => w.Handle == Native.GetForegroundWindow()) ?? live.FirstOrDefault(w => w.Handle == lastExternalWindow);
        if (target == null) { Notify("Select an application window first, then press the location shortcut."); return; }
        // A deliberate one-window placement takes priority over automatic snap enforcement.
        pending.Remove(target.Handle);
        foreach (int slot in slots.Where(kv => kv.Value == target.Handle).Select(kv => kv.Key).ToList()) slots.Remove(slot);
        if (!Native.Move(target.Handle, location.Position, false)) Notify("Could not move " + target.App + ". It may be running as administrator.");
        else Notify(target.App + " → " + location.Name);
        quietTicks = 2;
    }
    void LocationsTab(TabControl tabs)
    {
        var tab = new TabPage("Locations"); tabs.TabPages.Add(tab);
        var list = new ListBox { Dock = DockStyle.Fill, DisplayMember = "Name", AccessibleName = "Saved locations" };
        var info = new Label { Dock = DockStyle.Top, Height = 60, Text = "Save a named rectangle on any monitor. Assign its shortcut in Keyboard shortcuts.\nThe shortcut moves the active application window into that rectangle." };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45 };
        var add = new Button { Text = "New location…", AutoSize = true }; var edit = new Button { Text = "Edit…", AutoSize = true }; var remove = new Button { Text = "Delete…", AutoSize = true };
        void Refresh() { list.Items.Clear(); list.Items.AddRange(config.Locations.Cast<object>().ToArray()); }
        void Edit(SavedLocation? existing)
        {
            using var f = new Form { Icon = AppIcon.Value, Text = existing == null ? "New location" : "Edit location", Width = 910, Height = 830, StartPosition = FormStartPosition.CenterParent, MinimumSize = new Size(700, 650) };
            var name = new TextBox { Left = 150, Top = 20, Width = 365, Text = existing?.Name ?? "" };
            var screens = DisplayGeometry.Capture();
            var monitor = new ComboBox { Left = 150, Top = 60, Width = 365, DropDownStyle = ComboBoxStyle.DropDownList };
            monitor.Items.AddRange(screens.Select((s, i) => $"Monitor {i + 1}: {s.Width} × {s.Height}").Cast<object>().ToArray()); monitor.SelectedIndex = 0;
            var region = new ComboBox { Left = 150, Top = 100, Width = 365, DropDownStyle = ComboBoxStyle.DropDownList };
            region.Items.AddRange(new object[] { "Custom rectangle", "Full work area", "Left half", "Right half", "Top half", "Bottom half", "Left third", "Middle third", "Right third", "Top left quarter", "Top right quarter", "Bottom left quarter", "Bottom right quarter" }); region.SelectedIndex = 0;
            var fields = new NumericUpDown[4]; string[] labels = { "X", "Y", "Width", "Height" };
            var canvas = new LocationCanvas { Left = 20, Top = 350, Width = 850, Height = 420, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right, Displays = screens };
            f.Controls.Add(canvas);
            bool updating = false;
            for (int i = 0; i < 4; i++)
            {
                fields[i] = new NumericUpDown { Left = 150 + i % 2 * 190, Top = 145 + i / 2 * 45, Width = 135, Minimum = i < 2 ? -100000 : 100, Maximum = 100000, AccessibleName = labels[i] };
                f.Controls.Add(new Label { Text = labels[i], Left = fields[i].Left - 55, Top = fields[i].Top + 4, Width = 55 }); f.Controls.Add(fields[i]);
            }
            void SetRect(Rectangle r) { updating = true; fields[0].Value = Math.Clamp(r.X, -100000, 100000); fields[1].Value = Math.Clamp(r.Y, -100000, 100000); fields[2].Value = Math.Clamp(r.Width, 100, 100000); fields[3].Value = Math.Clamp(r.Height, 100, 100000); updating = false; canvas.Selection = new((int)fields[0].Value, (int)fields[1].Value, (int)fields[2].Value, (int)fields[3].Value); canvas.Invalidate(); }
            canvas.SelectionChanged = r => { region.SelectedIndex = 0; SetRect(r); };
            foreach (var field in fields) field.ValueChanged += (_, _) => { if (!updating) { region.SelectedIndex = 0; canvas.Selection = new((int)fields[0].Value, (int)fields[1].Value, (int)fields[2].Value, (int)fields[3].Value); canvas.Invalidate(); } };
            void Preset()
            {
                int n = region.SelectedIndex; if (n <= 0) return;
                var area = screens[monitor.SelectedIndex].Work;
                Rectangle r = n == 1 ? area : n <= 3 ? BuildLocationPart(area, 1, n - 2) : n <= 5 ? BuildLocationPart(area, 2, n - 4) : n <= 8 ? BuildLocationPart(area, 3, n - 6) : BuildLocationPart(area, 4, n - 9);
                SetRect(r);
            }
            var initial = existing?.Position;
            SetRect(initial == null ? screens[0].Work : new Rectangle(initial.X, initial.Y, initial.Width, initial.Height));
            region.SelectedIndexChanged += (_, _) => Preset(); monitor.SelectedIndexChanged += (_, _) => Preset();
            var capture = new Button { Text = "Copy an open window's rectangle…", Left = 150, Top = 245, Width = 365 };
            capture.Click += (_, _) =>
            {
                var windows = Windows(); using var picker = new Form { Icon = AppIcon.Value, Text = "Choose window", Width = 420, Height = 360, StartPosition = FormStartPosition.CenterParent };
                var apps = new ListBox { Dock = DockStyle.Fill }; apps.Items.AddRange(windows.Select(w => w.App + $" ({w.Position.X}, {w.Position.Y})").Cast<object>().ToArray());
                var choose = new Button { Text = "Copy rectangle", Dock = DockStyle.Bottom, DialogResult = DialogResult.OK }; picker.Controls.Add(apps); picker.Controls.Add(choose);
                if (picker.ShowDialog(f) == DialogResult.OK && apps.SelectedIndex >= 0) { var p = windows[apps.SelectedIndex].Position; region.SelectedIndex = 0; SetRect(new(p.X, p.Y, p.Width, p.Height)); }
            };
            var save = new Button { Text = "Save location", Left = 275, Top = 300, Width = 130 }; var cancel = new Button { Text = "Cancel", Left = 415, Top = 300, Width = 100, DialogResult = DialogResult.Cancel };
            save.Click += (_, _) =>
            {
                string text = name.Text.Trim(); if (text.Length == 0 || config.Locations.Any(l => l != existing && l.Name.Equals(text, StringComparison.OrdinalIgnoreCase))) { MessageBox.Show("Enter a unique location name."); return; }
                var location = existing ?? new SavedLocation(); location.Name = text; location.Displays = screens;
                location.Position = new Placement { X = (int)fields[0].Value, Y = (int)fields[1].Value, Width = (int)fields[2].Value, Height = (int)fields[3].Value };
                if (existing == null) config.Locations.Add(location);
                Persist(); Hotkeys(); RebuildMenu(); f.DialogResult = DialogResult.OK; f.Close();
            };
            f.Controls.AddRange(new Control[] { new Label { Text = "Location name", Left = 20, Top = 24, Width = 125 }, name, new Label { Text = "Monitor", Left = 20, Top = 64 }, monitor, new Label { Text = "Preset", Left = 20, Top = 104 }, region, capture, save, cancel });
            f.AcceptButton = save; f.CancelButton = cancel; f.ShowDialog(tab.FindForm()); Refresh();
        }
        add.Click += (_, _) => Edit(null); edit.Click += (_, _) => { if (list.SelectedItem is SavedLocation location) Edit(location); };
        remove.Click += (_, _) => { if (list.SelectedItem is SavedLocation location && Confirm("Delete location " + location.Name + " and its shortcut?")) { config.Locations.Remove(location); config.Shortcuts.Remove("location:" + location.Id); Persist(); Hotkeys(); RebuildMenu(); Refresh(); } };
        bar.Controls.AddRange(new Control[] { add, edit, remove }); tab.Controls.Add(list); tab.Controls.Add(info); tab.Controls.Add(bar); Refresh();
    }
    public static Rectangle BuildLocationPart(Rectangle area, int layout, int slot) => BuildLayout(area, layout)[slot];
}
