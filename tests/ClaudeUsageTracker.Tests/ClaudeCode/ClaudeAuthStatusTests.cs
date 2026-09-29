using ClaudeUsageTracker.Core.ClaudeCode;

namespace ClaudeUsageTracker.Tests.ClaudeCode;

public class ClaudeAuthStatusTests
{
    [Fact]
    public void Parse_ReadsAccountFields()
    {
        const string json = """
        { "loggedIn": true, "authMethod": "claude.ai", "email": "user@example.com",
          "orgName": "Example Org", "subscriptionType": "max" }
        """;

        var status = ClaudeAuthStatus.Parse(json);

        Assert.NotNull(status);
        Assert.True(status.LoggedIn);
        Assert.Equal("user@example.com", status.Email);
        Assert.Equal("Example Org", status.OrganizationName);
        Assert.Equal("Max", status.SubscriptionDisplayName);
    }

    [Fact]
    public void Parse_HandlesLoggedOutWithoutAccountFields()
    {
        var status = ClaudeAuthStatus.Parse("""{ "loggedIn": false }""");

        Assert.NotNull(status);
        Assert.False(status.LoggedIn);
        Assert.Null(status.Email);
        Assert.Null(status.SubscriptionDisplayName);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{ "email": "user@example.com" }""")]
    [InlineData("""{ "loggedIn": "yes" }""")]
    public void Parse_ReturnsNull_ForUnexpectedOutput(string output)
    {
        Assert.Null(ClaudeAuthStatus.Parse(output));
    }
}
