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

        LastUpdatedText = $"Updated {usage.LastUpdated:t}";
    }
}
