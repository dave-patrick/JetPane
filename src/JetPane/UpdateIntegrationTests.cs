using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JetPane;

static class UpdateIntegrationTests
{
    public static bool IsFixture(string directory)
    {
        string full = Path.GetFullPath(directory);
        return (File.GetAttributes(full) & FileAttributes.ReparsePoint) == 0 && Path.GetDirectoryName(full)!.Equals(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
            && Regex.IsMatch(Path.GetFileName(full), "^JetPaneInstallTest-[a-f0-9]{32}$") && File.Exists(Path.Combine(full, ".fixture"));
    }
    public static void ParentProbe(string ready)
    {
        File.WriteAllText(ready, "ready"); var timer = Stopwatch.StartNew();
        while (!File.Exists(ready + ".exit") && timer.ElapsedMilliseconds < 30000) Thread.Sleep(20);
    }
    public static void Run(Action<bool, string> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "JetPaneInstallTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); File.WriteAllText(Path.Combine(root, ".fixture"), "owned updater test");
        string payload = Path.Combine(root, "payload"), helper = Path.Combine(root, "helper"); Directory.CreateDirectory(payload); Directory.CreateDirectory(helper);
        string ready = Path.Combine(root, "parent-ready"); Process? parent = null, installer = null;
        try
        {
            foreach (string name in Updater.Files) File.WriteAllText(Path.Combine(root, name), "original fixture " + name);
            File.Copy(Environment.ProcessPath!, Path.Combine(payload, "JetPane.exe"));
            foreach (string name in Updater.Files.Skip(1)) File.WriteAllText(Path.Combine(payload, name), "updated fixture " + name);
            var config = new Config { AutoRestore = false }; config.Shortcuts = new() { ["save"] = "Ctrl+Alt+Shift+F24", ["restore"] = "", ["preview"] = "" };
            string settings = JsonSerializer.Serialize(config); File.WriteAllText(Path.Combine(root, "settings.json"), settings);
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            start.ArgumentList.Add("--update-parent-probe"); start.ArgumentList.Add(ready); parent = Process.Start(start)!;
            var timer = Stopwatch.StartNew(); while (!File.Exists(ready) && timer.ElapsedMilliseconds < 15000) Thread.Sleep(20);
            if (!File.Exists(ready)) throw new Exception("Owned updater parent did not become ready.");
            var hashes = Updater.Files.ToDictionary(n => n, n => Updater.Hash(Path.Combine(payload, n)));
            var plan = new UpdatePlan(root, payload, parent.Id, parent.StartTime.ToUniversalTime().Ticks, hashes);
            string planFile = Path.Combine(helper, "plan.json"); File.WriteAllText(planFile, JsonSerializer.Serialize(plan));
            string helperExe = Path.Combine(helper, "JetPane-updater.exe"); File.Copy(Environment.ProcessPath!, helperExe);
            var run = new ProcessStartInfo(helperExe) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            run.ArgumentList.Add("--apply-update-test"); run.ArgumentList.Add(planFile); installer = Process.Start(run)!;
            Thread.Sleep(200);
            check(!parent.HasExited && File.ReadAllText(Path.Combine(root, "JetPane.exe")) == "original fixture JetPane.exe", "Separate installer leaves application files untouched while the parent is running");
            File.WriteAllText(ready + ".exit", "exit");
            if (!installer.WaitForExit(30000)) throw new Exception("Owned updater helper timed out.");
            string error = Path.Combine(root, "update.log");
            check(installer.ExitCode == 0, "Separate installer completes and restarts an isolated smoke instance" + (File.Exists(error) ? ": " + File.ReadAllText(error) : ""));
            check(Updater.Files.All(n => Updater.Hash(Path.Combine(root, n)) == hashes[n]), "Installer process replaces exactly the verified application files");
            // The smoke app persists settings on shutdown, so compare the normalized configuration too.
            var after = Pilot.ReadConfig(Path.Combine(root, "settings.json"));
            check(!after.AutoRestore && after.Shortcuts["save"] == config.Shortcuts["save"], "Restarted smoke instance retains the installed user configuration");
            string backup = Directory.GetDirectories(root, ".jetpane-backup-*").Single();
            check(File.ReadAllText(Path.Combine(backup, "JetPane.exe")) == "original fixture JetPane.exe" && File.ReadAllText(Path.Combine(backup, "settings.json")) == settings, "Installer process retains byte-for-byte recovery copies before restart");
        }
        finally
        {
            File.WriteAllText(ready + ".exit", "exit");
            foreach (var process in new[] { parent, installer }) if (process != null) { if (!process.HasExited) { process.Kill(); process.WaitForExit(); } process.Dispose(); }
            if (IsFixture(root)) Directory.Delete(root, true);
        }
    }
}
