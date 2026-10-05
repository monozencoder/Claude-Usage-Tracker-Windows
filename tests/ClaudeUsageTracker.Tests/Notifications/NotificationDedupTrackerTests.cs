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

    [Fact]
    public void ResetForWindow_ForgetsThatWindowsEnd()
    {
        var end = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
        var tracker = new NotificationDedupTracker(["session_75"], new Dictionary<string, DateTimeOffset> { ["session_"] = end });

        Assert.False(tracker.HasWindowEnded("session_", end.AddMinutes(-1)));
        Assert.True(tracker.HasWindowEnded("session_", end));

        tracker.ResetForWindow("session_");

        Assert.False(tracker.HasWindowEnded("session_", end));
        Assert.Empty(tracker.WindowEnds);
    }

    [Fact]
    public void TakeChanged_IsTrueOnceAfterAChange()
    {
        var tracker = new NotificationDedupTracker(["session_75"]);
        Assert.False(tracker.TakeChanged());

        tracker.ShouldNotify("session_75"); // a repeat: nothing new
        Assert.False(tracker.TakeChanged());

        tracker.ShouldNotify("session_90");
        Assert.True(tracker.TakeChanged());
        Assert.False(tracker.TakeChanged());
    }
}