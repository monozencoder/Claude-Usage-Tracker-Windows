using ClaudeUsageTracker.Core.Models;
using Xunit;

namespace ClaudeUsageTracker.Tests.Models;

public class ClaudeCodeCredentialsTests
{
    [Fact]
    public void IsExpired_ReturnsTrue_WhenExpiryInPast()
    {
        var now = DateTimeOffset.UtcNow;
        var expiredAt = now.AddMinutes(-5).ToUnixTimeMilliseconds();
        var credentials = new ClaudeCodeCredentials("token", expiredAt);

        Assert.True(credentials.IsExpired(now));
    }

    [Fact]
    public void IsExpired_ReturnsFalse_WhenExpiryInFuture()
    {
        var now = DateTimeOffset.UtcNow;
        var futureExpiry = now.AddMinutes(30).ToUnixTimeMilliseconds();
        var credentials = new ClaudeCodeCredentials("token", futureExpiry);

        Assert.False(credentials.IsExpired(now));
    }

    [Fact]
    public void IsExpired_ReturnsFalse_WhenExpiryUnknown()
    {
        var credentials = new ClaudeCodeCredentials("token", null);

        Assert.False(credentials.IsExpired(DateTimeOffset.UtcNow));
    }
}
