namespace ClaudeUsageTracker.Core.Models;

/// <summary>
/// Snapshot of Claude Code usage. Only session (5h) and weekly (7d) aggregate
/// percentages are available via the OAuth-authenticated Anthropic API endpoints
/// this app reads from — no per-model (Opus/Sonnet/...) or cost breakdown, unlike
/// claude.ai's web-only usage endpoint.
/// </summary>
public sealed record ClaudeUsage
{
    public double SessionPercentage { get; init; }
    public DateTimeOffset? SessionResetTime { get; init; }

    public double WeeklyPercentage { get; init; }
    public DateTimeOffset? WeeklyResetTime { get; init; }

    public required DateTimeOffset LastUpdated { get; init; }

    /// <summary>Session percentage, but 0 once the reset time has already passed (stale window).</summary>
    public double EffectiveSessionPercentage(DateTimeOffset now)
        => SessionResetTime is { } reset && now >= reset ? 0 : SessionPercentage;

    public double RemainingPercentage(DateTimeOffset now)
        => Math.Max(0, 100 - EffectiveSessionPercentage(now));
}
