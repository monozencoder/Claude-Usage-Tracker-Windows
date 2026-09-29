using System.Collections.ObjectModel;
using ClaudeUsageTracker.App.Localization;
using ClaudeUsageTracker.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClaudeUsageTracker.App.ViewModels;

public partial class FlyoutViewModel : ObservableObject
{
    /// <summary>Warning shown above the usage rows (credentials/API problems), or null when all is well.</summary>
    [ObservableProperty]
    private string? _bannerText;

    /// <summary>Shows a Sign in button in the banner (the problem is a missing/expired sign-in).</summary>
    [ObservableProperty]
    private bool _canSignIn;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private string _lastUpdatedText = Loc.Get("Flyout_NeverRefreshed");

    /// <summary>Exact timestamp, shown as the tooltip of the relative LastUpdatedText.</summary>
    [ObservableProperty]
    private string? _lastUpdatedToolTip;

    private DateTimeOffset? _lastUpdatedAt;
    private ClaudeUsage? _lastUsage;

    public ObservableCollection<UsageRowViewModel> Rows { get; } = [];

    public event Action? RefreshRequested;
    public event Action? SettingsRequested;
    public event Action? SignInRequested;

    [RelayCommand]
    private void Refresh() => RefreshRequested?.Invoke();

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke();

    [RelayCommand]
    private void SignIn() => SignInRequested?.Invoke();

    public void SetBanner(string message, bool canSignIn = false)
    {
        BannerText = message;
        CanSignIn = canSignIn;
    }

    public void ClearBanner()
    {
        BannerText = null;
        CanSignIn = false;
    }

    public void ApplyUsage(ClaudeUsage usage, DateTimeOffset now)
    {
        _lastUsage = usage;
        _lastUpdatedAt = usage.LastUpdated;
        RenderUsage(now);
    }

    /// <summary>Rebuilds the code-built texts in the current language (after a language switch).</summary>
    public void RefreshLanguage()
    {
        if (_lastUsage is null)
        {
            LastUpdatedText = Loc.Get("Flyout_NeverRefreshed");
            return;
        }

        RenderUsage(DateTimeOffset.Now);
    }

    private void RenderUsage(DateTimeOffset now)
    {
        var usage = _lastUsage!;
        Rows.Clear();
        Rows.Add(UsageRowViewModel.For(Loc.Get("Usage_Session"), usage.EffectiveSessionPercentage(now), usage.SessionResetTime, now));
        Rows.Add(UsageRowViewModel.For(Loc.Get("Usage_Weekly"), usage.WeeklyPercentage, usage.WeeklyResetTime, now));
        RefreshLastUpdatedText(now);
    }

    /// <summary>Recomputes the relative "Updated N min ago" text (minute granularity, so it
    /// rarely changes width) and its exact-time tooltip. Called periodically while the
    /// flyout is visible so the relative time stays current.</summary>
    public void RefreshLastUpdatedText(DateTimeOffset now)
    {
        if (_lastUpdatedAt is not { } updatedAt)
            return;

        var elapsed = now - updatedAt;
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;

        LastUpdatedText = elapsed switch
        {
            { TotalMinutes: < 1 } => Loc.Get("Flyout_UpdatedJustNow"),
            { TotalHours: < 1 } => Loc.Format("Flyout_UpdatedMinutesAgo", (int)elapsed.TotalMinutes),
            { TotalDays: < 1 } => Loc.Format("Flyout_UpdatedHoursAgo", (int)elapsed.TotalHours),
            _ => Loc.Format("Flyout_UpdatedDaysAgo", (int)elapsed.TotalDays)
        };

        var localUpdatedAt = updatedAt.ToLocalTime();
        LastUpdatedToolTip = localUpdatedAt.Date == now.ToLocalTime().Date
            ? Loc.Format("Flyout_LastUpdatedAt", localUpdatedAt.ToString("HH:mm:ss"))
            : Loc.Format("Flyout_LastUpdatedAt", localUpdatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
    }
}
