using CommunityToolkit.Mvvm.ComponentModel;

namespace ClaudeUsageTracker.App.ViewModels;

/// <summary>Bindable fields for SettingsWindow.</summary>
public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private int _refreshIntervalSeconds = 60;

    [ObservableProperty]
    private bool _notificationsEnabled = true;

    [ObservableProperty]
    private bool _launchAtLoginEnabled;

    [ObservableProperty]
    private bool _showFlyoutOnStartup = true;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _statusIsError;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _credentialsSummary = "Checking for Claude Code CLI credentials...";
}
