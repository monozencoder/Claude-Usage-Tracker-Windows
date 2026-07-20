using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using ClaudeUsageTracker.App.Services;
using ClaudeUsageTracker.App.ViewModels;
using ClaudeUsageTracker.App.Views;
using ClaudeUsageTracker.Core.Api;
using ClaudeUsageTracker.Platform.Notifications;
using ClaudeUsageTracker.Platform.Startup;
using ClaudeUsageTracker.Platform.TrayIcon;

namespace ClaudeUsageTracker.App;

public partial class App : System.Windows.Application
{
    private static readonly TimeSpan MinRefreshInterval = TimeSpan.FromSeconds(15);

    private HttpClient? _httpClient;
    private ClaudeCodeUsageClient? _usageClient;
    private TrayIconController? _trayIconController;
    private UsageRefreshCoordinator? _coordinator;
    private DispatcherTimer? _refreshTimer;
    private FlyoutViewModel? _flyoutViewModel;
    private FlyoutWindow? _flyoutWindow;
    private AppSettingsStore? _settingsStore;
    private ILaunchAtLoginService? _launchAtLoginService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Safety net: this is a background tray app with no visible main window,
        // so an unhandled exception would otherwise silently kill it with no
        // indication to the user beyond the tray icon disappearing. Surface it
        // and keep running wherever the failure isn't fatal to the process.
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // Usage is read from Anthropic's actual API (api.anthropic.com) using
        // Claude Code CLI's own OAuth credentials — not claude.ai's bot-protected
        // web app — so a plain HttpClient is sufficient; no embedded browser needed.
        _httpClient = new HttpClient();
        _usageClient = new ClaudeCodeUsageClient(_httpClient);

        _settingsStore = new AppSettingsStore();
        _launchAtLoginService = new RunKeyLaunchAtLoginService(
            appName: "ClaudeUsageTracker",
            executablePathProvider: () => Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0]);

        var trayRenderer = new TrayIconRenderer();

        _flyoutViewModel = new FlyoutViewModel();
        _flyoutWindow = new FlyoutWindow { DataContext = _flyoutViewModel };

        _trayIconController = new TrayIconController(trayRenderer);
        _trayIconController.Clicked += () => _flyoutWindow.ToggleNearCursor();
        _trayIconController.RefreshRequested += async () => await RefreshAsync();
        _trayIconController.ExitRequested += () => Shutdown();

        _coordinator = new UsageRefreshCoordinator(
            _usageClient, _settingsStore, _flyoutViewModel, new ToastNotificationService(),
            (percentage, status) => _trayIconController.UpdateIcon(percentage, status));

        _flyoutViewModel.RefreshRequested += async () => await RefreshAsync();
        _flyoutViewModel.SettingsRequested += OpenSettingsWindow;

        var settings = _settingsStore.Load();

        _refreshTimer = new DispatcherTimer
        {
            Interval = ClampInterval(settings.RefreshIntervalSeconds)
        };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        _refreshTimer.Start();

        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_coordinator is not null)
            await _coordinator.RefreshAsync();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _flyoutViewModel?.SetBanner($"Unexpected error: {e.Exception.Message}", isError: true);
        e.Handled = true;
    }

    private static TimeSpan ClampInterval(int seconds)
        => TimeSpan.FromSeconds(Math.Max(seconds, (int)MinRefreshInterval.TotalSeconds));

    private void OpenSettingsWindow()
    {
        if (_settingsStore is null || _launchAtLoginService is null || _usageClient is null)
            return;

        var window = new SettingsWindow(_settingsStore, _launchAtLoginService, _usageClient);
        window.SettingsSaved += async () =>
        {
            _coordinator?.ReloadSettings();
            if (_refreshTimer is not null && _settingsStore is not null)
                _refreshTimer.Interval = ClampInterval(_settingsStore.Load().RefreshIntervalSeconds);
            await RefreshAsync();
        };
        window.Owner = null;
        window.ShowDialog();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _refreshTimer?.Stop();
        _trayIconController?.Dispose();
        _httpClient?.Dispose();
        base.OnExit(e);
    }
}
