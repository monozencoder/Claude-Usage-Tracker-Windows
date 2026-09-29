using System.Collections.ObjectModel;
using ClaudeUsageTracker.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClaudeUsageTracker.App.ViewModels;

public partial class FlyoutViewModel : ObservableObject
{
    [ObservableProperty]
    private string _statusText = "Not connected";

    /// <summary>Warning shown above the usage rows (credentials/API problems), or null when all is well.</summary>
    [ObservableProperty]
    private string? _bannerText;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private string _lastUpdatedText = "Never refreshed";

    /// <summary>Exact timestamp, shown as the tooltip of the relative LastUpdatedText.</summary>
    [ObservableProperty]
    private string? _lastUpdatedToolTip;

    private DateTimeOffset? _lastUpdatedAt;

    public ObservableCollection<UsageRowViewModel> Rows { get; } = [];

    public event Action? RefreshRequested;
    public event Action? SettingsRequested;

    [RelayCommand]
    private void Refresh() => RefreshRequested?.Invoke();

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke();

    public void SetBanner(string message) => BannerText = message;

    public void ClearBanner() => BannerText = null;

    public void ApplyUsage(ClaudeUsage usage, DateTimeOffset now)
    {
        StatusText = "Claude Code";
        Rows.Clear();
        Rows.Add(UsageRowViewModel.For("Session (5h)", usage.EffectiveSessionPercentage(now), usage.SessionResetTime, now));
        Rows.Add(UsageRowViewModel.For("Weekly (7d)", usage.WeeklyPercentage, usage.WeeklyResetTime, now));

        _lastUpdatedAt = usage.LastUpdated;
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
            { TotalMinutes: < 1 } => "Updated just now",
            { TotalHours: < 1 } => $"Updated {(int)elapsed.TotalMinutes} min ago",
            { TotalDays: < 1 } => $"Updated {(int)elapsed.TotalHours} h ago",
            _ => $"Updated {(int)elapsed.TotalDays} d ago"
        };

        var localUpdatedAt = updatedAt.ToLocalTime();
        LastUpdatedToolTip = localUpdatedAt.Date == now.ToLocalTime().Date
            ? $"Last updated at {localUpdatedAt:HH:mm:ss}"
            : $"Last updated at {localUpdatedAt:yyyy-MM-dd HH:mm:ss}";
    }
}
