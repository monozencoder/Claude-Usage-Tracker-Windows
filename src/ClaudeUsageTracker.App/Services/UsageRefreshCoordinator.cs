using ClaudeUsageTracker.App.ViewModels;
using ClaudeUsageTracker.Core.Api;
using ClaudeUsageTracker.Core.Models;
using ClaudeUsageTracker.Core.Status;
using ClaudeUsageTracker.Platform.ClaudeCli;
using ClaudeUsageTracker.Platform.Notifications;

namespace ClaudeUsageTracker.App.Services;

/// <summary>
/// Orchestrates a single refresh cycle: locate Claude Code CLI's own credentials
/// (Windows-native or WSL, whichever has them), nudge that CLI to refresh its
/// token if expired/rejected, fetch usage from Anthropic's API, update the
/// flyout view model, evaluate threshold/reset notifications, and report the
/// session status/percentage back to the caller so it can repaint the tray icon.
/// </summary>
public sealed class UsageRefreshCoordinator
{
    private static readonly int[] NotificationThresholds = [75, 90, 95];

    private readonly ClaudeCodeUsageClient _usageClient;
    private readonly AppSettingsStore _settingsStore;
    private readonly FlyoutViewModel _flyoutViewModel;
    private readonly IToastNotificationService _toastService;
    private readonly Action<double, UsageStatusLevel> _onIconUpdate;

    private readonly NotificationDedupTracker _dedupTracker;
    private AppSettings _settings;
    private double _lastSessionPercentage = -1;

    public UsageRefreshCoordinator(
        ClaudeCodeUsageClient usageClient,
        AppSettingsStore settingsStore,
        FlyoutViewModel flyoutViewModel,
        IToastNotificationService toastService,
        Action<double, UsageStatusLevel> onIconUpdate)
    {
        _usageClient = usageClient;
        _settingsStore = settingsStore;
        _flyoutViewModel = flyoutViewModel;
        _toastService = toastService;
        _onIconUpdate = onIconUpdate;

        _settings = settingsStore.Load();
        _dedupTracker = new NotificationDedupTracker(_settings.NotifiedThresholdKeys);
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        _flyoutViewModel.IsRefreshing = true;
        try
        {
            var resolution = ClaudeCredentialResolver.Resolve();
            var credentials = resolution.Credentials;

            if (credentials is null)
            {
                var message = resolution.CredentialsFileFound
                    ? "Claude Code CLI's token is expired. Run \"claude\" in a terminal to sign in again."
                    : "Claude Code CLI credentials not found. Install Claude Code and sign in, then refresh.";
                var level = resolution.CredentialsFileFound ? UsageStatusLevel.Critical : UsageStatusLevel.Safe;

                _flyoutViewModel.SetBanner(message, isError: true);
                _onIconUpdate(0, level);
                return;
            }

            ClaudeUsage usage;
            try
            {
                usage = await _usageClient.GetUsageAsync(credentials, ct);
            }
            catch (AuthRequiredException)
            {
                var refreshed = resolution.Source?.TryRefreshAndReread();
                if (refreshed is null)
                {
                    _flyoutViewModel.SetBanner(
                        "Claude Code CLI's token was rejected and could not be refreshed. Run \"claude\" in a terminal to sign in again.",
                        isError: true);
                    _onIconUpdate(0, UsageStatusLevel.Critical);
                    return;
                }

                usage = await _usageClient.GetUsageAsync(refreshed, ct);
            }

            _flyoutViewModel.ApplyUsage(usage);
            _flyoutViewModel.ClearBanner();

            var effectiveSession = usage.EffectiveSessionPercentage(DateTimeOffset.Now);
            var elapsedFraction = UsageStatusCalculator.ElapsedFraction(
                usage.SessionResetTime, TimeSpan.FromHours(5), showRemaining: false, now: DateTimeOffset.Now);
            var status = UsageStatusCalculator.CalculateStatus(effectiveSession, showRemaining: false, elapsedFraction);

            EvaluateNotifications(effectiveSession);

            _onIconUpdate(effectiveSession, status);
        }
        catch (ClaudeApiException ex)
        {
            _flyoutViewModel.SetBanner($"Failed to refresh usage (status {ex.StatusCode}).", isError: true);
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
        if (!_settings.NotificationsEnabled)
        {
            _lastSessionPercentage = effectiveSessionPercentage;
            return;
        }

        var stateChanged = false;

        // A drop from a meaningfully-used session back near zero means the 5h window rolled over.
        if (_lastSessionPercentage > 5 && effectiveSessionPercentage < 5)
        {
            _dedupTracker.ResetForWindow("session_");
            _toastService.Show("Claude session reset", "Your 5-hour session window has reset.");
            stateChanged = true;
        }

        _lastSessionPercentage = effectiveSessionPercentage;

        foreach (var threshold in NotificationThresholds)
        {
            if (effectiveSessionPercentage < threshold)
                continue;

            var key = $"session_{threshold}";
            if (!_dedupTracker.ShouldNotify(key))
                continue;

            _toastService.Show("Claude usage alert", $"Session usage has reached {threshold}%.");
            stateChanged = true;
        }

        if (stateChanged)
        {
            _settings.NotifiedThresholdKeys = [.. _dedupTracker.SentKeys];
            _settingsStore.Save(_settings);
        }
    }

    /// <summary>Reloads settings from disk (e.g. after the Settings window saves changes).</summary>
    public void ReloadSettings() => _settings = _settingsStore.Load();
}
