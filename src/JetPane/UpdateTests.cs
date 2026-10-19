using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace JetPane;

static class UpdateTests
{
    static string Release(string version, bool preview = true, bool draft = false, bool complete = true, string host = "github.com") => JsonSerializer.Serialize(new
    {
        tag_name = version, draft, prerelease = preview, body = "Test release",
        assets = (complete ? new[] { "JetPane-portable.zip", "SHA256SUMS.txt" } : new[] { "JetPane-portable.zip" }).Select(name => new
        { name, id = name == "JetPane-portable.zip" ? 1 : 2, state = "uploaded", browser_download_url = $"https://{host}/dave-patrick/JetPane/releases/download/{version}/{name}" })
    });
    sealed class Response(string body, HttpStatusCode code = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) }); }
    }
    public static void Run(Action<bool, string> check)
    {
        bool Rejects(Action action) { try { action(); return false; } catch (InvalidDataException) { return true; } }
        string list = "[" + Release("v1.2.0") + "," + Release("v1.10.0") + "," + Release("v9.0.0", draft: true) + "]";
        var selected = Updater.SelectRelease(list, new Version(1, 1, 4, 0));
        check(selected?.Version == new Version(1, 10, 0), "Updater compares versions numerically and ignores draft releases");
        check(selected!.Archive.AbsoluteUri == "https://api.github.com/repos/dave-patrick/JetPane/releases/assets/1", "Updater downloads through the official release-asset API");
        check(selected!.Preview, "Updater discovers published prereleases");
        check(Updater.SelectRelease("[" + Release("v1.1.4") + "]", new Version(1, 1, 4, 0)) == null, "Updater does not offer the installed version");
        check(Updater.SelectRelease("[" + Release("v1.1.3") + "]", new Version(1, 1, 4)) == null, "Updater does not offer downgrades");
        check(Updater.SelectRelease("[" + Release("v2.0.0", complete: false) + "]", new Version(1, 0, 0)) == null, "Updater waits for complete release assets");
        check(Updater.SelectRelease("[" + Release("not-a-version") + "]", new Version(1, 0, 0)) == null, "Updater ignores unsupported release tags");
        check(Rejects(() => Updater.SelectRelease("[" + Release("v2.0.0", host: "example.com") + "]", new Version(1, 0, 0))), "Updater rejects download addresses outside its repository");
        string hash = new('A', 64);
        check(Updater.ReadChecksum(hash + "  JetPane-portable.zip\r\n") == hash, "Updater reads release SHA256 manifests");
        check(Rejects(() => Updater.ReadChecksum(hash + "  other.zip")), "Updater rejects a missing package checksum");
        check(Rejects(() => Updater.ReadChecksum(hash + "  JetPane-portable.zip\n" + hash + "  JetPane-portable.zip")), "Updater rejects ambiguous package checksums");
        using (var client = new HttpClient(new Response("[]")))
            check(Updater.CheckAsync(CancellationToken.None, client).GetAwaiter().GetResult() == null, "Updater handles empty release feeds asynchronously");
        using (var client = new HttpClient(new Response("[]", HttpStatusCode.Forbidden)))
        {
            bool failed = false; try { Updater.CheckAsync(CancellationToken.None, client).GetAwaiter().GetResult(); } catch (HttpRequestException) { failed = true; }
            check(failed, "Updater reports failed HTTP checks without claiming success");
        }
        using (var client = new HttpClient(new Response("[]")))
        {
            bool canceled = false; using var token = new CancellationTokenSource(); token.Cancel();
            try { Updater.CheckAsync(token.Token, client).GetAwaiter().GetResult(); } catch (OperationCanceledException) { canceled = true; }
            check(canceled, "Updater cancellation stops a pending check");
        }
        string root = Path.Combine(Path.GetTempPath(), "JetPaneUpdateTests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            string Zip(string label, params string[] names)
            {
                string path = Path.Combine(root, label + ".zip");
                using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
                foreach (string name in names) { using var writer = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.UTF8); writer.Write("new " + name); }
                return path;
            }
            string archive = Zip("valid", Updater.Files);
            check(Rejects(() => Updater.ExtractVerified(archive, new string('0', 64), Path.Combine(root, "bad-hash"))), "Updater rejects a changed or corrupt download before extraction");
            string unsafeZip = Zip("traversal", Updater.Files.Concat(new[] { "../settings.json" }).ToArray());
            check(Rejects(() => Updater.ExtractVerified(unsafeZip, Updater.Hash(unsafeZip), Path.Combine(root, "traversal"))), "Updater rejects archive path traversal");
            string settingsZip = Zip("settings", Updater.Files.Concat(new[] { "settings.json" }).ToArray());
            check(Rejects(() => Updater.ExtractVerified(settingsZip, Updater.Hash(settingsZip), Path.Combine(root, "settings"))), "Updater never accepts settings from an update package");
            string duplicate = Zip("duplicate", Updater.Files.Concat(new[] { "jetpane.EXE" }).ToArray());
            check(Rejects(() => Updater.ExtractVerified(duplicate, Updater.Hash(duplicate), Path.Combine(root, "duplicate"))), "Updater rejects case-insensitive duplicate archive entries");
            string missing = Zip("missing", "JetPane.exe");
            check(Rejects(() => Updater.ExtractVerified(missing, Updater.Hash(missing), Path.Combine(root, "missing"))), "Updater rejects incomplete portable archives");
            string payload = Path.Combine(root, "payload"); var hashes = Updater.ExtractVerified(archive, Updater.Hash(archive), payload);
            check(hashes.Count == 4, "Updater extracts only its four approved application files");
            string target = Path.Combine(root, "installed"); Directory.CreateDirectory(target);
            foreach (string name in Updater.Files) File.WriteAllText(Path.Combine(target, name), "old " + name);
            File.WriteAllText(Path.Combine(target, "settings.json"), "private settings"); File.WriteAllText(Path.Combine(target, "personal.txt"), "keep");
            var plan = new UpdatePlan(target, payload, 0, 0, hashes);
            bool rolledBack = false;
            try { Updater.ApplyFiles(plan, name => { if (name == "README.md") throw new IOException("Injected interrupted install"); }); } catch (IOException) { rolledBack = true; }
            check(rolledBack && Updater.Files.All(name => File.ReadAllText(Path.Combine(target, name)) == "old " + name), "Updater rolls back partially replaced application files on failure");
            check(File.ReadAllText(Path.Combine(target, "settings.json")) == "private settings", "Failed updates preserve settings byte for byte");
            string backup = Updater.ApplyFiles(plan);
            check(Updater.Files.All(name => Updater.Hash(Path.Combine(target, name)) == hashes[name]), "Updater installs all verified files successfully");
            check(File.ReadAllText(Path.Combine(target, "settings.json")) == "private settings" && File.ReadAllText(Path.Combine(target, "personal.txt")) == "keep", "Successful updates preserve settings and unrelated user files");
            check(File.ReadAllText(Path.Combine(backup, "JetPane.exe")) == "old JetPane.exe" && File.ReadAllText(Path.Combine(backup, "settings.json")) == "private settings", "Updater retains the old application and settings in a recovery backup");
            File.AppendAllText(Path.Combine(payload, "JetPane.exe"), "tampered");
            check(Rejects(() => Updater.ApplyFiles(plan)), "Installer rechecks staged file hashes before changing the application");
            string ordinary = Path.Combine(root, "ordinary"); Directory.CreateDirectory(ordinary); Updater.DeleteStage(ordinary);
            check(Directory.Exists(ordinary), "Updater cleanup refuses unrelated directories");
        }
        finally { Directory.Delete(root, true); }
    }
}
