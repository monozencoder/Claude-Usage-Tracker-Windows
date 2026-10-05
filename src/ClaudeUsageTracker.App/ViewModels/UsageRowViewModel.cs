using ClaudeUsageTracker.App.Localization;
using ClaudeUsageTracker.Core.Models;
using ClaudeUsageTracker.Core.Status;

namespace ClaudeUsageTracker.App.ViewModels;

/// <summary>Which usage window a row shows.</summary>
public enum UsageRowKind { Session, Weekly }

/// <summary>Immutable per-row display data. Rows are rebuilt wholesale on each refresh (see FlyoutViewModel.ApplyUsage).</summary>
public sealed class UsageRowViewModel
{
    public required UsageRowKind Kind { get; init; }
    public required string Label { get; init; }

    /// <summary>The row's name cut down to its window ("5h", "7d"), for the taskbar where width is scarce.</summary>
    public required string ShortLabel { get; init; }

    public required double Percentage { get; init; }
    public required string PercentageText { get; init; }
    public required string ResetText { get; init; }

    /// <summary>Time left until the reset, short enough for the taskbar: "45m", "2h 15m", or "3d 4h" from a day up.</summary>
    public required string ShortResetText { get; init; }

    // ShortResetText in its two parts ("4d" + "10h", "4h" + "2m", "" + "45m", "Thu" + "18:30"), so that rows of
    // different lengths can be lined up part by part instead of only at one edge.
    public string ShortResetLead => ShortResetText.LastIndexOf(' ') is var space and >= 0 ? ShortResetText[..space] : string.Empty;
    public string ShortResetTail => ShortResetText[(ShortResetText.LastIndexOf(' ') + 1)..];

    public required UsageStatusLevel Status { get; init; }

    /// <param name="clockTime">Shows the reset as the time of day it happens instead of the time left until it.</param>
    public static UsageRowViewModel For(UsageRowKind kind, double percentage, DateTimeOffset? resetTime, DateTimeOffset now, bool clockTime = false)
    {
        var status = UsageStatusCalculator.CalculateStatus(percentage, showRemaining: false, elapsedFraction: null);
        return new UsageRowViewModel
        {
            Kind = kind,
            Label = Loc.Get(kind == UsageRowKind.Session ? "Usage_Session" : "Usage_Weekly"),
            ShortLabel = Loc.Get(kind == UsageRowKind.Session ? "Usage_SessionShort" : "Usage_WeeklyShort"),
            Percentage = Math.Clamp(percentage, 0, 100),
            PercentageText = $"{percentage:0.#}%",
            ResetText = resetTime is not { } reset ? string.Empty
                : clockTime ? FormatResetClock(reset, now) : FormatReset(reset - now),
            ShortResetText = resetTime is not { } shortReset ? string.Empty
                : clockTime ? FormatShortResetClock(shortReset, now) : FormatShortReset(shortReset - now),
            Status = status
        };
    }

    private static string FormatReset(TimeSpan delta)
    {
        if (delta <= TimeSpan.Zero)
            return Loc.Get("Usage_ResetsSoon");

        return delta.TotalHours >= 24
            ? Loc.Format("Usage_ResetsInDaysHours", delta.Days, delta.Hours)
            : Loc.Format("Usage_ResetsInHoursMinutes", (int)delta.TotalHours, delta.Minutes);
    }

    // The reset as a time of day, in the user's time zone; with the day when it isn't today's.
    private static string FormatResetClock(DateTimeOffset reset, DateTimeOffset now)
    {
        if (reset <= now)
            return Loc.Get("Usage_ResetsSoon");

        var local = reset.ToLocalTime();
        return Loc.Format(local.Date == now.ToLocalTime().Date ? "Usage_ResetsAtTime" : "Usage_ResetsAtDayTime", local);
    }

    // "18:30", or "Thu 18:30" on another day: the weekday is enough, a reset is never more than a week off.
    private static string FormatShortResetClock(DateTimeOffset reset, DateTimeOffset now)
    {
        var local = reset.ToLocalTime();
        return Loc.Format(local.Date == now.ToLocalTime().Date ? "Usage_ShortAtTime" : "Usage_ShortAtDayTime", local);
    }

    private static string FormatShortReset(TimeSpan delta)
    {
        if (delta < TimeSpan.Zero)
            delta = TimeSpan.Zero;

        // Units throughout rather than "4:03", which also reads as a time of day or as minutes and seconds.
        return delta switch
        {
            { TotalHours: >= 24 } => Loc.Format("Usage_ShortDaysHours", delta.Days, delta.Hours),
            { TotalHours: >= 1 } => Loc.Format("Usage_ShortHoursMinutes", delta.Hours, delta.Minutes),
            _ => Loc.Format("Usage_ShortMinutes", delta.Minutes)
        };
    }
}
