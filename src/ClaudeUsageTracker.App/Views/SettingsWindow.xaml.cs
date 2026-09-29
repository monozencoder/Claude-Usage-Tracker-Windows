using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using ClaudeUsageTracker.App.Themes;
using ClaudeUsageTracker.App.ViewModels;

namespace ClaudeUsageTracker.App.Views;

/// <summary>
/// Settings dialog. Behavior lives in <see cref="SettingsViewModel"/>; this code-behind
/// only wires window lifetime and keeps the refresh-interval box digits-only.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        ThemeManager.TrackTitleBar(this);

        viewModel.Saved += Close;
        Closed += (_, _) =>
        {
            viewModel.Saved -= Close;
            viewModel.DiscardUnsavedPreview();
        };
        // Also on re-activation: the user comes back here after signing in in the browser.
        Activated += async (_, _) => await viewModel.LoadAccountAsync();
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e) => Close();

    private void OnInstallLinkNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void OnRefreshIntervalPreviewTextInput(object sender, TextCompositionEventArgs e)
        => e.Handled = !e.Text.All(char.IsAsciiDigit);

    private void OnRefreshIntervalPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            // Space never reaches PreviewTextInput, so it has to be blocked here.
            case Key.Space:
                e.Handled = true;
                break;
            case Key.Up:
                StepRefreshInterval(up: true);
                e.Handled = true;
                break;
            case Key.Down:
                StepRefreshInterval(up: false);
                e.Handled = true;
                break;
        }
    }

    private void OnRefreshIntervalPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Only while focused, so scrolling past the box doesn't silently change it.
        if (!RefreshIntervalBox.IsKeyboardFocusWithin)
            return;
        StepRefreshInterval(up: e.Delta > 0);
        e.Handled = true;
    }

    private void OnRefreshIntervalLostFocus(object sender, KeyboardFocusChangedEventArgs e)
        => _viewModel.NormalizeRefreshInterval();

    /// <summary>Keeps only the digits of pasted text (so "90s" or " 120 " still work).</summary>
    private void OnRefreshIntervalPasting(object sender, DataObjectPastingEventArgs e)
    {
        e.CancelCommand();
        if (e.SourceDataObject.GetData(DataFormats.UnicodeText) is not string text)
            return;

        var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length > 0)
            RefreshIntervalBox.SelectedText = digits;
        RefreshIntervalBox.CaretIndex = RefreshIntervalBox.SelectionStart + RefreshIntervalBox.SelectionLength;
        RefreshIntervalBox.SelectionLength = 0;
    }

    private void StepRefreshInterval(bool up)
    {
        var command = up ? _viewModel.IncreaseRefreshIntervalCommand : _viewModel.DecreaseRefreshIntervalCommand;
        command.Execute(null);
        RefreshIntervalBox.CaretIndex = RefreshIntervalBox.Text.Length;
    }
}
