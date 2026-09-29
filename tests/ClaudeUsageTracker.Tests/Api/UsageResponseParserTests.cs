using System.Text.Json;
using ClaudeUsageTracker.Core.Api;
using ClaudeUsageTracker.Core.Api.Dtos;
using Xunit;

namespace ClaudeUsageTracker.Tests.Api;

public class UsageResponseParserTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FromOAuthUsage_MapsFiveHourAndSevenDayBuckets()
    {
        const string json = """
        {
          "five_hour": { "utilization": 42.5, "resets_at": "2026-07-20T18:00:00Z" },
          "seven_day": { "utilization": 10, "resets_at": "2026-07-25T00:00:00Z" }
        }
        """;
        var dto = JsonSerializer.Deserialize<OAuthUsageResponseDto>(json)!;

        var usage = UsageResponseParser.FromOAuthUsage(dto, Now);

        Assert.Equal(42.5, usage.SessionPercentage);
        Assert.Equal(new DateTimeOffset(2026, 7, 20, 18, 0, 0, TimeSpan.Zero), usage.SessionResetTime);
        Assert.Equal(10, usage.WeeklyPercentage);
        Assert.Equal(new DateTimeOffset(2026, 7, 25, 0, 0, 0, TimeSpan.Zero), usage.WeeklyResetTime);
    }

    [Fact]
    public void FromOAuthUsage_DefaultsToZeroAndNull_WhenBucketsMissing()
    {
        var dto = JsonSerializer.Deserialize<OAuthUsageResponseDto>("{}")!;

        var usage = UsageResponseParser.FromOAuthUsage(dto, Now);

        Assert.Equal(0, usage.SessionPercentage);
        Assert.Null(usage.SessionResetTime);
        Assert.Equal(0, usage.WeeklyPercentage);
        Assert.Null(usage.WeeklyResetTime);
    }

    [Fact]
    public void FromRateLimitHeaders_ConvertsFractionToPercentageAndUnixResetTimes()
    {
        using var response = new HttpResponseMessage();
        response.Headers.TryAddWithoutValidation("anthropic-ratelimit-unified-5h-utilization", "0.42");
        response.Headers.TryAddWithoutValidation("anthropic-ratelimit-unified-5h-reset", "1795276800");
        response.Headers.TryAddWithoutValidation("anthropic-ratelimit-unified-7d-utilization", "0.1");
        response.Headers.TryAddWithoutValidation("anthropic-ratelimit-unified-7d-reset", "1795700000");

        var usage = UsageResponseParser.FromRateLimitHeaders(response.Headers, Now);

        Assert.Equal(42, usage.SessionPercentage, precision: 3);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1795276800), usage.SessionResetTime);
        Assert.Equal(10, usage.WeeklyPercentage, precision: 3);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1795700000), usage.WeeklyResetTime);
    }

    [Fact]
    public void FromRateLimitHeaders_TreatsRejectedFiveHourClaimAsFullyUsed()
    {
        using var response = new HttpResponseMessage();
        response.Headers.TryAddWithoutValidation("anthropic-ratelimit-unified-status", "rejected");
        response.Headers.TryAddWithoutValidation("anthropic-ratelimit-unified-representative-claim", "five_hour");
        response.Headers.TryAddWithoutValidation("anthropic-ratelimit-unified-reset", "1795276800");

        var usage = UsageResponseParser.FromRateLimitHeaders(response.Headers, Now);

        Assert.Equal(100, usage.SessionPercentage);
        Assert.Equal(0, usage.WeeklyPercentage);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1795276800), usage.SessionResetTime);
    }

    [Fact]
    public void FromRateLimitHeaders_TreatsRejectedSevenDayClaimAsFullyUsed()
    {
        using var response = new HttpResponseMessage();
        response.Headers.TryAddWithoutValidation("anthropic-ratelimit-unified-status", "rejected");
        response.Headers.TryAddWithoutValidation("anthropic-ratelimit-unified-representative-claim", "seven_day");

        var usage = UsageResponseParser.FromRateLimitHeaders(response.Headers, Now);

        Assert.Equal(0, usage.SessionPercentage);
        Assert.Equal(100, usage.WeeklyPercentage);
    }

    [Fact]
    public void FromRateLimitHeaders_ReturnsZero_WhenNoHeadersPresent()
    {
        using var response = new HttpResponseMessage();

        var usage = UsageResponseParser.FromRateLimitHeaders(response.Headers, Now);

        Assert.Equal(0, usage.SessionPercentage);
        Assert.Equal(0, usage.WeeklyPercentage);
        Assert.Null(usage.SessionResetTime);
        Assert.Null(usage.WeeklyResetTime);
    }
}
