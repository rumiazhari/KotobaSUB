using KotobaSUB.Core;

namespace KotobaSUB.Windows;

internal sealed class TokenInfoWindow : Window
{
    public TokenInfoWindow(LearningToken token)
    {
        Title = token.Surface; Width = 340; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        string reading = string.IsNullOrWhiteSpace(token.Reading) ? "—" : token.Reading;
        string glosses = token.Glosses.Count == 0 ? "No local gloss available" : string.Join("; ", token.Glosses);
        Content = new StackPanel { Margin = new Thickness(20), Children =
        {
            new TextBlock { Text = token.Surface, FontSize = 30, FontWeight = FontWeights.SemiBold },
            new TextBlock { Text = reading, FontSize = 16, Margin = new Thickness(0, 4, 0, 12) },
            new TextBlock { Text = $"Lemma: {token.Lemma}\nPart of speech: {token.PartOfSpeech}", TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = glosses, Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap, FontSize = 16 }
        }};
    }
}