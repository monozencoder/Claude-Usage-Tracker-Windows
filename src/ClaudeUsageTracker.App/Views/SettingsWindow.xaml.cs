using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using ClaudeUsageTracker.App.Services;
using ClaudeUsageTracker.App.ViewModels;
using ClaudeUsageTracker.Core.Api;
using ClaudeUsageTracker.Platform.ClaudeCli;
using ClaudeUsageTracker.Platform.Startup;

namespace ClaudeUsageTracker.App.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettingsStore _settingsStore;
    private readonly ILaunchAtLoginService _launchAtLoginService;
    private readonly ClaudeCodeUsageClient _usageClient;
    private readonly SettingsViewModel _viewModel = new();
    private readonly AppTheme _savedTheme;
    private bool _saved;

    public event Func<Task>? SettingsSaved;

    public SettingsWindow(
        AppSettingsStore settingsStore,
        ILaunchAtLoginService launchAtLoginService,
        ClaudeCodeUsageClient usageClient)
    {
        _settingsStore = settingsStore;
        _launchAtLoginService = launchAtLoginService;
        _usageClient = usageClient;

        InitializeComponent();
        DataContext = _viewModel;
        ThemeManager.TrackTitleBar(this);

        var settings = _settingsStore.Load();
        _viewModel.RefreshIntervalSeconds = settings.RefreshIntervalSeconds;
        _viewModel.NotificationsEnabled = settings.NotificationsEnabled;
        _viewModel.LaunchAtLoginEnabled = _launchAtLoginService.IsEnabled;
        _viewModel.ShowFlyoutOnStartup = settings.ShowFlyoutOnStartup;
        _viewModel.Theme = _savedTheme = settings.Theme;

        // Preview the theme as soon as it's picked; Closed reverts it unless saved.
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) =>
        {
            if (!_saved)
                ThemeManager.Apply(_savedTheme);
        };

        RefreshCredentialsSummary();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.Theme))
            ThemeManager.Apply(_viewModel.Theme);
    }

    private void RefreshCredentialsSummary()
    {
        var resolution = ClaudeCredentialResolver.Resolve();
        _viewModel.CredentialsSummary = resolution switch
        {
            { Credentials: not null } => "Credentials found and look valid.",
            { CredentialsFileFound: true } => "Credentials found, but the token is expired. Run \"claude\" in a terminal to refresh it.",
            _ => "No credentials found on Windows or in any installed WSL distro. Install Claude Code and run \"claude\" to sign in."
        };
    }

    private async void OnTestConnectionClicked(object sender, RoutedEventArgs e)
    {
        _viewModel.IsBusy = true;
        _viewModel.StatusMessage = "Testing connection...";
        _viewModel.StatusIsError = false;
        try
        {
            var resolution = ClaudeCredentialResolver.Resolve();
            var credentials = resolution.Credentials;
            if (credentials is null)
            {
                _viewModel.StatusMessage = resolution.CredentialsFileFound
                    ? "Token is still expired after attempting a refresh. Run \"claude\" in a terminal to sign in."
                    : "No Claude Code CLI credentials found on Windows or in any installed WSL distro.";
                _viewModel.StatusIsError = true;
                return;
            }

            try
            {
                var usage = await _usageClient.GetUsageAsync(credentials);
                _viewModel.StatusMessage = $"Connected. Session usage: {usage.SessionPercentage:0.#}%, weekly: {usage.WeeklyPercentage:0.#}%.";
                _viewModel.StatusIsError = false;
            }
            catch (AuthRequiredException)
            {
                _viewModel.StatusMessage = "Anthropic rejected the token. Run \"claude\" in a terminal to sign in again.";
                _viewModel.StatusIsError = true;
            }
            catch (ClaudeApiException ex)
            {
                _viewModel.StatusMessage = $"Connection test failed (status {ex.StatusCode}).";
                _viewModel.StatusIsError = true;
            }

            RefreshCredentialsSummary();
        }
        finally
        {
            _viewModel.IsBusy = false;
        }
    }

    private async void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        _viewModel.IsBusy = true;
        try
        {
            _viewModel.NormalizeRefreshInterval();
            var settings = _settingsStore.Load();
            settings.RefreshIntervalSeconds = _viewModel.RefreshIntervalSeconds;
            settings.NotificationsEnabled = _viewModel.NotificationsEnabled;
            settings.ShowFlyoutOnStartup = _viewModel.ShowFlyoutOnStartup;
            settings.Theme = _viewModel.Theme;
            _settingsStore.Save(settings);
            _saved = true;

            _launchAtLoginService.SetEnabled(_viewModel.LaunchAtLoginEnabled);

            if (SettingsSaved is not null)
                await SettingsSaved.Invoke();

            Close();
        }
        finally
        {
            _viewModel.IsBusy = false;
        }
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e) => Close();

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
