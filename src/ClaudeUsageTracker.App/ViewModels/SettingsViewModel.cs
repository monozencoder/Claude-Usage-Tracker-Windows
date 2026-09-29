using System.Globalization;
using ClaudeUsageTracker.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClaudeUsageTracker.App.ViewModels;

/// <summary>Bindable fields for SettingsWindow.</summary>
public partial class SettingsViewModel : ObservableObject
{
    /// <summary>Amount the -/+ buttons, arrow keys and mouse wheel change the interval by.</summary>
    public const int RefreshIntervalStep = 5;

    private const int MinInterval = AppSettings.MinRefreshIntervalSeconds;
    private const int MaxInterval = AppSettings.MaxRefreshIntervalSeconds;

    // Last in-range value, restored if the box is left empty.
    private int _lastValidRefreshInterval = 60;

    /// <summary>Raw text of the interval box. Kept as a string so a half-typed or empty value doesn't fight the binding.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRefreshIntervalValid), nameof(RefreshIntervalError), nameof(CanSave))]
    private string _refreshIntervalText = "60";

    public bool IsRefreshIntervalValid => ParsedRefreshInterval is >= MinInterval and <= MaxInterval;

    /// <summary>Why the typed interval can't be saved, or null when it's fine (nothing is shown then).</summary>
    public string? RefreshIntervalError => ParsedRefreshInterval switch
    {
        null => $"Enter {MinInterval}–{MaxInterval} seconds",
        < MinInterval => $"Minimum is {MinInterval} seconds",
        > MaxInterval => $"Maximum is {MaxInterval} seconds",
        _ => null
    };

    /// <summary>The interval to persist: the typed value clamped to the allowed range.</summary>
    public int RefreshIntervalSeconds
    {
        get => ParsedRefreshInterval is { } s ? Math.Clamp(s, MinInterval, MaxInterval) : _lastValidRefreshInterval;
        set => RefreshIntervalText = Math.Clamp(value, MinInterval, MaxInterval).ToString(CultureInfo.InvariantCulture);
    }

    [ObservableProperty]
    private bool _notificationsEnabled = true;

    [ObservableProperty]
    private bool _launchAtLoginEnabled;

    [ObservableProperty]
    private bool _showFlyoutOnStartup = true;

    [ObservableProperty]
    private AppTheme _theme = AppTheme.System;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _statusIsError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private bool _isBusy;

    public bool CanSave => !IsBusy && IsRefreshIntervalValid;

    [ObservableProperty]
    private string _credentialsSummary = "Checking for Claude Code CLI credentials...";

    private int? ParsedRefreshInterval =>
        int.TryParse(RefreshIntervalText, NumberStyles.None, CultureInfo.InvariantCulture, out var s) ? s : null;

    partial void OnRefreshIntervalTextChanged(string value)
    {
        if (IsRefreshIntervalValid)
            _lastValidRefreshInterval = ParsedRefreshInterval!.Value;
    }

    /// <summary>Snaps the typed value into range (or restores the last valid one if the box is empty).</summary>
    public void NormalizeRefreshInterval() => RefreshIntervalSeconds = RefreshIntervalSeconds;

    [RelayCommand]
    private void IncreaseRefreshInterval() =>
        RefreshIntervalSeconds = (RefreshIntervalSeconds / RefreshIntervalStep + 1) * RefreshIntervalStep;

    [RelayCommand]
    private void DecreaseRefreshInterval() =>
        RefreshIntervalSeconds = (RefreshIntervalSeconds - 1) / RefreshIntervalStep * RefreshIntervalStep;
}
