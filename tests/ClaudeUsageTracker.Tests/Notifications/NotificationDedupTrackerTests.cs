using ClaudeUsageTracker.Core.Notifications;

namespace ClaudeUsageTracker.Tests.Notifications;

public class NotificationDedupTrackerTests
{
    [Fact]
    public void ShouldNotify_ReturnsTrueOnlyTheFirstTime()
    {
        var tracker = new NotificationDedupTracker();

        Assert.True(tracker.ShouldNotify("session_75"));
        Assert.False(tracker.ShouldNotify("session_75"));
        Assert.True(tracker.ShouldNotify("session_90"));
    }

    [Fact]
    public void ShouldNotify_RespectsInitialKeys()
    {
        var tracker = new NotificationDedupTracker(["session_75"]);

        Assert.False(tracker.ShouldNotify("session_75"));
        Assert.Equal(["session_75"], tracker.SentKeys);
    }

    [Fact]
    public void ResetForWindow_ClearsOnlyKeysWithThatPrefix()
    {
        var tracker = new NotificationDedupTracker(["session_75", "session_90", "weekly_75"]);

        tracker.ResetForWindow("session_");

        Assert.Equal(["weekly_75"], tracker.SentKeys);
        Assert.True(tracker.ShouldNotify("session_75"));
    }
}
