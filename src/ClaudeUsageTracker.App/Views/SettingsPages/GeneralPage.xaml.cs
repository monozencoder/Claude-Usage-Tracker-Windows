using System.Windows.Controls;
using System.Windows.Input;

namespace ClaudeUsageTracker.App.Views.SettingsPages;

public partial class GeneralPage : UserControl
{
    public GeneralPage() => InitializeComponent();

    // Keeps the box that adds a notification threshold digits-only.
    private void OnDigitsOnlyPreviewTextInput(object sender, TextCompositionEventArgs e)
        => e.Handled = !e.Text.All(char.IsAsciiDigit);
}
