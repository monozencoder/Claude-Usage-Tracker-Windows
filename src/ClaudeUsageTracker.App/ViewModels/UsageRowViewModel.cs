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
    public required double Percentage { get; init; }
    public required string PercentageText { get; init; }
    public required string ResetText { get; init; }

    /// <summary>Time left until the reset, short enough for the taskbar: "2:15", or "3d 4h" from a day up.</summary>
    public required string ShortResetText { get; init; }

    public required UsageStatusLevel Status { get; init; }

    public static UsageRowViewModel For(UsageRowKind kind, double percentage, DateTimeOffset? resetTime, DateTimeOffset now)
    {
        var status = UsageStatusCalculator.CalculateStatus(percentage, showRemaining: false, elapsedFraction: null);
        return new UsageRowViewModel
        {
            Kind = kind,
            Label = Loc.Get(kind == UsageRowKind.Session ? "Usage_Session" : "Usage_Weekly"),
            Percentage = Math.Clamp(percentage, 0, 100),
            PercentageText = $"{percentage:0.#}%",
            ResetText = resetTime is { } reset ? FormatReset(reset - now) : string.Empty,
            ShortResetText = resetTime is { } shortReset ? FormatShortReset(shortReset - now) : string.Empty,
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

    private static string FormatShortReset(TimeSpan delta)
    {
        if (delta < TimeSpan.Zero)
            delta = TimeSpan.Zero;

        return delta.TotalHours >= 24
            ? Loc.Format("Usage_ShortDaysHours", delta.Days, delta.Hours)
            : $"{(int)delta.TotalHours}:{delta.Minutes:00}";
    }
}
