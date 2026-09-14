using Forms = System.Windows.Forms;

namespace KotobaSUB.Windows;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--smoke")) return NativeSmoke.Run(args);
        using var instance = new System.Threading.Mutex(true, "Local\\KotobaSUB", out bool first);
        if (!first) return 0;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KotobaSUB");
        var log = new LocalLog(Path.Combine(directory, "logs"));
        using var tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Information, Text = "KotobaSUB — no input provider", Visible = true };
        void Report(string message)
        {
            try { log.Write(message); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { System.Diagnostics.Trace.WriteLine(ex); }
            tray.ShowBalloonTip(5000, "KotobaSUB", message, Forms.ToolTipIcon.Warning);
        }
        var store = new SettingsStore(Path.Combine(directory, "settings.json"), Report);
        var overlay = new OverlayWindow(store.Load());
        void Save()
        {
            try { store.Save(overlay.Snapshot()); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Report($"Settings could not be saved: {ex.Message}"); }
        }
        var menu = new Forms.ContextMenuStrip(); tray.ContextMenuStrip = menu;
        var visible = new Forms.ToolStripMenuItem("Hide overlay");
        void ToggleVisible() { if (overlay.IsVisible) overlay.Hide(); else overlay.Show(); visible.Text = overlay.IsVisible ? "Hide overlay" : "Show overlay"; }
        visible.Click += (_, _) => ToggleVisible(); menu.Items.Add(visible);
        var position = new Forms.ToolStripMenuItem("Unlock position");
        void ToggleLock() { overlay.SetLocked(!overlay.Locked); position.Text = overlay.Locked ? "Unlock position" : "Lock position"; Save(); }
        position.Click += (_, _) => ToggleLock(); menu.Items.Add(position);
        bool preview = args.Contains("--preview");
        var sample = new Forms.ToolStripMenuItem("Sample preview") { Checked = preview };
        sample.Click += (_, _) => { preview = !preview; sample.Checked = preview; overlay.Render(preview ? PreviewSubtitle.Line : null); };
        menu.Items.Add(sample);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Source: not connected") { Enabled = false });
        SettingsWindow? settings = null;
        void OpenSettings()
        {
            if (settings is not null) { settings.Activate(); return; }
            settings = new SettingsWindow(overlay.Snapshot(), value => { overlay.Apply(value with { Left = overlay.Left, Top = overlay.Top, Width = overlay.Width, Height = overlay.Height }); Save(); });
            settings.Closed += (_, _) => settings = null; settings.Show();
        }
        menu.Items.Add("Settings", null, (_, _) => OpenSettings());
        tray.DoubleClick += (_, _) => OpenSettings();
        menu.Items.Add("Quit", null, (_, _) => app.Shutdown());
        overlay.GeometryChanged += Save;
        app.Exit += (_, _) => { Save(); settings?.Close(); overlay.Close(); tray.Visible = false; };
        overlay.Show();
        overlay.Native.Hotkey += id => { if (id == 1) ToggleVisible(); else if (id == 2) ToggleLock(); };
        if (!overlay.Native.Register(1, 0x78)) Report("Ctrl+Alt+F9 is unavailable. Use the tray to show or hide subtitles.");
        if (!overlay.Native.Register(2, 0x79)) Report("Ctrl+Alt+F10 is unavailable. Use the tray to unlock the overlay.");
        overlay.Render(preview ? PreviewSubtitle.Line : null);
        try { log.Write("Started native overlay; no provider active."); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Report($"Local log could not be written: {ex.Message}"); }
        return app.Run();
    }
}
