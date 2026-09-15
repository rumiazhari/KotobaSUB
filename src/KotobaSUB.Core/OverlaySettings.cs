namespace KotobaSUB.Core;

public sealed record OverlaySettings
{
    public int Version { get; init; } = 1;
    public double Left { get; init; } = 200;
    public double Top { get; init; } = 650;
    public double Width { get; init; } = 1000;
    public double Height { get; init; } = 240;
    public double FontSize { get; init; } = 36;
    public double JapaneseOpacity { get; init; } = 1;
    public string FontFamily { get; init; } = "Yu Gothic UI";
    public bool Furigana { get; init; } = true;
    public bool Romaji { get; init; }
    public bool Gloss { get; init; } = true;
    public bool Translation { get; init; }
    public bool PreviousLine { get; init; }
    public bool NextLine { get; init; }
    public double GlobalOffsetSeconds { get; init; }
    public double TokenSpacing { get; init; } = 8;
    public string? MonitorDeviceName { get; init; }
    public double MonitorRelativeLeft { get; init; }
    public double MonitorRelativeTop { get; init; }

    public OverlaySettings Validate() => this with
    {
        GlobalOffsetSeconds = Clamp(GlobalOffsetSeconds, -30, 30, 0),
        Version = 1, Left = Finite(Left, 200), Top = Finite(Top, 650),
        Width = Clamp(Width, 320, 3840, 1000), Height = Clamp(Height, 140, 1200, 240),
        FontSize = Clamp(FontSize, 18, 72, 36), TokenSpacing = Clamp(TokenSpacing, 0, 32, 8),
        JapaneseOpacity = Clamp(JapaneseOpacity, 0.1, 1, 1),
        FontFamily = string.IsNullOrWhiteSpace(FontFamily) ? "Yu Gothic UI" : FontFamily,
        MonitorDeviceName = string.IsNullOrWhiteSpace(MonitorDeviceName) ? null : MonitorDeviceName.Trim(),
        MonitorRelativeLeft = Clamp(MonitorRelativeLeft, 0, 1, 0), MonitorRelativeTop = Clamp(MonitorRelativeTop, 0, 1, 0)
    };
    private static double Finite(double n, double fallback) => double.IsFinite(n) ? n : fallback;
    private static double Clamp(double n, double min, double max, double fallback) => Math.Clamp(Finite(n, fallback), min, max);
}