using ClaudeUsageTracker.Core.Models;

namespace ClaudeUsageTracker.Core.Status;

/// <summary>
/// Pure status/pace logic, ported 1:1 from the macOS app's UsageStatusCalculator.
/// No I/O, no platform dependency — safe to unit test exhaustively.
/// </summary>
public static class UsageStatusCalculator
{
    private const double PaceProjectionLowerBound = 0.15;
    private const double PaceProjectionUpperBound = 1.0;
    private const double PaceCriticalThreshold = 90;
    private const double PaceModerateThreshold = 70;

    /// <summary>
    /// Determines Safe/Moderate/Critical for a usage percentage.
    /// </summary>
    /// <param name="usedPercentage">Percentage of the window already consumed (0-100).</param>
    /// <param name="showRemaining">Whether the UI is displaying "remaining" rather than "used" — affects only the static-threshold fallback below.</param>
    /// <param name="elapsedFraction">
    /// Fraction (0-1) of the reset window that has elapsed. When known and within
    /// [0.15, 1.0), usage is projected forward (used / elapsedFraction) and bucketed
    /// against 70%/90% — this "are you burning quota faster than time is passing"
    /// check applies regardless of display mode. When null (or outside that range),
    /// falls back to static thresholds keyed on <paramref name="showRemaining"/>.
    /// </param>
    public static UsageStatusLevel CalculateStatus(double usedPercentage, bool showRemaining, double? elapsedFraction)
    {
        if (elapsedFraction is { } t &&
            t >= PaceProjectionLowerBound && t < PaceProjectionUpperBound &&
            usedPercentage > 0)
        {
            var projected = usedPercentage / t;
            return projected >= PaceCriticalThreshold ? UsageStatusLevel.Critical
                 : projected >= PaceModerateThreshold ? UsageStatusLevel.Moderate
                 : UsageStatusLevel.Safe;
        }

        return showRemaining
            ? StaticRemainingStatus(usedPercentage)
            : StaticUsedStatus(usedPercentage);
    }

    private static UsageStatusLevel StaticRemainingStatus(double usedPercentage)
    {
        var remaining = 100 - usedPercentage;
        return remaining >= 30 ? UsageStatusLevel.Safe
             : remaining >= 10 ? UsageStatusLevel.Moderate
             : UsageStatusLevel.Critical;
    }

    private static UsageStatusLevel StaticUsedStatus(double usedPercentage)
    {
        return usedPercentage < 70 ? UsageStatusLevel.Safe
             : usedPercentage < 90 ? UsageStatusLevel.Moderate
             : UsageStatusLevel.Critical;
    }

    /// <summary>
    /// Fraction (0-1) of a reset window that has elapsed as of <paramref name="now"/>,
    /// inverted (1 - fraction) when <paramref name="showRemaining"/> is true.
    /// </summary>
    public static double ElapsedFraction(DateTimeOffset? resetTime, TimeSpan windowDuration, bool showRemaining, DateTimeOffset now)
    {
        if (resetTime is null || windowDuration <= TimeSpan.Zero)
            return 0;

        var windowStart = resetTime.Value - windowDuration;
        var elapsed = now - windowStart;
        var fraction = Math.Clamp(elapsed / windowDuration, 0, 1);
        return showRemaining ? 1 - fraction : fraction;
    }

    public static double GetDisplayPercentage(double usedPercentage, bool showRemaining)
        => showRemaining ? 100 - usedPercentage : usedPercentage;
}
