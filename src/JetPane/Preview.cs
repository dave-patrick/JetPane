using System.Drawing.Drawing2D;

namespace JetPane;

public class DisplayGeometry
{
    public string Name { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int WorkX { get; set; }
    public int WorkY { get; set; }
    public int WorkWidth { get; set; }
    public int WorkHeight { get; set; }
    public Rectangle Bounds => new(X, Y, Width, Height);
    public Rectangle Work => WorkWidth > 0 && WorkHeight > 0 ? new(WorkX, WorkY, WorkWidth, WorkHeight) : Bounds;
    public static List<DisplayGeometry> Capture() => Screen.AllScreens.Select(s => new DisplayGeometry
    {
        Name = s.DeviceName, X = s.Bounds.X, Y = s.Bounds.Y, Width = s.Bounds.Width, Height = s.Bounds.Height,
        WorkX = s.WorkingArea.X, WorkY = s.WorkingArea.Y, WorkWidth = s.WorkingArea.Width, WorkHeight = s.WorkingArea.Height
    }).ToList();
}

class ArrangementCanvas : Control
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public List<DisplayGeometry> Displays { get; set; } = new();
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public List<Placement> Windows { get; set; } = new();
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public int SnapLayout { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public Dictionary<int, string> Locks { get; set; } = new();
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public string Caption { get; set; } = "";
    public ArrangementCanvas()
    {
        DoubleBuffered = true; BackColor = Color.FromArgb(239, 243, 248);
        AccessibleName = "Arrangement diagram"; AccessibleDescription = "Monitor geometry and labeled window positions. The application list below provides the same information as text.";
    }
    static Color AppColor(string name)
    {
        Color[] palette = { Color.FromArgb(29, 79, 132), Color.FromArgb(30, 107, 95), Color.FromArgb(112, 63, 124), Color.FromArgb(140, 78, 28), Color.FromArgb(117, 57, 78) };
        uint hash = 2166136261; foreach (char c in name.ToLowerInvariant()) hash = (hash ^ c) * 16777619;
        return palette[hash % palette.Length];
    }
    protected (double Scale, float X, float Y, Rectangle Bounds) Transform()
    {
        var bounds = Displays.Count == 0 ? new Rectangle(0, 0, 1920, 1080) : Displays.Select(d => d.Bounds).Aggregate(Rectangle.Union);
        double scale = Math.Min(Math.Max(1, Width - 70) / (double)Math.Max(1, bounds.Width), Math.Max(1, Height - 120) / (double)Math.Max(1, bounds.Height));
        return (scale, (float)((Width - bounds.Width * scale) / 2), (float)(68 + (Height - 120 - bounds.Height * scale) / 2), bounds);
    }
    protected RectangleF MapRectangle(Rectangle r)
    {
        var t = Transform(); return new(t.X + (float)((r.X - t.Bounds.X) * t.Scale), t.Y + (float)((r.Y - t.Bounds.Y) * t.Scale), (float)(r.Width * t.Scale), (float)(r.Height * t.Scale));
    }
    protected Point DesktopPoint(Point p)
    { var t = Transform(); return new(t.Bounds.X + (int)Math.Round((p.X - t.X) / t.Scale), t.Bounds.Y + (int)Math.Round((p.Y - t.Y) / t.Scale)); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        using var headingFont = new Font("Segoe UI", 10, FontStyle.Bold);
        using var labelFont = new Font("Segoe UI", 9);
        using var brush = new SolidBrush(Color.FromArgb(32, 49, 68));
        g.DrawString(Caption, headingFont, brush, new RectangleF(20, 12, Width - 40, 48));
        if (Displays.Count == 0) { g.DrawString("No monitor geometry is available.", labelFont, brush, 20, 70); return; }
        var bounds = Displays.Select(d => d.Bounds).Aggregate(Rectangle.Union);
        double scale = Math.Min(Math.Max(1, Width - 70) / (double)Math.Max(1, bounds.Width), Math.Max(1, Height - 120) / (double)Math.Max(1, bounds.Height));
        float offsetX = (float)((Width - bounds.Width * scale) / 2), offsetY = (float)(68 + (Height - 120 - bounds.Height * scale) / 2);
        RectangleF Map(Rectangle r) => new(offsetX + (float)((r.X - bounds.X) * scale), offsetY + (float)((r.Y - bounds.Y) * scale), (float)(r.Width * scale), (float)(r.Height * scale));
        for (int index = 0; index < Displays.Count; index++)
        {
            var display = Displays[index]; var frame = Map(display.Bounds); var work = Map(display.Work);
            using var monitorFill = new SolidBrush(Color.FromArgb(214, 224, 237)); g.FillRectangle(monitorFill, frame);
            using var workFill = new SolidBrush(Color.FromArgb(250, 252, 255)); g.FillRectangle(workFill, work);
            var saved = g.Save(); g.SetClip(work);
            if (SnapLayout > 0)
            {
                var slots = Pilot.BuildLayout(display.Work, SnapLayout);
                for (int i = 0; i < slots.Length; i++)
                {
                    string text = $"Slot {i + 1}" + (Locks.TryGetValue(i, out string? app) ? " · Locked: " + app : "");
                    PaintWindow(g, Map(slots[i]), text, AppColor("slot" + i), labelFont);
                }
            }
            else
            {
                foreach (var p in Windows.Where(p => !p.Minimized).OrderByDescending(p => p.Order))
                {
                    var rect = new Rectangle(p.X, p.Y, p.Width, p.Height);
                    if (p.Maximized)
                    {
                        var owner = Displays.OrderByDescending(d => IntersectionArea(d.Bounds, rect)).First();
                        rect = owner.Work;
                    }
                    if (rect.IntersectsWith(display.Bounds)) PaintWindow(g, Map(rect), p.App + (p.Maximized ? " · Max" : ""), AppColor(p.App), labelFont);
                }
            }
            g.Restore(saved);
            using var outline = new Pen(Color.FromArgb(57, 79, 105), 2); g.DrawRectangle(outline, frame.X, frame.Y, frame.Width, frame.Height);
            g.DrawString($"Monitor {index + 1}  ·  {display.Width} × {display.Height}", labelFont, brush, new RectangleF(frame.X, frame.Bottom + 5, Math.Max(frame.Width, 130), 28));
        }
    }
    static long IntersectionArea(Rectangle a, Rectangle b) { var r = Rectangle.Intersect(a, b); return (long)Math.Max(0, r.Width) * Math.Max(0, r.Height); }
    static void PaintWindow(Graphics g, RectangleF rect, string text, Color color, Font font)
    {
        rect.Inflate(-2, -2); if (rect.Width <= 1 || rect.Height <= 1) return;
        using var fill = new SolidBrush(Color.FromArgb(225, color)); g.FillRectangle(fill, rect);
        using var border = new Pen(Color.White, 1); g.DrawRectangle(border, rect.X, rect.Y, rect.Width, rect.Height);
        using var foreground = new SolidBrush(Color.White);
        using var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter };
        g.DrawString(text, font, foreground, new RectangleF(rect.X + 6, rect.Y + 6, Math.Max(1, rect.Width - 12), Math.Max(1, rect.Height - 12)), format);
    }
}

