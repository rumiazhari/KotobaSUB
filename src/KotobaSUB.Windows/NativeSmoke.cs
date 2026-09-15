using System.Text.Json;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace KotobaSUB.Windows;

internal static class NativeSmoke
{
    public static int Run(string[] args)
    {
        string directory = Path.GetFullPath(args.SkipWhile(a => a != "--output").Skip(1).FirstOrDefault() ?? "artifacts/native-smoke");
        Directory.CreateDirectory(directory);
        var results = new List<string>();
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        int result = 1;
        var overlay = new OverlayWindow(new OverlaySettings { Left = 50, Top = 50, Width = 1000, Height = 260 });
        nint foreground = NativeOverlay.GetForegroundWindow();
        overlay.Show();
        app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            try
            {
                void Check(bool test, string name) { if (!test) throw new InvalidOperationException(name); results.Add("PASS " + name); }
                int mask = NativeOverlay.Transparent | NativeOverlay.ToolWindow | NativeOverlay.NoActivate | NativeOverlay.Layered;
                Check((overlay.Native.Styles & mask) == mask, "locked HWND extended styles");
                Check(NativeOverlay.GetForegroundWindow() == foreground, "show preserves foreground window");
                var hwnd = new WindowInteropHelper(overlay).Handle;
                Check(NativeOverlay.SendMessage(hwnd, 0x84, 0, 0) == -1, "locked hit test returns HTTRANSPARENT");
                Check(NativeOverlay.SendMessage(hwnd, 0x21, 0, 0) == 3, "mouse activation returns MA_NOACTIVATE");
                overlay.Render(PreviewSubtitle.Line); overlay.UpdateLayout();
                Capture(overlay, Path.Combine(directory, "default.png"));
                Check(HasTransparentCorner(overlay), "normal content has transparent corner pixels");
                overlay.SetLocked(false);
                Check((overlay.Native.Styles & NativeOverlay.Transparent) == 0, "unlock removes click-through");
                overlay.SetStudyInteractive(true); overlay.UpdateLayout();
                Check(overlay.StudyInteractive, "study mode enables token interaction");
                Capture(overlay, Path.Combine(directory, "study-mode.png"));
                overlay.SetStudyInteractive(false);
                overlay.SetLocked(true);
                Check((overlay.Native.Styles & mask) == mask, "relock restores styles");
                overlay.Hide(); overlay.Show();
                Check(NativeOverlay.GetForegroundWindow() == foreground, "hide/show preserves focus");
                overlay.Apply(overlay.Snapshot() with { FontSize = 54, Width = 600, Height = 480, Translation = true });
                overlay.UpdateLayout(); Capture(overlay, Path.Combine(directory, "large-wrapped.png"));
                overlay.Apply(overlay.Snapshot() with { Furigana = false, Gloss = false, Translation = false });
                overlay.UpdateLayout(); Capture(overlay, Path.Combine(directory, "japanese-only.png"));
                overlay.Apply(overlay.Snapshot() with { Width = 320, Height = 140, FontSize = 72, Furigana = true, Gloss = true, Translation = true });
                overlay.UpdateLayout(); Capture(overlay, Path.Combine(directory, "small-window.png"));
                Check(overlay.ActualWidth == 320 && overlay.ActualHeight == 140, "minimum geometry with large typography");
                overlay.Width = 800; overlay.Height = 300; overlay.UpdateLayout();
                Capture(overlay, Path.Combine(directory, "resized.png"));
                Check(overlay.Snapshot().Width == 800, "interactive geometry snapshot updates");
                OverlaySettings monitorSnapshot = overlay.Snapshot();
                Check(!string.IsNullOrWhiteSpace(monitorSnapshot.MonitorDeviceName) && monitorSnapshot.MonitorRelativeLeft is >= 0 and <= 1 && monitorSnapshot.MonitorRelativeTop is >= 0 and <= 1, "snapshot records active monitor placement");
                overlay.Apply(monitorSnapshot with { MonitorDeviceName = "missing-monitor" });
                Check(!string.IsNullOrWhiteSpace(overlay.Snapshot().MonitorDeviceName), "missing monitor recovers to an active display");
                overlay.Render(null); overlay.UpdateLayout();
                Check(HasTransparentCorner(overlay), "empty provider renders no background");
                overlay.Apply(new OverlaySettings { Left = 50, Top = 50, Width = 1000, Height = 320, PreviousLine = true, NextLine = true });
                var timeline = new KotobaSUB.Core.Lyrics.LyricTimeline("[00:00]今日は晴れです\n[00:03]私は明日学校に行きます\n[00:06]また会いましょう", null, TimeSpan.FromSeconds(10));
                overlay.RenderFrame(timeline.At(TimeSpan.FromSeconds(4))); overlay.UpdateLayout();
                Capture(overlay, Path.Combine(directory, "context-lines.png"));
                OverlaySettings? edited = null; bool startupPreference = false;
                var settings = new SettingsWindow(overlay.Snapshot(), value => { edited = value; overlay.Apply(value); }, () => startupPreference, value => { startupPreference = value; return true; });
                settings.Show(); settings.UpdateLayout();
                var panel = (StackPanel)((ScrollViewer)settings.Content).Content;
                var nextToggle = panel.Children.OfType<CheckBox>().Single(c => (string)c.Content == "Next line");
                nextToggle.IsChecked = false; nextToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Check(edited?.NextLine == false, "settings context toggle applies live");
                var fontCombo = panel.Children.OfType<ComboBox>().First(c => c.Items.Contains("Meiryo UI"));
                fontCombo.SelectedItem = "Meiryo UI";
                Check(edited?.FontFamily == "Meiryo UI", "settings font family applies live");
                var alignment = panel.Children.OfType<ComboBox>().First(c => c.Items.Contains("Left"));
                alignment.SelectedItem = "Left";
                Check(edited?.TextAlignmentMode == "Left", "settings text alignment applies live");
                panel.Children.OfType<Slider>().First().Value = 42;
                Check(edited?.FontSize == 42, "settings font slider applies live");
                Check(panel.Children.OfType<Slider>().Count() == 11, "settings exposes weight, spacing, outline and layer opacity sliders");
                panel.Children.OfType<Slider>().Skip(1).First().Value = 700;
                Check(edited?.JapaneseWeight == 700, "settings Japanese weight applies live");
                panel.Children.OfType<Slider>().Skip(2).First().Value = 16;
                Check(edited?.TokenSpacing == 16, "settings token spacing applies live");
                panel.Children.OfType<Slider>().Skip(3).First().Value = 12;
                Check(edited?.LineSpacing == 12, "settings line spacing applies live");
                panel.Children.OfType<Slider>().Skip(4).First().Value = 4.5;
                Check(edited?.OutlineWidth == 4.5, "settings text outline applies live");
                panel.Children.OfType<Slider>().Skip(5).First().Value = .6;
                Check(edited?.JapaneseOpacity == .6, "settings Japanese opacity applies live");
                panel.Children.OfType<Slider>().Skip(6).First().Value = .7;
                Check(edited?.FuriganaOpacity == .7, "settings furigana opacity applies live");
                var startupToggle = panel.Children.OfType<CheckBox>().Single(c => (string)c.Content == "Start with Windows");
                startupToggle.IsChecked = true; startupToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Check(startupPreference, "settings startup toggle applies live");
                panel.Children.OfType<Slider>().Last().Value = 1.5;
                Check(edited?.GlobalOffsetSeconds == 1.5, "settings sync slider applies live");
                settings.UpdateLayout(); Capture(settings, Path.Combine(directory, "settings.png")); settings.Close();
                result = 0;
            }
            catch (Exception ex) { results.Add("FAIL " + ex); }
            finally
            {
                File.WriteAllText(Path.Combine(directory, "results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
                overlay.Close(); app.Shutdown();
            }
        }));
        app.Run(); return result;
    }
    private static RenderTargetBitmap Bitmap(FrameworkElement element)
    {
        var bitmap = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element); return bitmap;
    }
    internal static void Capture(FrameworkElement element, string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(Bitmap(element)));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private static bool HasTransparentCorner(FrameworkElement element)
    {
        byte[] pixel = new byte[4]; Bitmap(element).CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, 4, 0); return pixel[3] == 0;
    }
}
