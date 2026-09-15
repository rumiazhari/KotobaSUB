using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Effects;

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
    internal bool Locked { get; private set; } = true;
    public event Action? GeometryChanged;

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
        Width = Math.Min(Preferences.Width, SystemParameters.VirtualScreenWidth);
        Height = Math.Min(Preferences.Height, SystemParameters.VirtualScreenHeight);
        Left = Math.Clamp(Preferences.Left, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width);
        Top = Math.Clamp(Preferences.Top, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height);
        RenderFrame(current);
    }
    internal OverlaySettings Snapshot() => Preferences with { Left = Left, Top = Top, Width = Width, Height = Height };
    internal void SetLocked(bool value)
    {
        Native.SetLocked(value); Locked = value;
        edit.Visibility = resize.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
        text.IsHitTestVisible = false;
    }
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
            if (Preferences.Furigana)
                stack.Children.Add(Label(token.Reading ?? "", Preferences.FontSize * .45, .95, Preferences.FontSize * .7));
            stack.Children.Add(Label(token.Surface, Preferences.FontSize, 1));
            if (Preferences.Gloss) stack.Children.Add(Label(token.Glosses.FirstOrDefault() ?? "", Preferences.FontSize * .43, .85, Preferences.FontSize * .65));
            words.Children.Add(stack);
        }
        if (line.Tokens.Count == 0) words.Children.Add(Label(line.OriginalText, Preferences.FontSize, 1));
        text.Children.Add(words);
        if (Preferences.Translation && !string.IsNullOrWhiteSpace(line.Translation)) text.Children.Add(Label(line.Translation, Preferences.FontSize * .55, .9));
        if (Preferences.NextLine && frame.Next is { } next && !string.IsNullOrWhiteSpace(next.OriginalText)) text.Children.Add(Label(next.OriginalText, Preferences.FontSize * .6, .45));
    }
    private OutlinedText Label(string value, double size, double opacity, double minHeight = 0) => new(value, size, Preferences.FontFamily)
    {
        Opacity = opacity, MinHeight = minHeight
    };
}