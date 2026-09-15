namespace KotobaSUB.Windows;

internal sealed class SettingsWindow : Window
{
    public SettingsWindow(OverlaySettings initial, Action<OverlaySettings> changed)
    {
        Title = "KotobaSUB settings"; Width = 390; SizeToContent = SizeToContent.Height; MaxHeight = SystemParameters.WorkArea.Height - 40; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var current = initial;
        var panel = new StackPanel { Margin = new Thickness(24) }; Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        panel.Children.Add(new TextBlock { Text = "Subtitle appearance", FontSize = 20, Margin = new Thickness(0, 0, 0, 16) });
        void Toggle(string label, bool value, Func<OverlaySettings, bool, OverlaySettings> update)
        {
            var box = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 6, 0, 6) };
            box.Click += (_, _) => { current = update(current, box.IsChecked == true); changed(current); };
            panel.Children.Add(box);
        }
        Toggle("Furigana", current.Furigana, (s, v) => s with { Furigana = v });
        Toggle("Romaji (optional)", current.Romaji, (s, v) => s with { Romaji = v });
        Toggle("Word gloss", current.Gloss, (s, v) => s with { Gloss = v });
        Toggle("Supplied line translation", current.Translation, (s, v) => s with { Translation = v });
        Toggle("Previous line", current.PreviousLine, (s, v) => s with { PreviousLine = v });
        Toggle("Next line", current.NextLine, (s, v) => s with { NextLine = v });
        var sizeText = new TextBlock { Text = $"Japanese text size: {current.FontSize:0}", Margin = new Thickness(0, 16, 0, 4) };
        panel.Children.Add(sizeText);
        var size = new Slider { Minimum = 18, Maximum = 72, Value = current.FontSize, TickFrequency = 2, IsSnapToTickEnabled = true };
        size.ValueChanged += (_, _) => { current = current with { FontSize = size.Value }; sizeText.Text = $"Japanese text size: {size.Value:0}"; changed(current); };
        panel.Children.Add(size);
        var offsetText = new TextBlock { Text = $"Global sync: {current.GlobalOffsetSeconds:+0.0;-0.0;0.0} s (positive = earlier)", Margin = new Thickness(0, 12, 0, 4) };
        panel.Children.Add(offsetText);
        var offset = new Slider { Minimum = -30, Maximum = 30, Value = current.GlobalOffsetSeconds, TickFrequency = .5, IsSnapToTickEnabled = true };
        offset.ValueChanged += (_, _) => { current = current with { GlobalOffsetSeconds = offset.Value }; offsetText.Text = $"Global sync: {offset.Value:+0.0;-0.0;0.0} s (positive = earlier)"; changed(current); };
        panel.Children.Add(offset);
        panel.Children.Add(new TextBlock { Text = "Ctrl+Alt+F9: show/hide\nCtrl+Alt+F10: unlock/lock position\nSample preview is available from the tray.\nReadings: IPADIC. Meanings: JMdict (EDRDG). Install or update the local ASR model from the tray.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 20, 0, 0), Foreground = Brushes.DimGray });
        panel.Children.Add(new TextBlock { Text = "JMdict © EDRDG / Jim Breen — CC BY-SA 4.0", FontSize = 11, Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap });
    }
}
