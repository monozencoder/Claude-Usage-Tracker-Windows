namespace ClaudeUsageTracker.App.Services;

/// <summary>
/// Non-secret app settings persisted as JSON under %APPDATA%. There are no
/// secrets to store — usage data comes from Claude Code CLI's own credentials
/// file, which this app only ever reads.
/// </summary>
public sealed class AppSettings
{
    public int RefreshIntervalSeconds { get; set; } = 60;
    public bool NotificationsEnabled { get; set; } = true;
    public bool LaunchAtLoginEnabled { get; set; }
    public bool ShowFlyoutOnStartup { get; set; } = true;

    /// <summary>Threshold-notification dedup state (e.g. "session_75"), so the same threshold doesn't re-notify every refresh.</summary>
    public List<string> NotifiedThresholdKeys { get; set; } = [];
}
