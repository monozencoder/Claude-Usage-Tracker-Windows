using System.Windows;
using System.Windows.Input;
using ClaudeUsageTracker.App.Themes;
using ClaudeUsageTracker.App.ViewModels;

namespace ClaudeUsageTracker.App.Views;

/// <summary>
/// Settings window; changes apply immediately, so it only has a close button. Behavior lives in
/// <see cref="SettingsViewModel"/>, which its pages (Views/SettingsPages) share; this code-behind
/// only wires window lifetime.
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        ThemeManager.TrackTitleBar(this);

        Closing += (_, _) => viewModel.Close();
        // Changes apply as they're made, so Esc simply closes.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
        };
        // Also on re-activation: the user comes back here after signing in in the browser.
        Activated += async (_, _) => await viewModel.LoadAccountAsync();
    }
}
