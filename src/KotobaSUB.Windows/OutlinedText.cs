using System.Globalization;

namespace KotobaSUB.Windows;

// Draw only glyph geometry: an opaque dark outline keeps white text readable
// without introducing a subtitle panel on light video frames.
internal sealed class OutlinedText(string value, double size, string family) : FrameworkElement
{
    private FormattedText Format(double width)
    {
        var formatted = new FormattedText(value, CultureInfo.GetCultureInfo("ja-JP"), FlowDirection.LeftToRight,
            new Typeface(new FontFamily(family), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            size, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        if (double.IsFinite(width) && width > 4) formatted.MaxTextWidth = width - 4;
        formatted.TextAlignment = TextAlignment.Center;
        return formatted;
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        var formatted = Format(availableSize.Width);
        return new Size(formatted.WidthIncludingTrailingWhitespace + 4, formatted.Height + 4);
    }
    protected override void OnRender(DrawingContext drawingContext)
    {
        var formatted = Format(ActualWidth);
        var geometry = formatted.BuildGeometry(new Point(2, 2));
        var outline = new Pen(Brushes.Black, Math.Max(1.4, size * .045)) { LineJoin = PenLineJoin.Round };
        drawingContext.DrawGeometry(null, outline, geometry);
        drawingContext.DrawGeometry(Brushes.White, null, geometry);
    }
}
