namespace KotobaSUB.Windows;

internal sealed class SettingsWindow : Window
{
    public SettingsWindow(OverlaySettings initial, Action<OverlaySettings> changed, Func<bool>? startupEnabled = null, Func<bool, bool>? setStartup = null)
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
        panel.Children.Add(new TextBlock { Text = "Font family", Margin = new Thickness(0, 12, 0, 4) });
        var fonts = new ComboBox { ItemsSource = new[] { "Yu Gothic UI", "Meiryo UI", "MS Gothic", "Segoe UI", "Arial" }, SelectedItem = current.FontFamily, IsEditable = true };
        if (fonts.SelectedItem is null) fonts.Text = current.FontFamily;
        fonts.SelectionChanged += (_, _) => { if (fonts.SelectedItem is string family) { current = current with { FontFamily = family }; changed(current); } };
        fonts.LostFocus += (_, _) => { if (!string.IsNullOrWhiteSpace(fonts.Text)) { current = current with { FontFamily = fonts.Text }; changed(current); } };
        panel.Children.Add(fonts);
        var sizeText = new TextBlock { Text = $"Japanese text size: {current.FontSize:0}", Margin = new Thickness(0, 16, 0, 4) };
        panel.Children.Add(sizeText);
        var size = new Slider { Minimum = 18, Maximum = 72, Value = current.FontSize, TickFrequency = 2, IsSnapToTickEnabled = true };
        size.ValueChanged += (_, _) => { current = current with { FontSize = size.Value }; sizeText.Text = $"Japanese text size: {size.Value:0}"; changed(current); };
        panel.Children.Add(size);
        var spacingText = new TextBlock { Text = $"Token spacing: {current.TokenSpacing:0}", Margin = new Thickness(0, 12, 0, 4) };
        panel.Children.Add(spacingText);
        var spacing = new Slider { Minimum = 0, Maximum = 32, Value = current.TokenSpacing, TickFrequency = 2, IsSnapToTickEnabled = true };
        spacing.ValueChanged += (_, _) => { current = current with { TokenSpacing = spacing.Value }; spacingText.Text = $"Token spacing: {spacing.Value:0}"; changed(current); };
        panel.Children.Add(spacing);
        var opacityText = new TextBlock { Text = $"Japanese opacity: {current.JapaneseOpacity:P0}", Margin = new Thickness(0, 12, 0, 4) };
        panel.Children.Add(opacityText);
        var opacity = new Slider { Minimum = .1, Maximum = 1, Value = current.JapaneseOpacity, TickFrequency = .1, IsSnapToTickEnabled = true };
        opacity.ValueChanged += (_, _) => { current = current with { JapaneseOpacity = opacity.Value }; opacityText.Text = $"Japanese opacity: {opacity.Value:P0}"; changed(current); };
        panel.Children.Add(opacity);
        void LayerOpacity(string label, double value, Func<OverlaySettings, double, OverlaySettings> update)
        {
            var labelText = new TextBlock { Text = $"{label}: {value:P0}", Margin = new Thickness(0, 10, 0, 4) };
            panel.Children.Add(labelText);
            var slider = new Slider { Minimum = .1, Maximum = 1, Value = value, TickFrequency = .1, IsSnapToTickEnabled = true };
            slider.ValueChanged += (_, _) => { current = update(current, slider.Value); labelText.Text = $"{label}: {slider.Value:P0}"; changed(current); };
            panel.Children.Add(slider);
        }
        LayerOpacity("Furigana opacity", current.FuriganaOpacity, (s, v) => s with { FuriganaOpacity = v });
        LayerOpacity("Gloss opacity", current.GlossOpacity, (s, v) => s with { GlossOpacity = v });
        LayerOpacity("Translation opacity", current.TranslationOpacity, (s, v) => s with { TranslationOpacity = v });
        LayerOpacity("Inactive line opacity", current.InactiveLineOpacity, (s, v) => s with { InactiveLineOpacity = v });
        if (startupEnabled is not null && setStartup is not null)
        {
            var startup = new CheckBox { Content = "Start with Windows", IsChecked = startupEnabled(), Margin = new Thickness(0, 14, 0, 4) };
            startup.Click += (_, _) => { bool desired = startup.IsChecked == true; if (!setStartup(desired)) startup.IsChecked = !desired; };
            panel.Children.Add(startup);
        }
        var offsetText = new TextBlock { Text = $"Global sync: {current.GlobalOffsetSeconds:+0.0;-0.0;0.0} s (positive = earlier)", Margin = new Thickness(0, 12, 0, 4) };
        panel.Children.Add(offsetText);
        var offset = new Slider { Minimum = -30, Maximum = 30, Value = current.GlobalOffsetSeconds, TickFrequency = .5, IsSnapToTickEnabled = true };
        offset.ValueChanged += (_, _) => { current = current with { GlobalOffsetSeconds = offset.Value }; offsetText.Text = $"Global sync: {offset.Value:+0.0;-0.0;0.0} s (positive = earlier)"; changed(current); };
        panel.Children.Add(offset);
        panel.Children.Add(new TextBlock { Text = "Ctrl+Alt+F9: show/hide\nCtrl+Alt+F10: unlock/lock position\nSample preview is available from the tray.\nReadings: IPADIC. Meanings: JMdict (EDRDG). Install or update the local ASR model from the tray.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 20, 0, 0), Foreground = Brushes.DimGray });
        panel.Children.Add(new TextBlock { Text = "JMdict © EDRDG / Jim Breen — CC BY-SA 4.0", FontSize = 11, Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap });
    }
}
