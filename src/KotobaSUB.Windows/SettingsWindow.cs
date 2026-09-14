namespace KotobaSUB.Windows;

internal sealed class SettingsWindow : Window
{
    public SettingsWindow(OverlaySettings initial, Action<OverlaySettings> changed)
    {
        Title = "KotobaSUB settings"; Width = 390; Height = 380; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var current = initial;
        var panel = new StackPanel { Margin = new Thickness(24) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = "Subtitle appearance", FontSize = 20, Margin = new Thickness(0, 0, 0, 16) });
        void Toggle(string label, bool value, Func<OverlaySettings, bool, OverlaySettings> update)
        {
            var box = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 6, 0, 6) };
            box.Click += (_, _) => { current = update(current, box.IsChecked == true); changed(current); };
            panel.Children.Add(box);
        }
        Toggle("Furigana", current.Furigana, (s, v) => s with { Furigana = v });
        Toggle("Word gloss", current.Gloss, (s, v) => s with { Gloss = v });
        Toggle("Supplied line translation", current.Translation, (s, v) => s with { Translation = v });
        var sizeText = new TextBlock { Text = $"Japanese text size: {current.FontSize:0}", Margin = new Thickness(0, 16, 0, 4) };
        panel.Children.Add(sizeText);
        var size = new Slider { Minimum = 18, Maximum = 72, Value = current.FontSize, TickFrequency = 2, IsSnapToTickEnabled = true };
        size.ValueChanged += (_, _) => { current = current with { FontSize = size.Value }; sizeText.Text = $"Japanese text size: {size.Value:0}"; changed(current); };
        panel.Children.Add(size);
        panel.Children.Add(new TextBlock { Text = "Ctrl+Alt+F9: show/hide\nCtrl+Alt+F10: unlock/lock position\nSample preview is available from the tray.\nAudio and lyrics providers are not installed yet.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 20, 0, 0), Foreground = Brushes.DimGray });
    }
}
