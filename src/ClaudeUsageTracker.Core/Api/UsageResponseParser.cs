using System.Globalization;
using System.Net.Http.Headers;
using ClaudeUsageTracker.Core.Api.Dtos;
using ClaudeUsageTracker.Core.Models;

namespace ClaudeUsageTracker.Core.Api;

/// <summary>Maps raw Anthropic API responses into the app's <see cref="ClaudeUsage"/> model.</summary>
public static class UsageResponseParser
{
    public static ClaudeUsage FromOAuthUsage(OAuthUsageResponseDto dto, DateTimeOffset now)
        => new()
        {
            SessionPercentage = dto.FiveHour?.Utilization ?? 0,
            SessionResetTime = ParseDate(dto.FiveHour?.ResetsAt),
            WeeklyPercentage = dto.SevenDay?.Utilization ?? 0,
            WeeklyResetTime = ParseDate(dto.SevenDay?.ResetsAt),
            LastUpdated = now
        };

    /// <summary>
    /// Parses the anthropic-ratelimit-unified-* headers returned by a Messages API
    /// call. Utilization here is a 0.0-1.0 fraction, unlike the OAuth usage
    /// endpoint's already-percentage values.
    /// </summary>
    public static ClaudeUsage FromRateLimitHeaders(HttpResponseHeaders headers, DateTimeOffset now)
    {
        var sessionPercentage = GetHeaderDouble(headers, "anthropic-ratelimit-unified-5h-utilization") * 100.0;
        var sessionReset = UnixSecondsToDate(GetHeaderLong(headers, "anthropic-ratelimit-unified-5h-reset"));
        var weeklyPercentage = GetHeaderDouble(headers, "anthropic-ratelimit-unified-7d-utilization") * 100.0;
        var weeklyReset = UnixSecondsToDate(GetHeaderLong(headers, "anthropic-ratelimit-unified-7d-reset"));

        if (sessionPercentage == 0 && weeklyPercentage == 0)
        {
            var status = GetHeaderString(headers, "anthropic-ratelimit-unified-status");
            if (status == "rejected")
            {
                var claim = GetHeaderString(headers, "anthropic-ratelimit-unified-representative-claim");
                switch (claim)
                {
                    case "five_hour":
                        sessionPercentage = 100;
                        break;
                    case "seven_day":
                        weeklyPercentage = 100;
                        break;
                }
            }

            if (sessionReset is null)
            {
                var overallReset = GetHeaderLong(headers, "anthropic-ratelimit-unified-reset");
                sessionReset = UnixSecondsToDate(overallReset);
            }
        }

        return new ClaudeUsage
        {
            SessionPercentage = sessionPercentage,
            SessionResetTime = sessionReset,
            WeeklyPercentage = weeklyPercentage,
            WeeklyResetTime = weeklyReset,
            LastUpdated = now
        };
    }

    private static double GetHeaderDouble(HttpResponseHeaders headers, string name)
        => GetHeaderString(headers, name) is { } value &&
           double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;

    private static long? GetHeaderLong(HttpResponseHeaders headers, string name)
        => GetHeaderString(headers, name) is { } value && long.TryParse(value, out var parsed)
            ? parsed
            : null;

    private static string? GetHeaderString(HttpResponseHeaders headers, string name)
        => headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static DateTimeOffset? UnixSecondsToDate(long? unixSeconds)
        => unixSeconds is { } seconds ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;

    private static DateTimeOffset? ParseDate(string? raw)
        => !string.IsNullOrWhiteSpace(raw) &&
           DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
}