sealed class ArrangementPreview : Form
{
    public ArrangementPreview(Config config, Action<string> applyProfile, Action<int, string> applySnap, Action<string> applyLocation, Action help)
    {
        Text = "JetPane — Arrangement preview"; Icon = AppIcon.Value; Width = 1120; Height = 820;
        MinimumSize = new Size(820, 650); StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10); BackColor = Color.White;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = new Padding(16) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 235)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 154)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54)); Controls.Add(root);
        var title = new Label { Text = "Arrangement preview\nSelect a layout to inspect it. Apply only when you're ready.", Dock = DockStyle.Fill, AutoSize = false };
        root.Controls.Add(title, 0, 0); root.SetColumnSpan(title, 2);
        var choices = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, AccessibleName = "Profiles and snap layouts" };
        foreach (var p in config.Profiles) choices.Items.Add("Profile: " + p.Name);
        string[] names = { "Left / right", "Top / bottom", "Three columns", "Four quadrants" };
        foreach (var name in names) choices.Items.Add("Snap: " + name);
        foreach (var location in config.Locations) choices.Items.Add("Location: " + location.Name);
        root.Controls.Add(choices, 0, 1);
        var canvas = new ArrangementCanvas { Dock = DockStyle.Fill, Margin = new Padding(12, 0, 0, 0) }; root.Controls.Add(canvas, 1, 1);
        var details = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, AccessibleName = "Arrangement details" };
        root.Controls.Add(details, 0, 2); root.SetColumnSpan(details, 2);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0), WrapContents = false };
        root.Controls.Add(footer, 0, 3); root.SetColumnSpan(footer, 2);
        var monitorLabel = new Label { Text = "Snap monitor:", AutoSize = true, Margin = new Padding(0, 6, 6, 0) };
        var monitors = new ComboBox { Width = 245, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Snap target monitor" };
        var current = DisplayGeometry.Capture();
        monitors.Items.AddRange(current.Select((d, i) => $"Monitor {i + 1}: {d.Width} × {d.Height}").Cast<object>().ToArray());
        monitors.SelectedIndex = Math.Max(0, current.FindIndex(d => d.Name == config.SnapMonitor));
        var apply = new Button { Text = "Apply arrangement", AutoSize = true, Height = 32 };
        var helpButton = new Button { Text = "Help", AutoSize = true, Height = 32 }; helpButton.Click += (_, _) => help();
        var close = new Button { Text = "Close", AutoSize = true, Height = 32 }; close.Click += (_, _) => Close();
        footer.Controls.AddRange(new Control[] { monitorLabel, monitors, apply, helpButton, close });
        void RefreshPreview()
        {
            int selected = choices.SelectedIndex; if (selected < 0) return;
            bool profile = selected < config.Profiles.Count; monitorLabel.Visible = monitors.Visible = !profile;
            if (profile)
            {
                var p = config.Profiles[selected]; bool legacy = p.Displays.Count == 0;
                canvas.Displays = legacy ? current : p.Displays; canvas.Windows = p.Windows; canvas.SnapLayout = 0;
                canvas.Caption = p.Name + " · " + canvas.Displays.Count + " monitors · " + p.Windows.Count + " applications";
                details.Text = (legacy ? "Older profile: using current monitor geometry. Save this profile again to record its monitors.\r\n" : "Saved monitor geometry. If monitors are missing, applying will place off-screen windows on connected displays.\r\n")
                    + (p.Windows.Count == 0 ? "No windows saved. Arrange your desktop and save this profile." : string.Join("\r\n", p.Windows.OrderBy(w => w.Order).Select(w => $"{w.App} — {w.Width} × {w.Height} at ({w.X}, {w.Y}) — " + (w.Minimized ? "minimized (not drawn)" : w.Maximized ? "maximized" : "normal") + $" — stack {w.Order + 1}")));
                apply.Enabled = p.Windows.Count > 0;
            }
            else if (selected < config.Profiles.Count + 4)
            {
                int layout = selected - config.Profiles.Count + 1;
                canvas.Displays = new() { current[monitors.SelectedIndex] }; canvas.Windows = new(); canvas.SnapLayout = layout;
                canvas.Locks = config.Snap == layout ? config.Locks : new(); canvas.Caption = names[layout - 1] + " · " + monitors.SelectedItem;
                details.Text = "Slot numbers match the lock menu. Open applications will be arranged when you click Apply.\r\n"
                    + string.Join("\r\n", Enumerable.Range(0, Pilot.BuildLayout(canvas.Displays[0].Work, layout).Length).Select(i => $"Slot {i + 1}: " + (canvas.Locks.TryGetValue(i, out string? app) ? "reserved for " + app : "available")))
                    + "\r\nOther monitors are not tiled by this snap layout.";
                apply.Enabled = true;
            }
            else
            {
                var location = config.Locations[selected - config.Profiles.Count - 4];
                canvas.Displays = location.Displays.Count > 0 ? location.Displays : current;
                var p = location.Position; canvas.Windows = new() { new Placement { App = location.Name, X = p.X, Y = p.Y, Width = p.Width, Height = p.Height } };
                canvas.SnapLayout = 0; canvas.Caption = location.Name + " · Saved location";
                details.Text = $"Move the active application window to {p.Width} × {p.Height} at ({p.X}, {p.Y}).\r\nEdit this region in Settings → Locations. A location shortcut moves one window; a profile loads an entire arrangement.";
                monitorLabel.Visible = monitors.Visible = false; apply.Enabled = true;
            }
            canvas.AccessibleDescription = canvas.Caption + ". " + details.Text; canvas.Invalidate();
        }
        choices.SelectedIndexChanged += (_, _) => RefreshPreview(); monitors.SelectedIndexChanged += (_, _) => RefreshPreview();
        apply.Click += (_, _) =>
        {
            if (choices.SelectedIndex < config.Profiles.Count) applyProfile(config.Profiles[choices.SelectedIndex].Name);
            else if (choices.SelectedIndex < config.Profiles.Count + 4) applySnap(choices.SelectedIndex - config.Profiles.Count + 1, current[monitors.SelectedIndex].Name);
            else applyLocation(config.Locations[choices.SelectedIndex - config.Profiles.Count - 4].Id);
            RefreshPreview();
        };
        choices.SelectedIndex = Math.Max(0, config.Profiles.FindIndex(p => p.Name == config.Active));
    }
}
