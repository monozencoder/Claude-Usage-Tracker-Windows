using ClaudeUsageTracker.App.Localization;
using ClaudeUsageTracker.App.Settings;
using ClaudeUsageTracker.App.ViewModels;
using ClaudeUsageTracker.Core.History;
using ClaudeUsageTracker.Core.Models;
using ClaudeUsageTracker.Core.Notifications;
using ClaudeUsageTracker.Core.Status;
using ClaudeUsageTracker.Platform.Notifications;

namespace ClaudeUsageTracker.App.Services;

/// <summary>
/// Orchestrates a single refresh cycle: fetch usage (<see cref="UsageFetcher"/>),
/// update the flyout view model, record the sample for the history chart, evaluate
/// threshold/reset notifications, and report
/// the session status/percentage back to the caller so it can repaint the tray icon.
/// </summary>
public sealed class UsageRefreshCoordinator
{
    private const string SessionKeyPrefix = "session_";

    private readonly UsageFetcher _fetcher;
    private readonly AppSettingsStore _settingsStore;
    private readonly FlyoutViewModel _flyoutViewModel;
    private readonly UsageHistoryStore _history;
    private readonly IToastNotificationService _toastService;
    private readonly Action<double, UsageStatusLevel> _onIconUpdate;
    private readonly NotificationDedupTracker _dedupTracker;

    private double _lastSessionPercentage = -1;

    public UsageRefreshCoordinator(
        UsageFetcher fetcher,
        AppSettingsStore settingsStore,
        FlyoutViewModel flyoutViewModel,
        UsageHistoryStore history,
        IToastNotificationService toastService,
        Action<double, UsageStatusLevel> onIconUpdate)
    {
        _fetcher = fetcher;
        _settingsStore = settingsStore;
        _flyoutViewModel = flyoutViewModel;
        _history = history;
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
                _onIconUpdate(0, errorStatus);
            return;
        }

        var now = DateTimeOffset.Now;
        _flyoutViewModel.ApplyUsage(usage, now);
        _flyoutViewModel.ClearBanner();

        var effectiveSession = usage.EffectiveSessionPercentage(now);
        var elapsedFraction = UsageStatusCalculator.ElapsedFraction(
            usage.SessionResetTime, ClaudeUsage.SessionWindow, showRemaining: false, now);
        var status = UsageStatusCalculator.CalculateStatus(effectiveSession, showRemaining: false, elapsedFraction);

        _history.Record(new UsageSample(now, effectiveSession, usage.WeeklyPercentage));
        EvaluateNotifications(effectiveSession);
        _onIconUpdate(effectiveSession, status);
    }

    /// <summary>
    /// Fires threshold (the user's, 75/90/95% by default) and session-reset toasts, deduped so the same
    /// threshold doesn't re-notify every refresh cycle. Dedup state is only
    /// persisted when it actually changes.
    /// </summary>
    private void EvaluateNotifications(double effectiveSessionPercentage)
    {
        var previous = _lastSessionPercentage;
        _lastSessionPercentage = effectiveSessionPercentage;

        if (!_settingsStore.Current.NotificationsEnabled)
            return;

        var stateChanged = false;

        // A drop from a meaningfully-used session back near zero means the 5h window rolled over.
        if (previous > 5 && effectiveSessionPercentage < 5)
        {
            _dedupTracker.ResetForWindow(SessionKeyPrefix);
            _toastService.Show(Loc.Get("Toast_SessionResetTitle"), Loc.Get("Toast_SessionResetBody"));
            stateChanged = true;
        }

        foreach (var threshold in _settingsStore.Current.EffectiveNotificationThresholds)
        {
            if (effectiveSessionPercentage < threshold || !_dedupTracker.ShouldNotify($"{SessionKeyPrefix}{threshold}"))
                continue;

            _toastService.Show(Loc.Get("Toast_UsageAlertTitle"), Loc.Format("Toast_UsageAlertBody", threshold));
            stateChanged = true;
        }

        if (stateChanged)
        {
            _settingsStore.Current.NotifiedThresholdKeys = [.. _dedupTracker.SentKeys];
            _settingsStore.Save();
        }
    }
}
