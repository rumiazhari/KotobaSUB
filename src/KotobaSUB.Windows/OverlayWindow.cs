using System.Windows.Controls.Primitives;
using DrawingRectangle = System.Drawing.Rectangle;
using System.Windows.Input;
using KotobaSUB.Core.Japanese;
using Forms = System.Windows.Forms;

namespace KotobaSUB.Windows;

internal sealed class OverlayWindow : Window, ISubtitleRenderer
{
    private readonly Grid root = new();
    private readonly StackPanel text = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Border edit = new() { BorderBrush = Brushes.SlateGray, BorderThickness = new Thickness(1), Background = new SolidColorBrush(Color.FromArgb(25, 80, 100, 120)) };
    private readonly Thumb resize = new() { Width = 18, Height = 18, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Cursor = Cursors.SizeNWSE };
    private readonly Viewbox fittedText = new() { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Margin = new Thickness(12) };
    private SubtitleFrame current = new(null);
    internal NativeOverlay Native { get; private set; } = null!;
    internal OverlaySettings Preferences { get; private set; }
    internal SubtitleFrame DisplayedFrame => current;
    internal bool Locked { get; private set; } = true;
    internal bool StudyInteractive { get; private set; }
    public event Action? GeometryChanged;
    public event Action<LearningToken>? TokenSelected;

    public OverlayWindow(OverlaySettings settings)
    {
        Title = "KotobaSUB"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        MinWidth = 320; MinHeight = 140;
        Preferences = settings.Validate(); Content = root;
        fittedText.Child = text; root.Children.Add(edit); root.Children.Add(fittedText); root.Children.Add(resize);
        edit.Visibility = resize.Visibility = Visibility.Collapsed;
        edit.MouseLeftButtonDown += (_, e) => { if (!Locked && e.ButtonState == MouseButtonState.Pressed) { DragMove(); GeometryChanged?.Invoke(); } };
        resize.DragDelta += (_, e) => { Width = Math.Max(MinWidth, Width + e.HorizontalChange); Height = Math.Max(MinHeight, Height + e.VerticalChange); };
        resize.DragCompleted += (_, _) => GeometryChanged?.Invoke();
        SizeChanged += (_, _) => RenderFrame(current);
        SourceInitialized += (_, _) => Native = new NativeOverlay(this);
        Closed += (_, _) => Native?.Dispose();
        Apply(settings);
    }
    internal void Apply(OverlaySettings settings)
    {
        Preferences = settings.Validate();
        Forms.Screen screen = TargetScreen(Preferences.MonitorDeviceName);
        DrawingRectangle workArea = screen.WorkingArea;
        Width = Math.Min(Preferences.Width, workArea.Width);
        Height = Math.Min(Preferences.Height, workArea.Height);
        bool matchingMonitor = string.Equals(Preferences.MonitorDeviceName, screen.DeviceName, StringComparison.OrdinalIgnoreCase);
        double requestedLeft = matchingMonitor ? workArea.Left + Preferences.MonitorRelativeLeft * Math.Max(0, workArea.Width - Width) : Preferences.Left;
        double requestedTop = matchingMonitor ? workArea.Top + Preferences.MonitorRelativeTop * Math.Max(0, workArea.Height - Height) : Preferences.Top;
        Left = Math.Clamp(requestedLeft, workArea.Left, workArea.Right - Width);
        Top = Math.Clamp(requestedTop, workArea.Top, workArea.Bottom - Height);
        RenderFrame(current);
    }
    internal OverlaySettings Snapshot()
    {
        Forms.Screen screen = Forms.Screen.FromRectangle(new DrawingRectangle((int)Math.Round(Left), (int)Math.Round(Top), (int)Math.Ceiling(Width), (int)Math.Ceiling(Height)));
        DrawingRectangle workArea = screen.WorkingArea;
        return Preferences with
        {
            Left = Left, Top = Top, Width = Width, Height = Height, MonitorDeviceName = screen.DeviceName,
            MonitorRelativeLeft = Ratio(Left - workArea.Left, workArea.Width - Width),
            MonitorRelativeTop = Ratio(Top - workArea.Top, workArea.Height - Height)
        };
    }
    private static double Ratio(double value, double span) => span <= 0 ? 0 : Math.Clamp(value / span, 0, 1);
    private static Forms.Screen TargetScreen(string? deviceName) => Forms.Screen.AllScreens.FirstOrDefault(s => string.Equals(s.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
        ?? Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];
    internal void SetLocked(bool value)
    {
        Native.SetLocked(value); Locked = value;
        edit.Visibility = resize.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
        text.IsHitTestVisible = !value && StudyInteractive;
    }
    internal void SetStudyInteractive(bool value) { StudyInteractive = value; text.IsHitTestVisible = !Locked && value; }
    public void Render(SubtitleLine? line) => RenderFrame(new(line));
    public void RenderFrame(SubtitleFrame frame)
    {
        current = frame; var line = frame.Current; text.Children.Clear(); text.MaxWidth = (Math.Max(280, Width - 32) * Math.Max(1, Preferences.FontSize / 36));
        if (line is null) return;
        if (Preferences.PreviousLine && frame.Previous is { } previous && !string.IsNullOrWhiteSpace(previous.OriginalText)) text.Children.Add(Label(previous.OriginalText, Preferences.FontSize * .6, .45));
        var words = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = (Math.Max(280, Width - 32) * Math.Max(1, Preferences.FontSize / 36)) };
        foreach (var token in line.Tokens)
        {
            var stack = new StackPanel { Margin = new Thickness(Preferences.TokenSpacing / 2, 4, Preferences.TokenSpacing / 2, 4) };
            stack.MouseLeftButtonUp += (_, _) => { if (StudyInteractive) TokenSelected?.Invoke(token); };
            if (Preferences.Furigana)
                stack.Children.Add(Label(JapaneseText.HasKanji(token.Surface) ? token.Reading ?? "" : "", Preferences.FontSize * .45, .95, Preferences.FontSize * .7));
            if (Preferences.Romaji) stack.Children.Add(Label(Romaji(token), Preferences.FontSize * .42, .9, Preferences.FontSize * .65));
            stack.Children.Add(Label(token.Surface, Preferences.FontSize, Preferences.JapaneseOpacity));
            if (Preferences.Gloss) stack.Children.Add(Label(token.Glosses.FirstOrDefault() ?? "", Preferences.FontSize * .43, .85, Preferences.FontSize * .65));
            words.Children.Add(stack);
        }
        if (line.Tokens.Count == 0) words.Children.Add(Label(line.OriginalText, Preferences.FontSize, 1));
        text.Children.Add(words);
        if (Preferences.Translation && !string.IsNullOrWhiteSpace(line.Translation)) text.Children.Add(Label(line.Translation, Preferences.FontSize * .55, .9));
        if (Preferences.NextLine && frame.Next is { } next && !string.IsNullOrWhiteSpace(next.OriginalText)) text.Children.Add(Label(next.OriginalText, Preferences.FontSize * .6, .45));
    }
    private static string Romaji(LearningToken token) => token.PartOfSpeech == "助詞" && token.Surface is "は" or "へ" or "を"
        ? token.Surface switch { "は" => "wa", "へ" => "e", _ => "o" }
        : token.Reading is { } reading ? KanaRomanizer.Convert(reading) : "";
    private OutlinedText Label(string value, double size, double opacity, double minHeight = 0) => new(value, size, Preferences.FontFamily)
    {
        Opacity = opacity, MinHeight = minHeight
    };
}