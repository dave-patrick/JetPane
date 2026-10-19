using System.Diagnostics;

namespace JetPane;

static class ResponsivenessTests
{
    // Runs only in a separately owned verification process, never a user application.
    public static void RunProbe(string ready, bool stall)
    {
        using var window = new Form { Text = "JetPane owned responsiveness probe", Width = 600, Height = 400 };
        window.Show(); Native.ShowWindow(window.Handle, 4); Application.DoEvents();
        File.WriteAllText(ready + ".tmp", window.Handle.ToString());
        File.Move(ready + ".tmp", ready);
        if (stall) Thread.Sleep(6000);
        Application.Run(window);
    }
    static void WithProbe(bool stall, Action<nint> test)
    {
        string ready = Path.Combine(Path.GetTempPath(), "jetpane-probe-" + Guid.NewGuid().ToString("N"));
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        start.ArgumentList.Add(stall ? "--hung-window-probe" : "--window-probe"); start.ArgumentList.Add(ready);
        using var child = Process.Start(start) ?? throw new Exception("Could not start owned responsiveness probe.");
        try
        {
            var wait = Stopwatch.StartNew();
            while (!File.Exists(ready) && !child.HasExited && wait.ElapsedMilliseconds < 15000) Thread.Sleep(20);
            if (!File.Exists(ready)) throw new Exception("Owned responsiveness probe did not become ready.");
            test((nint)long.Parse(File.ReadAllText(ready)));
        }
        finally
        {
            if (!child.HasExited) { child.Kill(); child.WaitForExit(); }
            File.Delete(ready); File.Delete(ready + ".tmp");
        }
    }
    public static void Run(Action<bool, string> check)
    {
        WithProbe(true, hwnd =>
        {
            var timer = Stopwatch.StartNew();
            bool queued = Native.Move(hwnd, new Placement { X = 100, Y = 100, Width = 600, Height = 400 }, false);
            check(queued && timer.ElapsedMilliseconds < 1000, "Moving an unresponsive owned app does not block the UI thread");
            timer.Restart(); Native.ShowWindow(hwnd, 6);
            check(timer.ElapsedMilliseconds < 1000, "Minimizing an unresponsive owned app does not block the UI thread");
            timer.Restart(); Native.SetWindowPos(hwnd, 0, 0, 0, 0, 0, 0x13);
            check(timer.ElapsedMilliseconds < 1000, "Reordering an unresponsive owned app does not block the UI thread");
            timer.Restart(); var windows = Native.Scan(Array.Empty<string>());
            check(timer.ElapsedMilliseconds < 1000 && windows.Any(w => w.Handle == hwnd && w.Path.Length > 0), $"Scanning an unresponsive owned app avoids module inspection and retains executable identity ({timer.ElapsedMilliseconds} ms, found={windows.Any(w => w.Handle == hwnd)}, path={windows.FirstOrDefault(w => w.Handle == hwnd)?.Path.Length ?? 0})");
        });
        WithProbe(false, hwnd =>
        {
            var target = Native.SafeBounds(new Placement { X = 100, Y = 100, Width = 650, Height = 450 });
            bool queued = Native.Move(hwnd, new Placement { X = target.X, Y = target.Y, Width = target.Width, Height = target.Height }, false);
            var timer = Stopwatch.StartNew(); bool placed = false;
            while (timer.ElapsedMilliseconds < 2000)
            {
                if (Native.GetWindowRect(hwnd, out var rect) && rect.Left == target.X && rect.Top == target.Y && rect.Right - rect.Left == target.Width && rect.Bottom - rect.Top == target.Height) { placed = true; break; }
                Thread.Sleep(20);
            }
            check(queued && placed, "Queued placement completes on a responsive owned external window");
            var state = new Placement { X = target.X, Y = target.Y, Width = target.Width, Height = target.Height, Minimized = true };
            Native.Move(hwnd, state, true); timer.Restart();
            while (!Native.IsIconic(hwnd) && timer.ElapsedMilliseconds < 2000) Thread.Sleep(20);
            check(Native.IsIconic(hwnd), "Queued external placement preserves minimized state");
            state.Minimized = false; state.Maximized = true; Native.Move(hwnd, state, false); timer.Restart();
            while (!Native.IsZoomed(hwnd) && timer.ElapsedMilliseconds < 2000) Thread.Sleep(20);
            check(Native.IsZoomed(hwnd), "Queued external placement restores maximized state");
        });
    }
}


