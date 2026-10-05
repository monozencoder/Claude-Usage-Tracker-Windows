using System.Text.Json.Serialization;
using ClaudeUsageTracker.App.Localization;
using ClaudeUsageTracker.App.Themes;
using ClaudeUsageTracker.Platform.TrayIcon;

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

    public const int MaxNotificationThresholds = 5;

    /// <summary>Lower bound of <see cref="FlyoutOpacityPercent"/>: below this the flyout is too faint to read or find.</summary>
    public const int MinFlyoutOpacityPercent = 20;
    public const int MaxFlyoutOpacityPercent = 100;

    /// <summary>Range of the <see cref="TaskbarBarDisplaySettings.Offset"/>.</summary>
    public const int MinTaskbarBarOffset = -3000;
    public const int MaxTaskbarBarOffset = 300;

    /// <summary>Used in the default (token-using) mode; ignored while <see cref="AvoidTokenUsage"/> is on.</summary>
    public int RefreshIntervalSeconds { get; set; } = 60;

    public bool NotificationsEnabled { get; set; } = true;

    /// <summary>Session usage percentages a notification is sent at. Use <see cref="EffectiveNotificationThresholds"/>.</summary>
    public List<int> NotificationThresholds { get; set; } = [.. DefaultNotificationThresholds];

    /// <summary>What <see cref="NotificationThresholds"/> starts as, and what resetting it restores.</summary>
    public static IReadOnlyList<int> DefaultNotificationThresholds { get; } = [75, 90, 95];

    /// <summary>
    /// Also notifies for the weekly window: at the same <see cref="NotificationThresholds"/>, and
    /// when it resets. Only while <see cref="NotificationsEnabled"/> is on.
    /// </summary>
    public bool WeeklyNotificationsEnabled { get; set; } = true;

    /// <summary>Whether a reset is shown as the time left until it or as the time of day it happens.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<ResetTimeDisplay>))]
    public ResetTimeDisplay ResetTimeDisplay { get; set; } = ResetTimeDisplay.Remaining;

    /// <summary>What the tray icon draws.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<TrayIconStyle>))]
    public TrayIconStyle TrayIconStyle { get; set; } = TrayIconStyle.Ring;

    public bool ShowFlyoutOnStartup { get; set; } = true;

    /// <summary>Keeps the flyout above other windows (Topmost).</summary>
    public bool AlwaysOnTop { get; set; } = true;

    /// <summary>
    /// Lets the mouse through the flyout to whatever is behind it; holding Ctrl makes it clickable
    /// again. <see cref="TaskbarBarClickThrough"/> is the same for the taskbar bars.
    /// </summary>
    public bool FlyoutClickThrough { get; set; }

    public bool TaskbarBarClickThrough { get; set; }

    /// <summary>Shows the flyout cut down to one short line per usage row, about the size of the taskbar bars.</summary>
    public bool FlyoutCompact { get; set; }

    /// <summary>Shows the usage bars on the taskbar, left of the notification area.</summary>
    public bool ShowTaskbarBar { get; set; }

    /// <summary>
    /// Keeps the taskbar bars up while a full-screen app (a game, a video) covers the taskbar,
    /// drawn over that app where the taskbar would be. Off: they're hidden along with the taskbar.
    /// </summary>
    public bool ShowTaskbarBarOverFullScreen { get; set; }

    /// <summary>Which usage rows are shown: in the flyout, compact or not, and on the taskbar bars.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<UsageRows>))]
    public UsageRows VisibleRows { get; set; } = UsageRows.Both;

    /// <summary>
    /// Shows each row's short name ("5h") left of its bar in the compact views: the compact flyout
    /// and the taskbar bars. Off makes them narrower; their tooltip still names the rows.
    /// </summary>
    public bool CompactShowLabels { get; set; } = true;

    /// <summary>Shows each row's percentage as a number, right of its bar, in the compact views. Off leaves the bar to say it.</summary>
    public bool CompactShowPercentage { get; set; } = true;

    /// <summary>Shows when each row resets, right of its percentage, in the compact views.</summary>
    public bool CompactShowResetTime { get; set; } = true;

    // The settings the three above replaced, from when they were the taskbar bars' alone (and
    // names could also be shown in full, or were an on/off before that). Read from older
    // settings files so their choices carry over; never written back.

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonConverter(typeof(JsonStringEnumConverter<UsageRows>))]
    public UsageRows? TaskbarBarRows
    {
        get => null;
        set => VisibleRows = value ?? VisibleRows;
    }

    /// <summary>Was "Full", "Short" or "None".</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TaskbarBarLabels
    {
        get => null;
        set => CompactShowLabels = value is null ? CompactShowLabels : value != "None";
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? TaskbarBarShowLabels
    {
        get => null;
        set => CompactShowLabels = value ?? CompactShowLabels;
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? TaskbarBarShowResetTime
    {
        get => null;
        set => CompactShowResetTime = value ?? CompactShowResetTime;
    }

    /// <summary>Shows a small creature left of the taskbar bars that acts out how much usage is left.</summary>
    public bool TaskbarBarShowMascot { get; set; }

    /// <summary>Shows the same creature in the flyout: in its bottom-left corner, or left of the rows when compact.</summary>
    public bool FlyoutShowMascot { get; set; }

    /// <summary>How much the creature moves, wherever it's shown.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<MascotAnimation>))]
    public MascotAnimation MascotAnimation { get; set; } = MascotAnimation.Subtle;

    /// <summary>
    /// The on/off setting <see cref="MascotAnimation"/> replaced, read from older settings files
    /// (off carries over as Off) and never written back.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AnimateMascot
    {
        get => null;
        set
        {
            if (value == false)
                MascotAnimation = MascotAnimation.Off;
        }
    }

    /// <summary>
    /// Per-monitor placement of the taskbar bars, by the monitor's device name (e.g. "\\.\DISPLAY2").
    /// A monitor without an entry shows them, unshifted.
    /// </summary>
    public Dictionary<string, TaskbarBarDisplaySettings> TaskbarBarDisplays { get; set; } = [];

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

    /// <summary>
    /// <see cref="NotificationThresholds"/> as usable values: within 1–100, each once, ascending,
    /// at most <see cref="MaxNotificationThresholds"/> (the file may have been hand-edited).
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<int> EffectiveNotificationThresholds =>
        [.. NotificationThresholds.Where(threshold => threshold is >= 1 and <= 100).Distinct().Order().Take(MaxNotificationThresholds)];

    /// <summary><see cref="FlyoutOpacityPercent"/> as a WPF opacity (clamped: the file may have been hand-edited).</summary>
    [JsonIgnore]
    public double FlyoutOpacity => Math.Clamp(FlyoutOpacityPercent, MinFlyoutOpacityPercent, MaxFlyoutOpacityPercent) / 100.0;
}

public enum ResetTimeDisplay { Remaining, Clock }

/// <summary>Which of the two usage windows are shown.</summary>
public enum UsageRows { Both, SessionOnly, WeeklyOnly }

/// <summary>How much the creature moves.</summary>
public enum MascotAnimation
{
    /// <summary>Not at all: its face still follows the usage, but it holds a pose.</summary>
    Off,

    /// <summary>Now and then: when its mood changes, and briefly once a minute.</summary>
    Subtle,

    /// <summary>All the time.</summary>
    Lively
}

/// <summary>Whether one monitor's taskbar gets the usage bars, and where on it.</summary>
public sealed class TaskbarBarDisplaySettings
{
    public bool Show { get; set; } = true;

    /// <summary>
    /// How far the bars are shifted from where they're placed automatically, in device-independent
    /// pixels (negative is left). Clamped when applied.
    /// </summary>
    public int Offset { get; set; }
}

/// <summary>A point in screen (device) pixels.</summary>
public sealed record ScreenPoint(int X, int Y);
