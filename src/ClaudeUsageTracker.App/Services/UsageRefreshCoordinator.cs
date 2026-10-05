using ClaudeUsageTracker.App.Localization;
using ClaudeUsageTracker.App.Settings;
using ClaudeUsageTracker.App.ViewModels;
using ClaudeUsageTracker.Core.Notifications;
using ClaudeUsageTracker.Core.Status;
using ClaudeUsageTracker.Core.Usage;
using ClaudeUsageTracker.Platform.Notifications;
using ClaudeUsageTracker.Platform.TrayIcon;

namespace ClaudeUsageTracker.App.Services;

/// <summary>
/// Orchestrates a single refresh cycle: fetch usage (<see cref="UsageFetcher"/>),
/// update the flyout view model, raise the threshold/reset notifications it calls for
/// (<see cref="UsageNotificationEvaluator"/>), and report the usage and its status back to
/// the caller so it can repaint the tray icon.
/// </summary>
public sealed class UsageRefreshCoordinator
{
    private readonly UsageFetcher _fetcher;
    private readonly AppSettingsStore _settingsStore;
    private readonly AppStateStore _stateStore;
    private readonly FlyoutViewModel _flyoutViewModel;
    private readonly IToastNotificationService _toastService;
    private readonly Action<TrayIconContent> _onIconUpdate;
    private readonly NotificationDedupTracker _dedupTracker;
    private readonly UsageNotificationEvaluator _notifications;

    // Whether any usage has been shown since the app started, and whether RefreshAgainAfter is waiting.
    private bool _hasUsage;
    private bool _retryPending;

    public UsageRefreshCoordinator(
        UsageFetcher fetcher,
        AppSettingsStore settingsStore,
        AppStateStore stateStore,
        FlyoutViewModel flyoutViewModel,
        IToastNotificationService toastService,
        Action<TrayIconContent> onIconUpdate)
    {
        _fetcher = fetcher;
        _settingsStore = settingsStore;
        _stateStore = stateStore;
        _flyoutViewModel = flyoutViewModel;
        _toastService = toastService;
        _onIconUpdate = onIconUpdate;
        _dedupTracker = new NotificationDedupTracker(stateStore.Current.NotifiedThresholdKeys, stateStore.Current.NotifiedWindowEnds);
        _notifications = new UsageNotificationEvaluator(_dedupTracker);
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        // The timer and the refresh buttons can fire while a (slow, CLI-backed) refresh is still running.
        if (_flyoutViewModel.IsRefreshing)
            return;

        _flyoutViewModel.IsRefreshing = true;
        try
        {
            var result = await _fetcher.FetchAsync(_settingsStore.Current.AvoidTokenUsage, ct);
            ApplyResult(result);

            // Restarted soon after the last call to the usage endpoint: there's nothing to show
            // yet, and the regular timer is minutes away, so come back as soon as it may be asked.
            if (result.RetryAfter is { } retryAfter && !_hasUsage && !_retryPending)
                RefreshAgainAfter(retryAfter);
        }
        finally
        {
            _flyoutViewModel.IsRefreshing = false;
        }
    }

    // async void on purpose, like App.RefreshNow: an unexpected exception reaches the banner.
    private async void RefreshAgainAfter(TimeSpan wait)
    {
        _retryPending = true;
        try
        {
            await Task.Delay(wait + TimeSpan.FromSeconds(1));
        }
        finally
        {
            _retryPending = false;
        }

        await RefreshAsync();
    }

    /// <summary>
    /// Shows a fetch result in the flyout and tray, whether it came from a refresh here or
    /// from elsewhere, e.g. the settings window's Test connection.
    /// </summary>
    public void ApplyResult(UsageFetchResult result)
    {
        if (result.RetryAfter is { } retryAfter)
        {
            // Token-free mode, asked again too soon: not an error, so no banner — keep the numbers
            // and say briefly when a refresh will work.
            _flyoutViewModel.ShowFooterNote(Loc.Format("Flyout_RefreshAvailableIn", (int)Math.Ceiling(retryAfter.TotalMinutes)));
            return;
        }

        if (result is { UsageEndpointRateLimited: true, Usage: null })
        {
            // Token-free mode only. Keep showing the last good numbers; the next regular
            // retry is 10 minutes out (UsageFetcher skips one regular refresh after a 429).
            _flyoutViewModel.SetBanner(Loc.Format("Error_RateLimited", UsagePolling.RateLimitedRetrySeconds / 60));
            return;
        }

        if (result.Usage is not { } usage)
        {
            _flyoutViewModel.SetBanner(UsageFetchErrorText.Describe(result)!, result.CanSignIn);
            if (result.ErrorStatus is { } errorStatus)
                _onIconUpdate(new TrayIconContent(0, errorStatus, 0, errorStatus));
            return;
        }

        var now = DateTimeOffset.Now;
        _hasUsage = true;
        _flyoutViewModel.ApplyUsage(usage, now);
        _flyoutViewModel.ClearBanner();

        var effectiveSession = usage.EffectiveSessionPercentage(now);
        // Colored by the usage itself, like the bars in the flyout and on the taskbar, so the same
        // number is the same color everywhere. Not by pace (usage projected to the end of the
        // window): work comes in bursts, so the projection cries wolf early in a window.
        var status = UsageStatusCalculator.CalculateStatus(effectiveSession, showRemaining: false, elapsedFraction: null);
        var weeklyStatus = UsageStatusCalculator.CalculateStatus(usage.WeeklyPercentage, showRemaining: false, elapsedFraction: null);

        Notify(new(effectiveSession, usage.SessionResetTime), new(usage.WeeklyPercentage, usage.WeeklyResetTime), now);
        _onIconUpdate(new TrayIconContent(effectiveSession, status, usage.WeeklyPercentage, weeklyStatus));
    }

    /// <summary>
    /// Raises the toasts a new reading calls for (thresholds: the user's, 75/90/95% by default;
    /// and window resets). Dedup state is only persisted when it actually changes, which it can
    /// without a toast (e.g. a window that ended while the app wasn't running).
    /// </summary>
    private void Notify(UsageWindowReading session, UsageWindowReading weekly, DateTimeOffset now)
    {
        var settings = _settingsStore.Current;
        var notifications = _notifications.Evaluate(session, weekly, now,
            settings.EffectiveNotificationThresholds, settings.NotificationsEnabled, settings.WeeklyNotificationsEnabled);

        foreach (var notification in notifications)
        {
            var (title, body) = Describe(notification);
            _toastService.Show(title, body);
        }

        if (!_dedupTracker.TakeChanged())
            return;
        _stateStore.Current.NotifiedThresholdKeys = [.. _dedupTracker.SentKeys];
        _stateStore.Current.NotifiedWindowEnds = new(_dedupTracker.WindowEnds);
        _stateStore.Save();
    }

    private static (string Title, string Body) Describe(UsageNotification notification) => notification switch
    {
        { Window: UsageWindow.Session, Threshold: null } => (Loc.Get("Toast_SessionResetTitle"), Loc.Get("Toast_SessionResetBody")),
        { Window: UsageWindow.Weekly, Threshold: null } => (Loc.Get("Toast_WeeklyResetTitle"), Loc.Get("Toast_WeeklyResetBody")),
        { Window: UsageWindow.Session } => (Loc.Get("Toast_UsageAlertTitle"), Loc.Format("Toast_UsageAlertBody", notification.Threshold)),
        _ => (Loc.Get("Toast_UsageAlertTitle"), Loc.Format("Toast_WeeklyAlertBody", notification.Threshold))
    };
}
