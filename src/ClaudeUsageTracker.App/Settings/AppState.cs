namespace ClaudeUsageTracker.App.Settings;

/// <summary>
/// What the app remembers between runs that isn't a setting: nothing here is chosen in the
/// settings window, the app writes it as it goes. Persisted as JSON under %APPDATA%, apart
/// from <see cref="AppSettings"/> so that saving it doesn't touch (or announce a change to)
/// the settings.
/// </summary>
public sealed class AppState
{
    /// <summary>
    /// Where the flyout was last dragged to (window top-left, screen pixels), or null if it never
    /// has been — then it opens near the tray/cursor. Clamped onto a monitor when applied, since
    /// the display it was saved on may be gone.
    /// </summary>
    public ScreenPoint? FlyoutPosition { get; set; }

    /// <summary>Threshold-notification dedup state (e.g. "session_75"), so the same threshold doesn't re-notify every refresh.</summary>
    public List<string> NotifiedThresholdKeys { get; set; } = [];

    /// <summary>
    /// When the usage window those keys were sent in ends, by the keys' prefix (e.g. "session_").
    /// Past that, they're from a window that is over and no longer hold a notification back —
    /// which is how a reset that happened while the app wasn't running is noticed.
    /// </summary>
    public Dictionary<string, DateTimeOffset> NotifiedWindowEnds { get; set; } = [];

    /// <summary>
    /// When the free usage endpoint was last called (whatever the outcome), and whether it
    /// answered 429 then. Kept here rather than in memory so restarting the app doesn't
    /// forget to space its calls out, which is what trips the endpoint's rate limit.
    /// </summary>
    public DateTimeOffset? LastUsageEndpointCall { get; set; }

    public bool UsageEndpointRateLimited { get; set; }
}

/// <summary>A point in screen (device) pixels.</summary>
public sealed record ScreenPoint(int X, int Y);
