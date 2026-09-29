using ClaudeUsageTracker.Core.Models;
using ClaudeUsageTracker.Core.Status;

namespace ClaudeUsageTracker.App.ViewModels;

/// <summary>Immutable per-row display data. Rows are rebuilt wholesale on each refresh (see FlyoutViewModel.ApplyUsage).</summary>
public sealed class UsageRowViewModel
{
    public required string Label { get; init; }
    public required double Percentage { get; init; }
    public required string PercentageText { get; init; }
    public required string ResetText { get; init; }
    public required UsageStatusLevel Status { get; init; }

    public static UsageRowViewModel For(string label, double percentage, DateTimeOffset? resetTime, DateTimeOffset now)
    {
        var status = UsageStatusCalculator.CalculateStatus(percentage, showRemaining: false, elapsedFraction: null);
        return new UsageRowViewModel
        {
            Label = label,
            Percentage = Math.Clamp(percentage, 0, 100),
            PercentageText = $"{percentage:0.#}%",
            ResetText = resetTime is { } reset ? $"Resets {FormatRelative(reset - now)}" : string.Empty,
            Status = status
        };
    }

    private static string FormatRelative(TimeSpan delta)
    {
        if (delta <= TimeSpan.Zero)
            return "soon";

        return delta.TotalHours >= 24
            ? $"in {delta.Days}d {delta.Hours}h"
            : $"in {(int)delta.TotalHours}h {delta.Minutes}m";
    }
}
