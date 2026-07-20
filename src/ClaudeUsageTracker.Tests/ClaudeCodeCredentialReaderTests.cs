using ClaudeUsageTracker.Core.ClaudeCode;
using Xunit;

namespace ClaudeUsageTracker.Tests;

public class ClaudeCodeCredentialReaderTests
{
    [Fact]
    public void Parse_ExtractsAccessTokenAndExpiry()
    {
        const string json = """
        {
          "claudeAiOauth": {
            "accessToken": "sk-ant-oat01-abc123",
            "refreshToken": "sk-ant-ort01-xyz789",
            "expiresAt": 1234567890000,
            "scopes": ["user:inference"]
          }
        }
        """;

        var credentials = ClaudeCodeCredentialReader.Parse(json);

        Assert.NotNull(credentials);
        Assert.Equal("sk-ant-oat01-abc123", credentials!.AccessToken);
        Assert.Equal(1234567890000, credentials.ExpiresAtUnixMs);
    }

    [Fact]
    public void Parse_ReturnsNull_WhenClaudeAiOauthMissing()
    {
        Assert.Null(ClaudeCodeCredentialReader.Parse("{}"));
    }

    [Fact]
    public void Parse_ReturnsNull_WhenAccessTokenMissing()
    {
        const string json = """{ "claudeAiOauth": { "expiresAt": 123 } }""";
        Assert.Null(ClaudeCodeCredentialReader.Parse(json));
    }

    [Fact]
    public void Parse_ReturnsNull_OnMalformedJson()
    {
        Assert.Null(ClaudeCodeCredentialReader.Parse("not json"));
    }

    [Fact]
    public void Parse_ToleratesMissingExpiresAt()
    {
        const string json = """{ "claudeAiOauth": { "accessToken": "sk-ant-oat01-abc" } }""";

        var credentials = ClaudeCodeCredentialReader.Parse(json);

        Assert.NotNull(credentials);
        Assert.Null(credentials!.ExpiresAtUnixMs);
        Assert.False(credentials.IsExpired(DateTimeOffset.UtcNow));
    }
}
