namespace ClaudeUsageTracker.Core.Usage;

/// <summary>How often the free usage endpoint (token-free mode) may be asked.</summary>
public static class UsagePolling
{
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
    public const int RateLimitedRetrySeconds = 600;

    /// <summary>
    /// Shortest gap between two calls to the free usage endpoint. Refresh buttons, the tray menu,
    /// sign-in changes and Test connection all bypass the 5-minute timer; without this, a few
    /// clicks could trip its rate limit (HTTP 429), which can take an hour or more to lift.
    /// </summary>
    public static readonly TimeSpan MinUsageEndpointSpacing = TimeSpan.FromMinutes(2);
}
