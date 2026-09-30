using ClaudeUsageTracker.Core.Api;

namespace ClaudeUsageTracker.Tests.Api;

public class ModelNamesTests
{
    [Theory]
    [InlineData("claude-haiku-4-5", "Claude Haiku 4.5")]
    [InlineData("claude-haiku-4-5-20251001", "Claude Haiku 4.5")]
    [InlineData("claude-sonnet-5-5", "Claude Sonnet 5.5")]
    [InlineData("claude-opus-5", "Claude Opus 5")]
    public void ToDisplayName_FormatsFamilyAndVersion(string id, string expected)
        => Assert.Equal(expected, ModelNames.ToDisplayName(id));

    [Theory]
    [InlineData("gpt-4")]
    [InlineData("claude-3-haiku-20240307")]
    [InlineData("claude-sonnet")]
    [InlineData("claude-mythos-preview")]
    public void ToDisplayName_LeavesUnfamiliarIdsUnchanged(string id)
        => Assert.Equal(id, ModelNames.ToDisplayName(id));
}
