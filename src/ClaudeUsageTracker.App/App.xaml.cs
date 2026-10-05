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
    private TaskbarBarController _taskbarBarController = null!;
    private ClickThroughController _clickThroughController = null!;
    private UsageRefreshCoordinator _coordinator = null!;
    private DispatcherTimer _minuteTimer = null!;
    private FlyoutWindow _flyoutWindow = null!;
    private DispatcherTimer _refreshTimer = null!;
    private CredentialsFileWatcher _credentialsWatcher = null!;
    private SettingsWindow? _settingsWindow;

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

        _flyoutWindow = new FlyoutWindow
        {
            DataContext = _flyoutViewModel,
            Topmost = settings.AlwaysOnTop,
            RestingOpacity = settings.FlyoutOpacity,
            Scale = settings.FlyoutScale,
            Compact = settings.FlyoutCompact,
            SavedPosition = settings.FlyoutPosition
        };
        _flyoutWindow.ScaleSaved += scale =>
        {
            _settingsStore.Current.FlyoutScale = scale;
            _settingsStore.Save();
            // Keeps the slider of an open settings window in step with the drag.
            if (_settingsWindow?.DataContext is SettingsViewModel settingsViewModel)
                settingsViewModel.FlyoutScalePercent = SettingsViewModel.ToScalePercent(scale);
        };
        _flyoutWindow.CompactChanged += SetFlyoutCompact;
        _flyoutWindow.PositionSaved += position =>
        {
            _settingsStore.Current.FlyoutPosition = position;
            _settingsStore.Save();
        };
        _flyoutViewModel.ShowResetClockTime = settings.ResetTimeDisplay == ResetTimeDisplay.Clock;
        _flyoutViewModel.RefreshRequested += RefreshNow;
        _flyoutViewModel.SettingsRequested += OpenSettingsWindow;
        _flyoutViewModel.SignInRequested += StartSignIn;
        Loc.LanguageChanged += _flyoutViewModel.RefreshLanguage;
        // The view model was built (as a field) before the saved language was applied above.
        _flyoutViewModel.RefreshLanguage();

        _trayIconController = new TrayIconController(new TrayIconRenderer()) { Style = settings.TrayIconStyle };
        _trayIconController.Clicked += _flyoutWindow.Toggle;
        _trayIconController.RefreshRequested += RefreshNow;
        _trayIconController.SettingsRequested += OpenSettingsWindow;
        _trayIconController.CanResetPosition = () => _flyoutWindow.SavedPosition is not null;
        _trayIconController.ResetPositionRequested += () =>
        {
            _flyoutWindow.ResetPosition();
            _settingsStore.Current.FlyoutPosition = null;
            _settingsStore.Save();
        };
        _trayIconController.ExitRequested += () => Shutdown();

        _taskbarBarController = new TaskbarBarController(_flyoutViewModel, _settingsStore);
        _taskbarBarController.Clicked += _flyoutWindow.Toggle;
        _taskbarBarController.OffsetChanged += (device, offset) =>
        {
            // Keeps the slider of an open settings window in step with the drag.
            if (_settingsWindow?.DataContext is SettingsViewModel settingsViewModel
                && settingsViewModel.TaskbarBarDisplays.FirstOrDefault(display => display.DeviceName == device) is { } row)
                row.Offset = offset;
        };
        ApplyTaskbarBarSettings();

        _clickThroughController = new ClickThroughController();
        _clickThroughController.Add(() => _settingsStore.Current.FlyoutClickThrough, () => [_flyoutWindow]);
        _clickThroughController.Add(() => _settingsStore.Current.TaskbarBarClickThrough, () => _taskbarBarController.Strips);
        _clickThroughController.Refresh();
        _trayIconController.IsFlyoutCompact = () => _settingsStore.Current.FlyoutCompact;
        _trayIconController.FlyoutCompactToggled += () => SetFlyoutCompact(!_settingsStore.Current.FlyoutCompact);
        _trayIconController.IsFlyoutClickThrough =() => _settingsStore.Current.FlyoutClickThrough;
        _trayIconController.IsTaskbarBarClickThrough = () => _settingsStore.Current.TaskbarBarClickThrough;
        _trayIconController.IsTaskbarBarShown = () => _settingsStore.Current.ShowTaskbarBar;
        _trayIconController.FlyoutClickThroughToggled += () => SetClickThrough(flyout: !_settingsStore.Current.FlyoutClickThrough);
        _trayIconController.TaskbarBarClickThroughToggled += () => SetClickThrough(taskbarBar: !_settingsStore.Current.TaskbarBarClickThrough);

        _coordinator = new UsageRefreshCoordinator(
            _usageFetcher, _settingsStore, _flyoutViewModel, new ToastNotificationService(), _trayIconController.UpdateIcon);

        // Refreshes can be minutes apart; the "resets in" times shown in between must still run down.
        _minuteTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _minuteTimer.Tick += (_, _) => _flyoutViewModel.RefreshTimes();
        _minuteTimer.Start();

        _refreshTimer = new DispatcherTimer { Interval = settings.RefreshInterval };
        _refreshTimer.Tick += (_, _) => RefreshNow();
        _refreshTimer.Start();

        // Picks up a finished sign-in (or Claude Code renewing its token) immediately.
        _credentialsWatcher = new CredentialsFileWatcher();
        _credentialsWatcher.Changed += RefreshNow;

        if (settings.ShowFlyoutOnStartup)
            _flyoutWindow.Toggle();

        RefreshNow();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _refreshTimer?.Stop();
        _minuteTimer?.Stop();
        ThemeManager.Shutdown();
        _trayIconController?.Dispose();
        _taskbarBarController?.Dispose();
        _clickThroughController?.Dispose();
        _credentialsWatcher?.Dispose();
        _httpClient.Dispose();
        base.OnExit(e);
    }

    // async void on purpose: an unexpected exception then reaches
    // DispatcherUnhandledException (and the banner) instead of vanishing with a Task.
    private async void RefreshNow() => await _coordinator.RefreshAsync();

    private void ApplyTaskbarBarSettings()
    {
        _taskbarBarController.Enabled = _settingsStore.Current.ShowTaskbarBar;
        _taskbarBarController.Refresh();
    }

    // From the tray menu or a double-click on the flyout; saved the same way as the click-through toggles below.
    private void SetFlyoutCompact(bool compact)
    {
        if (_settingsWindow?.DataContext is SettingsViewModel settingsViewModel)
        {
            settingsViewModel.FlyoutCompact = compact;
            return;
        }

        _settingsStore.Current.FlyoutCompact = compact;
        _settingsStore.Save();
        _flyoutWindow.Compact = compact;
    }

    // From the tray menu. With the settings window open the change goes through its view model,
    // which saves and applies it like a flip of its own toggle; otherwise it's done here.
    private void SetClickThrough(bool? flyout = null, bool? taskbarBar = null)
    {
        if (_settingsWindow?.DataContext is SettingsViewModel settingsViewModel)
        {
            settingsViewModel.FlyoutClickThrough = flyout ?? settingsViewModel.FlyoutClickThrough;
            settingsViewModel.TaskbarBarClickThrough = taskbarBar ?? settingsViewModel.TaskbarBarClickThrough;
            return;
        }

        var settings = _settingsStore.Current;
        settings.FlyoutClickThrough = flyout ?? settings.FlyoutClickThrough;
        settings.TaskbarBarClickThrough = taskbarBar ?? settings.TaskbarBarClickThrough;
        _settingsStore.Save();
        _clickThroughController.Refresh();
    }

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
        // Reachable from both the flyout and the tray menu; never stack a second copy.
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        var viewModel = new SettingsViewModel(_settingsStore, _launchAtLoginService, _usageFetcher);
        viewModel.Applied += () =>
        {
            // Setting Interval restarts the timer, so only touch it when it actually changed.
            if (_refreshTimer.Interval != _settingsStore.Current.RefreshInterval)
                _refreshTimer.Interval = _settingsStore.Current.RefreshInterval;
            _flyoutWindow.Topmost = _settingsStore.Current.AlwaysOnTop;
            ApplyTaskbarBarSettings();
            _clickThroughController.Refresh();
            _trayIconController.Style = _settingsStore.Current.TrayIconStyle;
            _flyoutViewModel.ShowResetClockTime = _settingsStore.Current.ResetTimeDisplay == ResetTimeDisplay.Clock;
            _flyoutWindow.Compact = _settingsStore.Current.FlyoutCompact;
            if (_flyoutWindow.RestingOpacity != _settingsStore.Current.FlyoutOpacity)
                _flyoutWindow.RestingOpacity = _settingsStore.Current.FlyoutOpacity;
            if (_flyoutWindow.Scale != _settingsStore.Current.FlyoutScale)
                _flyoutWindow.Scale = _settingsStore.Current.FlyoutScale;
        };
        viewModel.ConnectionTested += result => _coordinator.ApplyResult(result);
        _settingsWindow = new SettingsWindow(viewModel);
        // Modeless, like Obsidian's settings: the flyout and tray menu stay usable while it's open.
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;

            // Settings apply as they're changed; fetching with a new mode/interval waits until the
            // window closes so stepping the interval or flipping the mode doesn't send a prompt each click.
            // Skipped when the app is exiting (tray "Exit" closes this window during shutdown).
            if (viewModel.FetchSettingsChanged && !Dispatcher.HasShutdownStarted)
                RefreshNow();
        };
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }
}
