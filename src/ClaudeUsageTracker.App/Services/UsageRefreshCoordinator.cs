using ClaudeUsageTracker.App.Localization;
using ClaudeUsageTracker.App.Settings;
using ClaudeUsageTracker.App.ViewModels;
using ClaudeUsageTracker.Core.Models;
using ClaudeUsageTracker.Core.Notifications;
using ClaudeUsageTracker.Core.Status;
using ClaudeUsageTracker.Platform.Notifications;
using ClaudeUsageTracker.Platform.TrayIcon;

namespace ClaudeUsageTracker.App.Services;

/// <summary>
/// Orchestrates a single refresh cycle: fetch usage (<see cref="UsageFetcher"/>),
/// update the flyout view model, evaluate threshold/reset notifications, and report
/// the usage and its status back to the caller so it can repaint the tray icon.
/// </summary>
public sealed class UsageRefreshCoordinator
{
    private const string SessionKeyPrefix = "session_";
    private const string WeeklyKeyPrefix = "weekly_";

    private readonly UsageFetcher _fetcher;
    private readonly AppSettingsStore _settingsStore;
    private readonly FlyoutViewModel _flyoutViewModel;
    private readonly IToastNotificationService _toastService;
    private readonly Action<TrayIconContent> _onIconUpdate;
    private readonly NotificationDedupTracker _dedupTracker;

    private double _lastSessionPercentage = -1;
    private double _lastWeeklyPercentage = -1;

    public UsageRefreshCoordinator(
        UsageFetcher fetcher,
        AppSettingsStore settingsStore,
        FlyoutViewModel flyoutViewModel,
        IToastNotificationService toastService,
        Action<TrayIconContent> onIconUpdate)
    {
        _fetcher = fetcher;
        _settingsStore = settingsStore;
        _flyoutViewModel = flyoutViewModel;
        _toastService = toastService;
        _onIconUpdate = onIconUpdate;
        _dedupTracker = new NotificationDedupTracker(settingsStore.Current.NotifiedThresholdKeys);
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        // The timer and the refresh buttons can fire while a (slow, CLI-backed) refresh is still running.
        if (_flyoutViewModel.IsRefreshing)
            return;

        _flyoutViewModel.IsRefreshing = true;
        try
        {
            ApplyResult(await _fetcher.FetchAsync(ct: ct));
        }
        finally
        {
            _flyoutViewModel.IsRefreshing = false;
        }
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
            _flyoutViewModel.SetBanner(Loc.Format("Error_RateLimited", AppSettings.TokenFreeRateLimitedRetrySeconds / 60));
            return;
        }

        if (result.Usage is not { } usage)
        {
            _flyoutViewModel.SetBanner(result.Error!, result.CanSignIn);
            if (result.ErrorStatus is { } errorStatus)
                _onIconUpdate(new TrayIconContent(0, errorStatus, 0, errorStatus));
            return;
        }

        var now = DateTimeOffset.Now;
        _flyoutViewModel.ApplyUsage(usage, now);
        _flyoutViewModel.ClearBanner();

        var effectiveSession = usage.EffectiveSessionPercentage(now);
        // Colored by the usage itself, like the bars in the flyout and on the taskbar, so the same
        // number is the same color everywhere. Not by pace (usage projected to the end of the
        // window): work comes in bursts, so the projection cries wolf early in a window.
        var status = UsageStatusCalculator.CalculateStatus(effectiveSession, showRemaining: false, elapsedFraction: null);
        var weeklyStatus = UsageStatusCalculator.CalculateStatus(usage.WeeklyPercentage, showRemaining: false, elapsedFraction: null);

        EvaluateNotifications(effectiveSession, usage.WeeklyPercentage);
        _onIconUpdate(new TrayIconContent(effectiveSession, status, usage.WeeklyPercentage, weeklyStatus));
    }

    /// <summary>
    /// Fires threshold (the user's, 75/90/95% by default) and window-reset toasts for the session
    /// window and, if the user wants them, the weekly one — deduped so the same threshold
    /// doesn't re-notify every refresh cycle. Dedup state is only persisted when it actually changes.
    /// </summary>
    private void EvaluateNotifications(double sessionPercentage, double weeklyPercentage)
    {
        var previousSession = _lastSessionPercentage;
        var previousWeekly = _lastWeeklyPercentage;
        _lastSessionPercentage = sessionPercentage;
        _lastWeeklyPercentage = weeklyPercentage;

        var settings = _settingsStore.Current;
        if (!settings.NotificationsEnabled)
            return;

        var stateChanged = EvaluateWindow(SessionKeyPrefix, previousSession, sessionPercentage,
            "Toast_SessionResetTitle", "Toast_SessionResetBody", "Toast_UsageAlertBody");
        if (settings.WeeklyNotificationsEnabled)
        {
            stateChanged |= EvaluateWindow(WeeklyKeyPrefix, previousWeekly, weeklyPercentage,
                "Toast_WeeklyResetTitle", "Toast_WeeklyResetBody", "Toast_WeeklyAlertBody");
        }

        if (stateChanged)
        {
            settings.NotifiedThresholdKeys = [.. _dedupTracker.SentKeys];
            _settingsStore.Save();
        }
    }

    /// <summary>One window's toasts; returns whether the dedup state changed.</summary>
    private bool EvaluateWindow(string keyPrefix, double previous, double percentage, string resetTitleKey, string resetBodyKey, string alertBodyKey)
    {
        var stateChanged = false;

        // A drop from a meaningfully-used window back near zero means the window rolled over.
        if (previous > 5 && percentage < 5)
        {
            _dedupTracker.ResetForWindow(keyPrefix);
            _toastService.Show(Loc.Get(resetTitleKey), Loc.Get(resetBodyKey));
            stateChanged = true;
        }

        foreach (var threshold in _settingsStore.Current.EffectiveNotificationThresholds)
        {
            if (percentage < threshold || !_dedupTracker.ShouldNotify($"{keyPrefix}{threshold}"))
                continue;

            _toastService.Show(Loc.Get("Toast_UsageAlertTitle"), Loc.Format(alertBodyKey, threshold));
            stateChanged = true;
        }

        return stateChanged;
    }
}
