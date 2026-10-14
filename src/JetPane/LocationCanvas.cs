namespace JetPane;

sealed class LocationCanvas : ArrangementCanvas
{
    public Rectangle Selection;
    public Action<Rectangle>? SelectionChanged;
    Point start;
    Rectangle initial;
    int handle = -2;
    bool dragging;
    public LocationCanvas()
    {
        Cursor = Cursors.Cross; TabStop = true;
        Caption = "Draw on a monitor. Drag inside to move; drag a handle to resize.";
        AccessibleName = "Visual location editor";
        AccessibleDescription = "Use the X, Y, Width and Height fields for keyboard-accessible precise editing.";
    }
    PointF[] Handles()
    {
        var r = MapRectangle(Selection);
        return new[] { new PointF(r.Left, r.Top), new(r.Left + r.Width / 2, r.Top), new(r.Right, r.Top), new(r.Right, r.Top + r.Height / 2), new(r.Right, r.Bottom), new(r.Left + r.Width / 2, r.Bottom), new(r.Left, r.Bottom), new(r.Left, r.Top + r.Height / 2) };
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        Windows = new() { new Placement { App = "Saved location", X = Selection.X, Y = Selection.Y, Width = Selection.Width, Height = Selection.Height } };
        base.OnPaint(e);
        using var fill = new SolidBrush(Color.White); using var outline = new Pen(Color.FromArgb(15, 58, 110), 2);
        foreach (var p in Handles()) { var r = new RectangleF(p.X - 5, p.Y - 5, 10, 10); e.Graphics.FillRectangle(fill, r); e.Graphics.DrawRectangle(outline, r.X, r.Y, r.Width, r.Height); }
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e); if (e.Button != MouseButtons.Left) return;
        handle = Array.FindIndex(Handles(), p => Math.Abs(p.X - e.X) <= 9 && Math.Abs(p.Y - e.Y) <= 9);
        var point = DesktopPoint(e.Location); if (handle < 0 && !Displays.Any(d => d.Work.Contains(point))) return;
        if (handle < 0) handle = MapRectangle(Selection).Contains(e.Location) ? -1 : -2;
        start = point; initial = Selection; dragging = true; Capture = true;
    }
    public static Rectangle Drag(Rectangle initial, Point start, Point point, int handle)
    {
        int dx = point.X - start.X, dy = point.Y - start.Y;
        if (handle == -1) return new(initial.X + dx, initial.Y + dy, initial.Width, initial.Height);
        if (handle == -2) return new(Math.Min(start.X, point.X), Math.Min(start.Y, point.Y), Math.Max(100, Math.Abs(dx)), Math.Max(100, Math.Abs(dy)));
        int left = initial.Left, top = initial.Top, right = initial.Right, bottom = initial.Bottom;
        if (handle is 0 or 6 or 7) left = Math.Min(right - 100, left + dx);
        if (handle is 2 or 3 or 4) right = Math.Max(left + 100, right + dx);
        if (handle is 0 or 1 or 2) top = Math.Min(bottom - 100, top + dy);
        if (handle is 4 or 5 or 6) bottom = Math.Max(top + 100, bottom + dy);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!dragging) return;
        Selection = Drag(initial, start, DesktopPoint(e.Location), handle); SelectionChanged?.Invoke(Selection); Invalidate();
    }
    protected override void OnMouseUp(MouseEventArgs e)
    { base.OnMouseUp(e); dragging = false; Capture = false; }
}
