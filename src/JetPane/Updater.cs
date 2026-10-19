using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Net;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JetPane;

sealed record UpdateRelease(Version Version, string Tag, bool Preview, string Notes, Uri Page, Uri Archive, Uri Checksums);
sealed record UpdatePlan(string TargetDirectory, string PayloadDirectory, int ParentPid, long ParentStarted, Dictionary<string, string> Hashes);
sealed record PreparedUpdate(string Root, UpdatePlan Plan);

static class Updater
{
    internal static readonly string[] Files = { "JetPane.exe", "Help.html", "README.md", "LICENSE" };
    const long MaxDownload = 256L * 1024 * 1024;
    public static Version CurrentVersion => Normalize(typeof(Updater).Assembly.GetName().Version!);
    static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));
    static bool TryVersion(string tag, out Version version)
    {
        version = new(0, 0, 0);
        if (!Regex.IsMatch(tag, @"^v?\d+\.\d+\.\d+$") || !Version.TryParse(tag.TrimStart('v'), out var parsed)) return false;
        version = Normalize(parsed); return true;
    }
    static Uri AssetUri(string value, string tag, string name, long id)
    {
        var expected = "https://github.com/dave-patrick/JetPane/releases/download/" + tag + "/" + name;
        if (value != expected) throw new InvalidDataException("Unexpected update download address.");
        if (id <= 0) throw new InvalidDataException("Invalid release asset identifier.");
        return new("https://api.github.com/repos/dave-patrick/JetPane/releases/assets/" + id);
    }
    public static UpdateRelease? SelectRelease(string json, Version current)
    {
        using var document = JsonDocument.Parse(json);
        UpdateRelease? best = null;
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.GetProperty("draft").GetBoolean()) continue;
            string tag = item.GetProperty("tag_name").GetString() ?? "";
            if (!TryVersion(tag, out var version) || version <= Normalize(current) || (best != null && version <= best.Version)) continue;
            var assets = item.GetProperty("assets").EnumerateArray().Where(a => a.GetProperty("state").GetString() == "uploaded").ToList();
            JsonElement? Asset(string name) => assets.Where(a => a.GetProperty("name").GetString() == name).Select(a => (JsonElement?)a).SingleOrDefault();
            var archive = Asset("JetPane-portable.zip"); var checksum = Asset("SHA256SUMS.txt");
            if (archive == null || checksum == null) continue; // A release may still be uploading.
            Uri Download(JsonElement asset, string name) => AssetUri(asset.GetProperty("browser_download_url").GetString() ?? "", tag, name, asset.GetProperty("id").GetInt64());
            best = new(version, tag, item.GetProperty("prerelease").GetBoolean(), item.GetProperty("body").GetString() ?? "",
                new("https://github.com/dave-patrick/JetPane/releases/tag/" + tag), Download(archive.Value, "JetPane-portable.zip"), Download(checksum.Value, "SHA256SUMS.txt"));
        }
        return best;
    }
    static void RequireStandardUser()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new InvalidOperationException("Run JetPane normally, without administrator elevation, to install updates. Protected folders need a manual upgrade.");
    }
    static HttpClient Client()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("JetPane/" + CurrentVersion);
        return client;
    }
    static async Task CopyResponse(HttpClient client, Uri uri, Stream output, long limit, IProgress<int>? progress, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri) { Version = HttpVersion.Version20, VersionPolicy = HttpVersionPolicy.RequestVersionOrLower };
        if (uri.AbsolutePath.Contains("/releases/assets/", StringComparison.Ordinal)) request.Headers.Accept.ParseAdd("application/octet-stream");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var final = response.RequestMessage?.RequestUri ?? uri;
        if (final.Scheme != "https" || !(final.Host == "github.com" || final.Host == "api.github.com" || final.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Unexpected update server redirect.");
        long? length = response.Content.Headers.ContentLength;
        if (length > limit) throw new InvalidDataException("Update response exceeds the size limit.");
        await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        var buffer = new byte[81920]; long total = 0; int count;
        while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
        {
            total += count; if (total > limit) throw new InvalidDataException("Update response exceeds the size limit.");
            await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
            if (length > 0) progress?.Report((int)Math.Min(100, total * 100 / length.Value));
        }
    }
    static async Task<string> Text(HttpClient client, Uri uri, long limit, CancellationToken token)
    {
        using var output = new MemoryStream();
        await CopyResponse(client, uri, output, limit, null, token).ConfigureAwait(false);
        return System.Text.Encoding.UTF8.GetString(output.ToArray());
    }
    public static async Task<UpdateRelease?> CheckAsync(CancellationToken token, HttpClient? client = null)
    {
        using var owned = client == null ? Client() : null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        string json = await Text(client ?? owned!, new("https://api.github.com/repos/dave-patrick/JetPane/releases?per_page=30"), 1024 * 1024, timeout.Token).ConfigureAwait(false);
        return SelectRelease(json, CurrentVersion);
    }
    public static string ReadChecksum(string manifest)
    {
        var matches = Regex.Matches(manifest.TrimStart('\uFEFF'), @"(?m)^([a-fA-F0-9]{64})[ \t]+\*?JetPane-portable\.zip\r?$");
        if (matches.Count != 1) throw new InvalidDataException("The release checksum is missing or ambiguous.");
        return matches[0].Groups[1].Value;
    }
    internal static string Hash(string file) { using var input = File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(input)); }
    public static Dictionary<string, string> ExtractVerified(string archive, string expected, string payload)
    {
        if (!Hash(archive).Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Update checksum verification failed. Nothing was installed.");
        using var zip = ZipFile.OpenRead(archive);
        var allowed = Files.Concat(new[] { "state-test.txt", "self-test.txt", "smoke-test.txt" }).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
        foreach (var entry in zip.Entries)
        {
            if (!allowed.Contains(entry.FullName) || !seen.Add(entry.FullName) || entry.Length < 0 || (total += entry.Length) > MaxDownload)
                throw new InvalidDataException("Unexpected or unsafe file in the update archive.");
        }
        if (Files.Any(name => !seen.Contains(name))) throw new InvalidDataException("The update archive is incomplete.");
        Directory.CreateDirectory(payload);
        var hashes = new Dictionary<string, string>();
        foreach (string name in Files)
        {
            var entry = zip.Entries.Single(e => e.FullName.Equals(name, StringComparison.OrdinalIgnoreCase));
            string path = Path.Combine(payload, name);
            using (var input = entry.Open())
            using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            {
                var buffer = new byte[81920]; long written = 0; int count;
                while ((count = input.Read(buffer)) != 0)
                {
                    written += count;
                    if (written > entry.Length || written > MaxDownload) throw new InvalidDataException("Invalid decompressed update size.");
                    output.Write(buffer, 0, count);
                }
                if (written != entry.Length) throw new InvalidDataException("Incomplete update file.");
            }
            hashes[name] = Hash(path);
        }
        return hashes;
    }
    public static async Task<PreparedUpdate> DownloadAsync(UpdateRelease release, string target, IProgress<int> progress, CancellationToken token)
    {
        RequireStandardUser();
        string root = Path.Combine(Path.GetTempPath(), "JetPaneUpdate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); File.WriteAllText(Path.Combine(root, ".jetpane-update"), "1");
        try
        {
            using var client = Client();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromMinutes(5));
            var downloadToken = timeout.Token;
            string checksum = ReadChecksum(await Text(client, release.Checksums, 16384, downloadToken).ConfigureAwait(false));
            string archive = Path.Combine(root, "update.zip");
            await using (var output = File.Create(archive)) await CopyResponse(client, release.Archive, output, MaxDownload, progress, downloadToken).ConfigureAwait(false);
            string payload = Path.Combine(root, "payload");
            var hashes = await Task.Run(() => ExtractVerified(archive, checksum, payload), downloadToken).ConfigureAwait(false);
            downloadToken.ThrowIfCancellationRequested();
            var installed = FileVersionInfo.GetVersionInfo(Path.Combine(payload, "JetPane.exe"));
            if (!Version.TryParse(installed.FileVersion, out var actual) || Normalize(actual) != release.Version)
                throw new InvalidDataException("Downloaded application version does not match its release.");
            using var parent = Process.GetCurrentProcess();
            return new(root, new(Path.GetFullPath(target), payload, parent.Id, parent.StartTime.ToUniversalTime().Ticks, hashes));
        }
        catch { DeleteStage(root); throw; }
    }
    public static void StartInstaller(PreparedUpdate prepared)
    {
        RequireStandardUser();
        if (!Path.GetFileName(Environment.ProcessPath!).Equals("JetPane.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Keep the application named JetPane.exe to install updates in place.");
        string helper = Path.Combine(prepared.Root, "JetPane-updater.exe");
        File.Copy(Environment.ProcessPath!, helper, true); // An independent copy can replace the installed executable after it exits.
        string plan = Path.Combine(prepared.Root, "update.json"); File.WriteAllText(plan, JsonSerializer.Serialize(prepared.Plan));
        var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = prepared.Root };
        start.ArgumentList.Add("--apply-update"); start.ArgumentList.Add(plan);
        using var child = Process.Start(start) ?? throw new IOException("Could not start the update installer.");
    }
    static void Replace(string source, string destination)
    {
        string temporary = destination + ".jetpane-new-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(source, temporary);
            for (int attempt = 0; ; attempt++)
                try { File.Move(temporary, destination, true); break; }
                catch (IOException) when (attempt < 19) { Thread.Sleep(250); }
        }
        finally { File.Delete(temporary); }
    }
    internal static string ApplyFiles(UpdatePlan plan, Action<string>? beforeWrite = null)
    {
        string target = Path.GetFullPath(plan.TargetDirectory), payload = Path.GetFullPath(plan.PayloadDirectory);
        if (target == Path.GetPathRoot(target) || target == payload || plan.Hashes.Count != Files.Length || Files.Any(n => !plan.Hashes.ContainsKey(n)))
            throw new InvalidDataException("Invalid update installation plan.");
        foreach (string name in Files)
            if (!Hash(Path.Combine(payload, name)).Equals(plan.Hashes[name], StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Prepared update files changed. Nothing was installed.");
        using var installLock = new FileStream(Path.Combine(target, ".jetpane-update.lock"), FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
        string backup = Path.Combine(target, ".jetpane-backup-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(backup);
        var existing = Files.Where(n => File.Exists(Path.Combine(target, n))).ToHashSet();
        foreach (string name in existing.Concat(new[] { "settings.json" }).Distinct())
            if (File.Exists(Path.Combine(target, name))) File.Copy(Path.Combine(target, name), Path.Combine(backup, name));
        var changed = new List<string>();
        try
        {
            foreach (string name in Files)
            {
                beforeWrite?.Invoke(name); changed.Add(name);
                Replace(Path.Combine(payload, name), Path.Combine(target, name));
            }
            return backup;
        }
        catch (Exception failure)
        {
            var errors = new List<Exception> { failure };
            foreach (string name in changed.AsEnumerable().Reverse())
                try { if (existing.Contains(name)) Replace(Path.Combine(backup, name), Path.Combine(target, name)); else File.Delete(Path.Combine(target, name)); }
                catch (Exception e) { errors.Add(e); }
            if (errors.Count > 1) throw new AggregateException("Update failed; some files need recovery from " + backup, errors);
            throw;
        }
    }
    public static int RunInstaller(string file, bool verification = false)
    {
        UpdatePlan? plan = null;
        try
        {
            if (!verification) RequireStandardUser();
            plan = JsonSerializer.Deserialize<UpdatePlan>(File.ReadAllText(file)) ?? throw new InvalidDataException("Empty update plan.");
            if (verification && !UpdateIntegrationTests.IsFixture(plan.TargetDirectory)) return 1;
            try
            {
                using var parent = Process.GetProcessById(plan.ParentPid);
                if (parent.StartTime.ToUniversalTime().Ticks != plan.ParentStarted || !parent.WaitForExit(60000)) throw new IOException("JetPane did not exit. The update was not installed.");
            }
            catch (ArgumentException) { /* Parent already exited. */ }
            ApplyFiles(plan);
            var start = new ProcessStartInfo(Path.Combine(plan.TargetDirectory, "JetPane.exe")) { UseShellExecute = true, WorkingDirectory = plan.TargetDirectory };
            if (verification) start.ArgumentList.Add("--smoke-test");
            else { start.ArgumentList.Add("--updated-from"); start.ArgumentList.Add(Path.GetDirectoryName(file)!); }
            using var restarted = Process.Start(start) ?? throw new IOException("Updated successfully, but JetPane could not restart. Open JetPane.exe manually.");
            if (verification)
            {
                if (!restarted.WaitForExit(15000)) { restarted.Kill(); throw new IOException("Owned updated smoke instance did not exit."); }
                if (restarted.ExitCode != 0) throw new IOException("Updated smoke instance failed verification.");
            }
            return 0;
        }
        catch (Exception e)
        {
            if (plan != null) try { File.AppendAllText(Path.Combine(plan.TargetDirectory, "update.log"), DateTime.UtcNow.ToString("O") + " " + e + Environment.NewLine); } catch { }
            if (verification) return 1;
            MessageBox.Show(e.Message + "\n\nYour settings were preserved. Open JetPane.exe manually. See update.log and the backup folder if recovery is needed.", "JetPane update");
            return 1;
        }
    }
    internal static void DeleteStage(string root)
    {
        string full = Path.GetFullPath(root), parent = Path.GetDirectoryName(full)!;
        if (!parent.Equals(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
            || !Regex.IsMatch(Path.GetFileName(full), "^JetPaneUpdate-[a-f0-9]{32}$") || !File.Exists(Path.Combine(full, ".jetpane-update"))) return;
        try { if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) == 0) Directory.Delete(full, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    public static void CleanupLater(string root) => _ = Task.Run(async () => { for (int i = 0; i < 6; i++) { await Task.Delay(2000); DeleteStage(root); if (!Directory.Exists(root)) break; } });
}
