using System.Text.Json.Serialization;

namespace ClaudeUsageTracker.Core.Api.Dtos;

/// <summary>Raw shape of GET https://api.anthropic.com/api/oauth/usage.</summary>
public sealed class OAuthUsageResponseDto
{
    [JsonPropertyName("five_hour")]
    public OAuthUsageBucketDto? FiveHour { get; init; }

    [JsonPropertyName("seven_day")]
    public OAuthUsageBucketDto? SevenDay { get; init; }
}

public sealed class OAuthUsageBucketDto
{
    [JsonPropertyName("utilization")]
    public double Utilization { get; init; }

    [JsonPropertyName("resets_at")]
    public string? ResetsAt { get; init; }
}
