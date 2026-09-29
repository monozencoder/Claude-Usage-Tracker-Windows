using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using ClaudeUsageTracker.App.Localization;
using ClaudeUsageTracker.App.Services;
using ClaudeUsageTracker.App.Settings;
using ClaudeUsageTracker.App.Themes;
using ClaudeUsageTracker.App.Tray;
using ClaudeUsageTracker.App.ViewModels;
using ClaudeUsageTracker.App.Views;
using ClaudeUsageTracker.Core.Api;
using ClaudeUsageTracker.Platform.ClaudeCode;
using ClaudeUsageTracker.Platform.Notifications;
using ClaudeUsageTracker.Platform.Startup;
using ClaudeUsageTracker.Platform.TrayIcon;

namespace ClaudeUsageTracker.App;

/// <summary>Composition root: builds the object graph on startup and tears it down on exit.</summary>
public partial class App : System.Windows.Application
{
    private const string AppName = "ClaudeUsageTracker";

    // Usage is read from Anthropic's actual API (api.anthropic.com) using Claude Code
    // CLI's own OAuth credentials — not claude.ai's bot-protected web app — so a plain
    // HttpClient is sufficient; no embedded browser needed.
    private readonly HttpClient _httpClient = new();
    private readonly FlyoutViewModel _flyoutViewModel = new();

    // Assigned in OnStartup, which WPF always runs before anything else here.
    private AppSettingsStore _settingsStore = null!;
    private UsageFetcher _usageFetcher = null!;
    private ILaunchAtLoginService _launchAtLoginService = null!;
    private TrayIconController _trayIconController = null!;
    private UsageRefreshCoordinator _coordinator = null!;
    private FlyoutWindow _flyoutWindow = null!;
    private DispatcherTimer _refreshTimer = null!;
    private CredentialsFileWatcher _credentialsWatcher = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Safety net: this is a background tray app with no visible main window,
        // so an unhandled exception would otherwise silently kill it with no
        // indication to the user beyond the tray icon disappearing. Surface it
        // and keep running wherever the failure isn't fatal to the process.
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        _settingsStore = new AppSettingsStore();
        var settings = _settingsStore.Current;
        ThemeManager.Apply(settings.Theme);
        Loc.Apply(settings.Language);

        _usageFetcher = new UsageFetcher(new ClaudeCodeUsageClient(_httpClient), _settingsStore);
        _launchAtLoginService = new RunKeyLaunchAtLoginService(
            appName: AppName,
            executablePathProvider: () => Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0]);

        _flyoutWindow = new FlyoutWindow { DataContext = _flyoutViewModel, Topmost = settings.AlwaysOnTop };
        _flyoutViewModel.RefreshRequested += RefreshNow;
        _flyoutViewModel.SettingsRequested += OpenSettingsWindow;
        _flyoutViewModel.SignInRequested += StartSignIn;
        Loc.LanguageChanged += _flyoutViewModel.RefreshLanguage;
        // The view model was built (as a field) before the saved language was applied above.
        _flyoutViewModel.RefreshLanguage();

        _trayIconController = new TrayIconController(new TrayIconRenderer());
        _trayIconController.Clicked += _flyoutWindow.ToggleNearCursor;
        _trayIconController.RefreshRequested += RefreshNow;
        _trayIconController.ExitRequested += () => Shutdown();

        _coordinator = new UsageRefreshCoordinator(
            _usageFetcher, _settingsStore, _flyoutViewModel, new ToastNotificationService(), _trayIconController.UpdateIcon);

        _refreshTimer = new DispatcherTimer { Interval = settings.RefreshInterval };
        _refreshTimer.Tick += (_, _) => RefreshNow();
        _refreshTimer.Start();

        // Picks up a finished sign-in (or Claude Code renewing its token) immediately.
        _credentialsWatcher = new CredentialsFileWatcher();
        _credentialsWatcher.Changed += RefreshNow;

        if (settings.ShowFlyoutOnStartup)
            _flyoutWindow.ToggleNearCursor();

        RefreshNow();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _refreshTimer?.Stop();
        ThemeManager.Shutdown();
        _trayIconController?.Dispose();
        _credentialsWatcher?.Dispose();
        _httpClient.Dispose();
        base.OnExit(e);
    }

    // async void on purpose: an unexpected exception then reaches
    // DispatcherUnhandledException (and the banner) instead of vanishing with a Task.
    private async void RefreshNow() => await _coordinator.RefreshAsync();

    private void StartSignIn()
    {
        if (!ClaudeCli.StartLogin())
            _flyoutViewModel.SetBanner(Loc.Get("Error_CouldNotStartClaude"));
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _flyoutViewModel.SetBanner(Loc.Format("Error_Unexpected", e.Exception.Message));
        e.Handled = true;
    }

    private void OpenSettingsWindow()
    {
        var viewModel = new SettingsViewModel(_settingsStore, _launchAtLoginService, _usageFetcher);
        viewModel.Saved += () =>
        {
            _refreshTimer.Interval = _settingsStore.Current.RefreshInterval;
            _flyoutWindow.Topmost = _settingsStore.Current.AlwaysOnTop;
            RefreshNow();
        };
        viewModel.ConnectionTested += result => _coordinator.ApplyResult(result);
        new SettingsWindow(viewModel).ShowDialog();
    }
}
