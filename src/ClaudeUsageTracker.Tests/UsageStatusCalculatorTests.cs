using ClaudeUsageTracker.Core.Models;
using ClaudeUsageTracker.Core.Status;
using Xunit;

namespace ClaudeUsageTracker.Tests;

public class UsageStatusCalculatorTests
{
    [Theory]
    [InlineData(30, 0.5, UsageStatusLevel.Safe)]     // projected 60
    [InlineData(40, 0.5, UsageStatusLevel.Moderate)] // projected 80
    [InlineData(50, 0.5, UsageStatusLevel.Critical)] // projected 100
    public void CalculateStatus_UsesPaceProjection_WhenElapsedFractionInRange(double used, double elapsed, UsageStatusLevel expected)
    {
        var status = UsageStatusCalculator.CalculateStatus(used, showRemaining: false, elapsedFraction: elapsed);
        Assert.Equal(expected, status);
    }

    [Theory]
    [InlineData(0.05)] // below 0.15 lower bound
    [InlineData(1.0)]  // at/above upper bound
    public void CalculateStatus_FallsBackToStaticThresholds_WhenElapsedFractionOutOfRange(double elapsed)
    {
        // used=50 with showRemaining=false would be Safe under static thresholds (<70),
        // but would be Critical if (incorrectly) pace-projected at these extreme fractions.
        var status = UsageStatusCalculator.CalculateStatus(50, showRemaining: false, elapsedFraction: elapsed);
        Assert.Equal(UsageStatusLevel.Safe, status);
    }

    [Fact]
    public void CalculateStatus_IgnoresPaceProjection_WhenUsageIsZero()
    {
        var status = UsageStatusCalculator.CalculateStatus(0, showRemaining: false, elapsedFraction: 0.9);
        Assert.Equal(UsageStatusLevel.Safe, status);
    }

    [Theory]
    [InlineData(50, UsageStatusLevel.Safe)]     // remaining 50 >= 30
    [InlineData(85, UsageStatusLevel.Moderate)] // remaining 15, in [10,30)
    [InlineData(95, UsageStatusLevel.Critical)] // remaining 5 < 10
    public void CalculateStatus_StaticRemainingThresholds(double used, UsageStatusLevel expected)
    {
        var status = UsageStatusCalculator.CalculateStatus(used, showRemaining: true, elapsedFraction: null);
        Assert.Equal(expected, status);
    }

    [Theory]
    [InlineData(50, UsageStatusLevel.Safe)]
    [InlineData(75, UsageStatusLevel.Moderate)]
    [InlineData(95, UsageStatusLevel.Critical)]
    public void CalculateStatus_StaticUsedThresholds(double used, UsageStatusLevel expected)
    {
        var status = UsageStatusCalculator.CalculateStatus(used, showRemaining: false, elapsedFraction: null);
        Assert.Equal(expected, status);
    }

    [Fact]
    public void ElapsedFraction_ComputesFractionOfWindowElapsed()
    {
        var now = DateTimeOffset.UtcNow;
        var duration = TimeSpan.FromHours(5);
        // 1h elapsed of a 5h window -> reset time is 4h from now.
        var resetTime = now.AddHours(4);

        var fraction = UsageStatusCalculator.ElapsedFraction(resetTime, duration, showRemaining: false, now: now);
        var inverted = UsageStatusCalculator.ElapsedFraction(resetTime, duration, showRemaining: true, now: now);

        Assert.Equal(0.2, fraction, precision: 3);
        Assert.Equal(0.8, inverted, precision: 3);
    }

    [Fact]
    public void ElapsedFraction_ReturnsZero_WhenResetTimeUnknown()
    {
        var fraction = UsageStatusCalculator.ElapsedFraction(null, TimeSpan.FromHours(5), showRemaining: false, now: DateTimeOffset.UtcNow);
        Assert.Equal(0, fraction);
    }

    [Theory]
    [InlineData(30, false, 30)]
    [InlineData(30, true, 70)]
    public void GetDisplayPercentage_TogglesUsedVsRemaining(double used, bool showRemaining, double expected)
    {
        Assert.Equal(expected, UsageStatusCalculator.GetDisplayPercentage(used, showRemaining));
    }
}
