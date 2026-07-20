using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using ClaudeUsageTracker.App.Services;
using ClaudeUsageTracker.App.ViewModels;
using ClaudeUsageTracker.Core.Api;
using ClaudeUsageTracker.Core.ClaudeCode;
using ClaudeUsageTracker.Platform.ClaudeCli;
using ClaudeUsageTracker.Platform.Startup;

namespace ClaudeUsageTracker.App.Views;

public partial class SettingsWindow : Window
{
    // Undocumented but stable since Windows 10 20H1; makes the native title bar match
    // the app's dark theme instead of showing a jarring white bar above dark content.
    private const int DwmwaUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    private readonly AppSettingsStore _settingsStore;
    private readonly ILaunchAtLoginService _launchAtLoginService;
    private readonly ClaudeCodeUsageClient _usageClient;
    private readonly SettingsViewModel _viewModel = new();

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
        SourceInitialized += (_, _) => EnableDarkTitleBar();

        var settings = _settingsStore.Load();
        _viewModel.RefreshIntervalSeconds = settings.RefreshIntervalSeconds;
        _viewModel.NotificationsEnabled = settings.NotificationsEnabled;
        _viewModel.LaunchAtLoginEnabled = _launchAtLoginService.IsEnabled;

        RefreshCredentialsSummary();
    }

    private void EnableDarkTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var useDarkMode = 1;
        DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref useDarkMode, sizeof(int));
    }

    private void RefreshCredentialsSummary()
    {
        var credentials = ClaudeCodeCredentialReader.TryRead();
        _viewModel.CredentialsSummary = credentials switch
        {
            null => $"No credentials found at {ClaudeCodeCredentialReader.CredentialsFilePath}. Install Claude Code and run \"claude\" to sign in.",
            { } c when c.IsExpired(DateTimeOffset.Now) => "Credentials found, but the token is expired. Run \"claude\" in a terminal to refresh it.",
            _ => "Credentials found and look valid."
        };
    }

    private async void OnTestConnectionClicked(object sender, RoutedEventArgs e)
    {
        _viewModel.IsBusy = true;
        _viewModel.StatusMessage = "Testing connection...";
        _viewModel.StatusIsError = false;
        try
        {
            var credentials = ClaudeCodeCredentialReader.TryRead();
            if (credentials is null)
            {
                _viewModel.StatusMessage = "No Claude Code CLI credentials found.";
                _viewModel.StatusIsError = true;
                return;
            }

            if (credentials.IsExpired(DateTimeOffset.Now))
            {
                ClaudeCliRefresher.TryRefresh();
                credentials = ClaudeCodeCredentialReader.TryRead();
                if (credentials is null || credentials.IsExpired(DateTimeOffset.Now))
                {
                    _viewModel.StatusMessage = "Token is still expired after attempting a refresh. Run \"claude\" in a terminal to sign in.";
                    _viewModel.StatusIsError = true;
                    return;
                }
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
            var settings = _settingsStore.Load();
            settings.RefreshIntervalSeconds = _viewModel.RefreshIntervalSeconds;
            settings.NotificationsEnabled = _viewModel.NotificationsEnabled;
            _settingsStore.Save(settings);

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
}
