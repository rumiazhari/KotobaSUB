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
                overlay.Render(null); overlay.UpdateLayout();
                Check(HasTransparentCorner(overlay), "empty provider renders no background");
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
    private static void Capture(FrameworkElement element, string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(Bitmap(element)));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private static bool HasTransparentCorner(FrameworkElement element)
    {
        byte[] pixel = new byte[4]; Bitmap(element).CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, 4, 0); return pixel[3] == 0;
    }
}
