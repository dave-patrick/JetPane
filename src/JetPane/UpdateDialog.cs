using System.Diagnostics;

namespace JetPane;

sealed class UpdateDialog : Form
{
    readonly Label status = new() { Dock = DockStyle.Top, Height = 74, Padding = new Padding(12), Text = "Checking GitHub for updates…" };
    readonly TextBox notes = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    readonly ProgressBar progress = new() { Dock = DockStyle.Bottom, Height = 18 };
    readonly Button install = new() { Text = "Download update", AutoSize = true, Enabled = false };
    readonly Button releasePage = new() { Text = "View release", AutoSize = true, Enabled = false };
    readonly Button cancel = new() { Text = "Close", AutoSize = true };
    readonly CancellationTokenSource cancellation = new();
    readonly string target;
    readonly Action restart;
    UpdateRelease? release;
    PreparedUpdate? prepared;
    bool installing;

    public UpdateDialog(string targetDirectory, Action restartForUpdate)
    {
        target = targetDirectory; restart = restartForUpdate;
        Text = "JetPane updates"; Icon = AppIcon.Value; Width = 580; Height = 420; MinimumSize = new(480, 300); StartPosition = FormStartPosition.CenterScreen;
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(8) };
        buttons.Controls.AddRange(new Control[] { install, releasePage, cancel });
        Controls.Add(notes); Controls.Add(status); Controls.Add(progress); Controls.Add(buttons);
        cancel.Click += (_, _) => Close();
        releasePage.Click += (_, _) => { try { if (release != null) Process.Start(new ProcessStartInfo(release.Page.AbsoluteUri) { UseShellExecute = true }); } catch (Exception e) { status.Text = "Could not open the release page: " + e.Message; } };
        Shown += async (_, _) =>
        {
            try
            {
                release = await Updater.CheckAsync(cancellation.Token);
                if (IsDisposed) return;
                if (release == null) { status.Text = $"JetPane {Updater.CurrentVersion} is up to date."; notes.Text = "No newer complete release was found. Checks run only when you request them."; return; }
                status.Text = $"Installed: {Updater.CurrentVersion}\r\nAvailable: {release.Version}" + (release.Preview ? " (prerelease)" : "");
                notes.Text = release.Notes; releasePage.Enabled = true; install.Enabled = true;
            }
            catch (OperationCanceledException) { if (!IsDisposed) status.Text = cancellation.IsCancellationRequested ? "Update check canceled." : "The update check timed out. Close this window and try again."; }
            catch (Exception e) { if (!IsDisposed) status.Text = "Could not check for updates: " + e.Message; }
        };
        install.Click += async (_, _) =>
        {
            install.Enabled = false;
            try
            {
                if (prepared == null)
                {
                    status.Text = "Downloading and verifying the update… You can close this window to cancel.";
                    var updateProgress = new Progress<int>(value => { if (!IsDisposed) progress.Value = value; });
                    var downloaded = await Updater.DownloadAsync(release!, target, updateProgress, cancellation.Token);
                    if (IsDisposed) { Updater.CleanupLater(downloaded.Root); return; }
                    prepared = downloaded; progress.Value = 100;
                    status.Text = "Verified and ready. Restart to install. Your settings and profiles will be preserved; the existing app will be backed up.";
                    install.Text = "Restart and install"; install.Enabled = true;
                }
                else
                {
                    installing = true; cancel.Enabled = false; releasePage.Enabled = false;
                    status.Text = "Preparing the installer. JetPane will close and restart after installation.";
                    await Task.Run(() => Updater.StartInstaller(prepared));
                    restart();
                }
            }
            catch (OperationCanceledException) { if (!IsDisposed) status.Text = "Download canceled or timed out. Nothing was installed."; }
            catch (Exception e)
            {
                installing = false;
                if (!IsDisposed) { status.Text = "Update could not continue: " + e.Message; install.Enabled = true; cancel.Enabled = true; releasePage.Enabled = release != null; }
            }
        };
        FormClosing += (_, e) => { if (installing && !cancellation.IsCancellationRequested) e.Cancel = true; };
        FormClosed += (_, _) => { cancellation.Cancel(); if (prepared != null && !installing) Updater.CleanupLater(prepared.Root); };
    }
    public void AllowShutdown() { cancellation.Cancel(); }
}

sealed partial class Pilot
{
    UpdateDialog? updateDialog;
    void Updates()
    {
        if (updateDialog is { IsDisposed: false }) { updateDialog.Activate(); return; }
        updateDialog = new UpdateDialog(folder, () => { updateDialog!.AllowShutdown(); exiting = true; Application.Exit(); });
        updateDialog.Show(Form.ActiveForm ?? this);
    }
}
