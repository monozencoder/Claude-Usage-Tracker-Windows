using System.Text.Json.Serialization;
using ClaudeUsageTracker.App.Themes;

namespace ClaudeUsageTracker.App.Settings;

/// <summary>
/// Non-secret app settings persisted as JSON under %APPDATA%. There are no
/// secrets to store — usage data comes from Claude Code CLI's own credentials
/// file, which this app only ever reads.
/// </summary>
public sealed class AppSettings
{
    public const int MinRefreshIntervalSeconds = 10;
    public const int MaxRefreshIntervalSeconds = 3600;

    public int RefreshIntervalSeconds { get; set; } = 60;
    public bool NotificationsEnabled { get; set; } = true;
    public bool ShowFlyoutOnStartup { get; set; } = true;

    [JsonConverter(typeof(JsonStringEnumConverter<AppTheme>))]
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>Threshold-notification dedup state (e.g. "session_75"), so the same threshold doesn't re-notify every refresh.</summary>
    public List<string> NotifiedThresholdKeys { get; set; } = [];

    /// <summary><see cref="RefreshIntervalSeconds"/> clamped to the supported range (the file may have been hand-edited).</summary>
    [JsonIgnore]
    public TimeSpan RefreshInterval =>
        TimeSpan.FromSeconds(Math.Clamp(RefreshIntervalSeconds, MinRefreshIntervalSeconds, MaxRefreshIntervalSeconds));
}
