using System.Globalization;

namespace KotobaSUB.Windows;

// Draw only glyph geometry: an opaque dark outline keeps white text readable
// without introducing a subtitle panel on light video frames.
internal sealed class OutlinedText(string value, double size, string family, int weight, double outlineWidth) : FrameworkElement
{
    private double Padding => Math.Max(2, outlineWidth + 1);
    private FormattedText Format(double width)
    {
        var formatted = new FormattedText(value, CultureInfo.GetCultureInfo("ja-JP"), FlowDirection.LeftToRight,
            new Typeface(new FontFamily(family), FontStyles.Normal, FontWeight.FromOpenTypeWeight(weight), FontStretches.Normal),
            size, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        if (double.IsFinite(width) && width > Padding * 2) formatted.MaxTextWidth = width - Padding * 2;
        formatted.TextAlignment = TextAlignment.Center;
        return formatted;
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        var formatted = Format(availableSize.Width);
        return new Size(formatted.WidthIncludingTrailingWhitespace + Padding * 2, formatted.Height + Padding * 2);
    }
    protected override void OnRender(DrawingContext drawingContext)
    {
        var formatted = Format(ActualWidth);
        var geometry = formatted.BuildGeometry(new Point(Padding, Padding));
        if (outlineWidth > 0)
        {
            var outline = new Pen(Brushes.Black, outlineWidth) { LineJoin = PenLineJoin.Round };
            drawingContext.DrawGeometry(null, outline, geometry);
        }
        drawingContext.DrawGeometry(Brushes.White, null, geometry);
    }
}
