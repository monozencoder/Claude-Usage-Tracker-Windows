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
    /// frequent polling hard (and may take an hour or more to lift), so it's only asked every
    /// 5 minutes — polling every 2 minutes has been seen to work, this leaves headroom.
    /// </summary>
    public const int TokenFreeRefreshIntervalSeconds = 300;

    /// <summary>
    /// After a 429 from the usage endpoint, the next attempt waits this long instead (regular
    /// refreshes before then are skipped), and keeps doing so until a call succeeds. Never polls faster than the
    /// old fixed 10-minute interval while limited, so it can't prolong a limit more than that did.
    /// </summary>
    public const int TokenFreeRateLimitedRetrySeconds = 600;

    /// <summary>Lower bound of <see cref="FlyoutOpacityPercent"/>: below this the flyout is too faint to read or find.</summary>
    public const int MinFlyoutOpacityPercent = 20;
    public const int MaxFlyoutOpacityPercent = 100;

    /// <summary>Range of the taskbar bars' horizontal offsets, in device-independent pixels (negative is left).</summary>
    public const int MinTaskbarBarOffset = -800;
    public const int MaxTaskbarBarOffset = 200;

    /// <summary>Used in the default (token-using) mode; ignored while <see cref="AvoidTokenUsage"/> is on.</summary>
    public int RefreshIntervalSeconds { get; set; } = 60;

    public bool NotificationsEnabled { get; set; } = true;
    public bool ShowFlyoutOnStartup { get; set; } = true;

    /// <summary>Keeps the flyout above other windows (Topmost).</summary>
    public bool AlwaysOnTop { get; set; } = true;

    /// <summary>Shows the usage bars on the taskbar, left of the notification area.</summary>
    public bool ShowTaskbarBar { get; set; }

    /// <summary>Which monitors' taskbars get the usage bars.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<TaskbarBarMonitors>))]
    public TaskbarBarMonitors TaskbarBarMonitors { get; set; } = TaskbarBarMonitors.All;

    /// <summary>
    /// How far the usage bars are shifted from where they're placed automatically, on the main
    /// taskbar and on the other monitors' (whose automatic place is only a guess). Clamped when applied.
    /// </summary>
    public int TaskbarBarPrimaryOffset { get; set; }
    public int TaskbarBarSecondaryOffset { get; set; }

    /// <summary>How opaque the flyout is, in percent: 100 is solid, lower lets what's behind it show through.</summary>
    public int FlyoutOpacityPercent { get; set; } = MaxFlyoutOpacityPercent;

    /// <summary>
    /// On (default): only the free usage endpoint is used (no usage consumed), every 5 minutes —
    /// a usage tracker shouldn't quietly spend the usage it tracks.
    /// Off: usage is read from a one-token Messages API prompt on every refresh, which consumes a
    /// tiny amount of usage but isn't rate-limited like the usage endpoint, so it can refresh often.
    /// Existing settings files keep whatever they saved; the default only applies to new installs.
    /// </summary>
    public bool AvoidTokenUsage { get; set; } = true;

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

    /// <summary>Size the flyout was last dragged to, relative to its designed size (1 = 100%). Clamped when applied.</summary>
    public double FlyoutScale { get; set; } = 1;

    /// <summary>Threshold-notification dedup state (e.g. "session_75"), so the same threshold doesn't re-notify every refresh.</summary>
    public List<string> NotifiedThresholdKeys { get; set; } = [];

    /// <summary>The effective refresh interval for the current mode (clamped: the file may have been hand-edited).</summary>
    [JsonIgnore]
    public TimeSpan RefreshInterval => AvoidTokenUsage
        ? TimeSpan.FromSeconds(TokenFreeRefreshIntervalSeconds)
        : TimeSpan.FromSeconds(Math.Clamp(RefreshIntervalSeconds, MinRefreshIntervalSeconds, MaxRefreshIntervalSeconds));

    /// <summary><see cref="FlyoutOpacityPercent"/> as a WPF opacity (clamped: the file may have been hand-edited).</summary>
    [JsonIgnore]
    public double FlyoutOpacity => Math.Clamp(FlyoutOpacityPercent, MinFlyoutOpacityPercent, MaxFlyoutOpacityPercent) / 100.0;
}

public enum TaskbarBarMonitors { All, PrimaryOnly, SecondaryOnly }

/// <summary>A point in screen (device) pixels.</summary>
public sealed record ScreenPoint(int X, int Y);
