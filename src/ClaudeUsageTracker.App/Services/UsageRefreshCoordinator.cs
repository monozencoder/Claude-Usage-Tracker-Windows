using ClaudeUsageTracker.App.Settings;
using ClaudeUsageTracker.App.ViewModels;
using ClaudeUsageTracker.Core.Models;
using ClaudeUsageTracker.Core.Notifications;
using ClaudeUsageTracker.Core.Status;
using ClaudeUsageTracker.Platform.Notifications;

namespace ClaudeUsageTracker.App.Services;

/// <summary>
/// Orchestrates a single refresh cycle: fetch usage (<see cref="UsageFetcher"/>),
/// update the flyout view model, evaluate threshold/reset notifications, and report
/// the session status/percentage back to the caller so it can repaint the tray icon.
/// </summary>
public sealed class UsageRefreshCoordinator
{
    private static readonly int[] NotificationThresholds = [75, 90, 95];
    private const string SessionKeyPrefix = "session_";

    private readonly UsageFetcher _fetcher;
    private readonly AppSettingsStore _settingsStore;
    private readonly FlyoutViewModel _flyoutViewModel;
    private readonly IToastNotificationService _toastService;
    private readonly Action<double, UsageStatusLevel> _onIconUpdate;
    private readonly NotificationDedupTracker _dedupTracker;

    private double _lastSessionPercentage = -1;

    public UsageRefreshCoordinator(
        UsageFetcher fetcher,
        AppSettingsStore settingsStore,
        FlyoutViewModel flyoutViewModel,
        IToastNotificationService toastService,
        Action<double, UsageStatusLevel> onIconUpdate)
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
            var result = await _fetcher.FetchAsync(ct);
            if (result.Usage is not { } usage)
            {
                _flyoutViewModel.SetBanner(result.Error!);
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

            EvaluateNotifications(effectiveSession);
            _onIconUpdate(effectiveSession, status);
        }
        finally
        {
            _flyoutViewModel.IsRefreshing = false;
        }
    }

    /// <summary>
    /// Fires threshold (75/90/95%) and session-reset toasts, deduped so the same
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
            _toastService.Show("Claude session reset", "Your 5-hour session window has reset.");
            stateChanged = true;
        }

        foreach (var threshold in NotificationThresholds)
        {
            if (effectiveSessionPercentage < threshold || !_dedupTracker.ShouldNotify($"{SessionKeyPrefix}{threshold}"))
                continue;

            _toastService.Show("Claude usage alert", $"Session usage has reached {threshold}%.");
            stateChanged = true;
        }

        if (stateChanged)
        {
            _settingsStore.Current.NotifiedThresholdKeys = [.. _dedupTracker.SentKeys];
            _settingsStore.Save();
        }
    }
}
