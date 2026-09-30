using System.Text.Json.Serialization;
using ClaudeUsageTracker.App.Localization;
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

    /// <summary>
    /// Fixed interval in token-free mode: the free usage endpoint rate-limits (HTTP 429)
    /// frequent polling hard, so it's only asked every 10 minutes.
    /// </summary>
    public const int TokenFreeRefreshIntervalSeconds = 600;

    /// <summary>Used in the default (token-using) mode; ignored while <see cref="AvoidTokenUsage"/> is on.</summary>
    public int RefreshIntervalSeconds { get; set; } = 60;

    public bool NotificationsEnabled { get; set; } = true;
    public bool ShowFlyoutOnStartup { get; set; } = true;

    /// <summary>Keeps the flyout above other windows (Topmost).</summary>
    public bool AlwaysOnTop { get; set; } = true;

    /// <summary>
    /// Off (default): usage is read from a one-token Messages API prompt on every refresh,
    /// which consumes a tiny amount of usage but isn't rate-limited like the usage endpoint.
    /// On: only the free usage endpoint is used (no usage consumed), every 10 minutes.
    /// </summary>
    public bool AvoidTokenUsage { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<AppTheme>))]
    public AppTheme Theme { get; set; } = AppTheme.System;

    [JsonConverter(typeof(JsonStringEnumConverter<AppLanguage>))]
    public AppLanguage Language { get; set; } = AppLanguage.System;

    /// <summary>
    /// Where the flyout was last dragged to (window top-left, screen pixels), or null if it never
    /// has been — then it opens near the tray/cursor. Clamped onto a monitor when applied, since
    /// the display it was saved on may be gone.
    /// </summary>
    public ScreenPoint? FlyoutPosition { get; set; }

    /// <summary>Threshold-notification dedup state (e.g. "session_75"), so the same threshold doesn't re-notify every refresh.</summary>
    public List<string> NotifiedThresholdKeys { get; set; } = [];

    /// <summary>The effective refresh interval for the current mode (clamped: the file may have been hand-edited).</summary>
    [JsonIgnore]
    public TimeSpan RefreshInterval => AvoidTokenUsage
        ? TimeSpan.FromSeconds(TokenFreeRefreshIntervalSeconds)
        : TimeSpan.FromSeconds(Math.Clamp(RefreshIntervalSeconds, MinRefreshIntervalSeconds, MaxRefreshIntervalSeconds));
}

/// <summary>A point in screen (device) pixels.</summary>
public sealed record ScreenPoint(int X, int Y);
