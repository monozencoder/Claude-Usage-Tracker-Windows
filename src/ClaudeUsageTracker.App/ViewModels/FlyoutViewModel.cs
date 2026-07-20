using System.Collections.ObjectModel;
using ClaudeUsageTracker.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClaudeUsageTracker.App.ViewModels;

public partial class FlyoutViewModel : ObservableObject
{
    [ObservableProperty]
    private string _statusText = "Not connected";

    [ObservableProperty]
    private string? _bannerText;

    [ObservableProperty]
    private bool _bannerIsError;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private string _lastUpdatedText = "Never refreshed";

    private DateTimeOffset? _lastUpdatedAt;

    /// <summary>When true, the flyout stays open instead of auto-hiding when it loses focus.</summary>
    [ObservableProperty]
    private bool _isPinned;

    public ObservableCollection<UsageRowViewModel> Rows { get; } = [];

    public event Action? RefreshRequested;
    public event Action? SettingsRequested;

    [RelayCommand]
    private void Refresh() => RefreshRequested?.Invoke();

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke();

    [RelayCommand]
    private void TogglePin() => IsPinned = !IsPinned;

    public void SetBanner(string message, bool isError)
    {
        BannerText = message;
        BannerIsError = isError;
    }

    public void ClearBanner() => BannerText = null;

    public void ApplyUsage(ClaudeUsage usage)
    {
        var now = DateTimeOffset.Now;

        StatusText = "Claude Code";
        Rows.Clear();
        Rows.Add(UsageRowViewModel.For("Session (5h)", usage.EffectiveSessionPercentage(now), usage.SessionResetTime));
        Rows.Add(UsageRowViewModel.For("Weekly (7d)", usage.WeeklyPercentage, usage.WeeklyResetTime));

        _lastUpdatedAt = usage.LastUpdated;
        RefreshLastUpdatedText(now);
    }

    /// <summary>Recomputes the "Updated HH:mm:ss (Ns ago)" text. Called once per second
    /// while the flyout is visible so the elapsed-time portion counts up live.</summary>
    public void RefreshLastUpdatedText(DateTimeOffset now)
    {
        if (_lastUpdatedAt is not { } updatedAt)
            return;

        var elapsed = now - updatedAt;
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;

        var elapsedText = elapsed.TotalMinutes >= 1
            ? $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s ago"
            : $"{(int)elapsed.TotalSeconds}s ago";

        LastUpdatedText = $"Updated {updatedAt:HH:mm:ss} ({elapsedText})";
    }
}
