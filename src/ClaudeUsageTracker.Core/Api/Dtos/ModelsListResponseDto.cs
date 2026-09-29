using System.Text.Json.Serialization;

namespace ClaudeUsageTracker.Core.Api.Dtos;

/// <summary>Raw shape of GET https://api.anthropic.com/v1/models (only the fields the probe needs).</summary>
public sealed class ModelsListResponseDto
{
    [JsonPropertyName("data")]
    public List<ModelInfoDto> Data { get; init; } = [];
}

public sealed class ModelInfoDto
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }
}
