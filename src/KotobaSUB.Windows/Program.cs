using System.Net.Http;
using System.Threading;
using KotobaSUB.Core.Audio;
using KotobaSUB.Windows.Media;
using Forms = System.Windows.Forms;

namespace KotobaSUB.Windows;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--learning-smoke")) return LearningSmoke.Run();
        if (args.Contains("--audio-smoke")) return AudioSmoke.Run(args);
        if (args.Contains("--routing-smoke")) return RoutingSmoke.Run(args);
        if (args.Contains("--startup-smoke")) return StartupSmoke.Run();
        if (args.Contains("--smoke")) return NativeSmoke.Run(args);
        if (args.Contains("--media-smoke")) return MediaSmoke.Run(args);
        using var instance = new System.Threading.Mutex(true, (args.Contains("--app-smoke") || args.Contains("--freeze-smoke")) ? "Local\\KotobaSUB.Smoke" : "Local\\KotobaSUB", out bool first);
        if (!first) return 0;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        bool shellSmoke = args.Contains("--app-smoke") || args.Contains("--freeze-smoke");
        var directory = shellSmoke ? Path.GetFullPath(args.Contains("--freeze-smoke") ? "artifacts/freeze-smoke" : "artifacts/app-smoke") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KotobaSUB");
        var log = new LocalLog(Path.Combine(directory, "logs"));
        using var tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Information, Text = "KotobaSUB — waiting for media", Visible = true };
        void Log(string message)
        {
            try { log.Write(message); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { System.Diagnostics.Trace.WriteLine(ex); }
        }
        void Report(string message) { Log(message); tray.ShowBalloonTip(5000, "KotobaSUB", message, Forms.ToolTipIcon.Warning); }
        var store = new SettingsStore(Path.Combine(directory, "settings.json"), Report);
        var overlay = new OverlayWindow(store.Load());
        void Save()
        {
            try { store.Save(overlay.Snapshot()); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Report($"Settings could not be saved: {ex.Message}"); }
        }
        using var menu = new Forms.ContextMenuStrip(); tray.ContextMenuStrip = menu;
        var sourceStatus = new Forms.ToolStripMenuItem("Source: waiting for media") { Enabled = false };
        using var playback = new PlaybackController(overlay, directory, text =>
        {
            sourceStatus.Text = "Source: " + text;
            string tip = "KotobaSUB — " + text;
            if (tray.Text != tip) tray.Text = tip.Length > 63 ? tip[..63] : tip;
        }, Log);
        var visible = new Forms.ToolStripMenuItem("Hide overlay");
        void ToggleVisible() { if (overlay.IsVisible) overlay.Hide(); else overlay.Show(); visible.Text = overlay.IsVisible ? "Hide overlay" : "Show overlay"; }
        visible.Click += (_, _) => ToggleVisible(); menu.Items.Add(visible);
        var position = new Forms.ToolStripMenuItem("Unlock position");
        void ToggleLock() { overlay.SetLocked(!overlay.Locked); position.Text = overlay.Locked ? "Unlock position" : "Lock position"; Save(); }
        position.Click += (_, _) => ToggleLock(); menu.Items.Add(position);
        var pause = new Forms.ToolStripMenuItem("Pause subtitles") { CheckOnClick = true };
        pause.CheckedChanged += (_, _) => playback.SetPaused(pause.Checked); menu.Items.Add(pause);
        bool frozen = false; bool lockBeforeFreeze = true; TokenInfoWindow? tokenInfo = null;
        var freeze = new Forms.ToolStripMenuItem("Freeze / Study current subtitle") { CheckOnClick = true };
        void ToggleFreeze(bool value)
        {
            if (frozen == value) return;
            frozen = value;
            if (value) { lockBeforeFreeze = overlay.Locked; overlay.SetLocked(false); overlay.SetStudyInteractive(true); playback.SetFrozen(true); freeze.Text = "Resume live subtitles"; }
            else { playback.SetFrozen(false); overlay.SetStudyInteractive(false); overlay.SetLocked(lockBeforeFreeze); freeze.Text = "Freeze / Study current subtitle"; tokenInfo?.Close(); tokenInfo = null; }
        }
        freeze.CheckedChanged += (_, _) => ToggleFreeze(freeze.Checked); menu.Items.Add(freeze);
        overlay.TokenSelected += token =>
        {
            if (!frozen) return;
            tokenInfo?.Close(); tokenInfo = new TokenInfoWindow(token); tokenInfo.Closed += (_, _) => tokenInfo = null; tokenInfo.Show();
        };
        bool preview = args.Contains("--preview") || shellSmoke;
        var sample = new Forms.ToolStripMenuItem("Sample preview") { Checked = preview };
        sample.Click += (_, _) => { preview = !preview; sample.Checked = preview; playback.SetPreview(preview); };
        menu.Items.Add(sample);
        menu.Items.Add(new Forms.ToolStripSeparator()); menu.Items.Add(sourceStatus);
        menu.Items.Add("Sync earlier (0.5 s)", null, (_, _) => playback.AdjustSync(.5));
        menu.Items.Add("Sync later (0.5 s)", null, (_, _) => playback.AdjustSync(-.5));
        menu.Items.Add("Wrong lyrics", null, (_, _) => playback.Retry(true));
        menu.Items.Add("Retry source", null, (_, _) => playback.Retry(false));
        string modelPath = Path.Combine(directory, "models", "ggml-base.bin");
        using var modelHttp = new HttpClient(); modelHttp.DefaultRequestHeaders.UserAgent.ParseAdd("KotobaSUB/0.1 (+https://github.com/rumiazhari/KotobaSUB)");
        var modelInstaller = new WhisperModelInstaller(); CancellationTokenSource? modelDownload = null;
        var modelAction = new Forms.ToolStripMenuItem(File.Exists(modelPath) ? "Verify/update local ASR model" : "Install local ASR model (141 MB)");
        modelAction.Click += async (_, _) =>
        {
            if (modelDownload is not null) { modelDownload.Cancel(); return; }
            var cancellation = new CancellationTokenSource(); modelDownload = cancellation; modelAction.Text = "Cancel ASR model download";
            var progress = new Progress<ModelInstallProgress>(value =>
            {
                double received = value.BytesReceived / 1048576d; string total = value.TotalBytes is { } bytes ? $" / {bytes / 1048576d:F1}" : "";
                sourceStatus.Text = $"Source: Downloading ASR model {received:F1}{total} MB";
            });
            try
            {
                ModelInstallResult result = await modelInstaller.InstallAsync(modelHttp, modelPath, progress, cancellation.Token);
                string message = result.AlreadyPresent ? "Local ASR model verified" : "Local ASR model installed";
                Log(message); tray.ShowBalloonTip(3000, "KotobaSUB", message, Forms.ToolTipIcon.Info); playback.Retry(false);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { Log("ASR model download cancelled"); }
            catch (Exception ex) { Report($"ASR model installation failed: {ex.Message}"); }
            finally
            {
                cancellation.Dispose(); if (modelDownload == cancellation) modelDownload = null;
                modelAction.Text = File.Exists(modelPath) ? "Verify/update local ASR model" : "Install local ASR model (141 MB)";
            }
        };
        menu.Items.Add(modelAction);
        var startup = new StartupRegistration();
        bool changingStartup = false;
        var startWithWindows = new Forms.ToolStripMenuItem("Start with Windows") { CheckOnClick = true, Checked = startup.Enabled };
        startWithWindows.CheckedChanged += (_, _) =>
        {
            if (changingStartup) return;
            try
            {
                string executablePath = Environment.ProcessPath ?? throw new InvalidOperationException("Application path unavailable.");
                if (!Path.GetFileName(executablePath).Equals("KotobaSUB.exe", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Startup registration is available from the published KotobaSUB executable.");
                startup.Set(startWithWindows.Checked, executablePath);
            }
            catch (Exception ex)
            {
                changingStartup = true; startWithWindows.Checked = false; changingStartup = false;
                Report($"Startup registration failed: {ex.Message}");
            }
        };
        menu.Items.Add(startWithWindows);
        menu.Items.Add(new Forms.ToolStripSeparator());
        SettingsWindow? settings = null;
        void OpenSettings()
        {
            if (settings is not null) { settings.Activate(); return; }
            settings = new SettingsWindow(overlay.Snapshot(), value =>
            {
                overlay.Apply(value with { Left = overlay.Left, Top = overlay.Top, Width = overlay.Width, Height = overlay.Height });
                Save(); playback.SettingsChanged();
            });
            settings.Closed += (_, _) => settings = null; settings.Show();
        }
        menu.Items.Add("Settings", null, (_, _) => OpenSettings()); tray.DoubleClick += (_, _) => OpenSettings();
        menu.Items.Add("Quit", null, (_, _) => app.Shutdown());
        overlay.GeometryChanged += Save;
        app.Exit += (_, _) => { modelDownload?.Cancel(); tokenInfo?.Close(); playback.Dispose(); Save(); settings?.Close(); overlay.Close(); tray.Visible = false; };
        overlay.Show();
        overlay.Native.Hotkey += id => { if (id == 1) ToggleVisible(); else if (id == 2) ToggleLock(); else if (id == 3) freeze.Checked = !freeze.Checked; };
        if (!overlay.Native.Register(1, 0x78)) Report("Ctrl+Alt+F9 is unavailable. Use the tray to show or hide subtitles.");
        if (!overlay.Native.Register(2, 0x79)) Report("Ctrl+Alt+F10 is unavailable. Use the tray to unlock the overlay.");
        if (!overlay.Native.Register(3, 0x7B)) Report("Ctrl+Alt+F12 is unavailable. Use the tray to freeze or resume subtitles.");
        playback.SetPreview(preview);
        if (args.Contains("--freeze-smoke")) freeze.Checked = true;
        if (!shellSmoke) app.Dispatcher.BeginInvoke(new Action(async () => await playback.StartAsync()));
        Log("Started native overlay with automatic SMTC/lyrics/local-ASR routing.");
        // The shell smoke explicitly uses sample data and does not start SMTC.
        if (shellSmoke)
        {
            var smokeTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            smokeTimer.Tick += (_, _) =>
            {
                smokeTimer.Stop(); Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "result.txt"), $"TrayVisible={tray.Visible}\nOverlayLocked={overlay.Locked}\n{sourceStatus.Text}\nModelAction={modelAction.Text}\nStartWithWindows={startWithWindows.Checked}\nFrozen={frozen}\n");
                app.Shutdown();
            };
            smokeTimer.Start();
        }
        return app.Run();
    }
}
