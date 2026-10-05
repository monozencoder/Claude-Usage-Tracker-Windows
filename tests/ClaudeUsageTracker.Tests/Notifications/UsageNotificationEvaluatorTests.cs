using ClaudeUsageTracker.Core.Notifications;

namespace ClaudeUsageTracker.Tests.Notifications;

public class UsageNotificationEvaluatorTests
{
    private static readonly int[] Thresholds = [75, 90, 95];

    private readonly NotificationDedupTracker _tracker = new();
    private readonly UsageNotificationEvaluator _evaluator;

    public UsageNotificationEvaluatorTests() => _evaluator = new UsageNotificationEvaluator(_tracker);

    private IReadOnlyList<UsageNotification> Evaluate(double session, double weekly, bool enabled = true, bool weeklyEnabled = true)
        => _evaluator.Evaluate(session, weekly, Thresholds, enabled, weeklyEnabled);

    [Fact]
    public void BelowEveryThreshold_NotifiesNothing()
    {
        Assert.Empty(Evaluate(session: 50, weekly: 20));
        Assert.Empty(_tracker.SentKeys);
    }

    [Fact]
    public void ReachingAThreshold_NotifiesOnce()
    {
        var first = Evaluate(session: 80, weekly: 20);
        var second = Evaluate(session: 82, weekly: 20);

        Assert.Equal([new UsageNotification(UsageWindow.Session, 75)], first);
        Assert.Empty(second);
    }

    [Fact]
    public void JumpingPastSeveralThresholds_NotifiesEachInAscendingOrder()
    {
        var notifications = Evaluate(session: 96, weekly: 20);

        Assert.Equal(
            [
                new UsageNotification(UsageWindow.Session, 75),
                new UsageNotification(UsageWindow.Session, 90),
                new UsageNotification(UsageWindow.Session, 95)
            ],
            notifications);
    }

    [Fact]
    public void BothWindows_AreNotifiedSessionFirst()
    {
        var notifications = Evaluate(session: 80, weekly: 91);

        Assert.Equal(
            [
                new UsageNotification(UsageWindow.Session, 75),
                new UsageNotification(UsageWindow.Weekly, 75),
                new UsageNotification(UsageWindow.Weekly, 90)
            ],
            notifications);
    }

    [Fact]
    public void WeeklyOff_LeavesTheWeeklyWindowOut()
    {
        var notifications = Evaluate(session: 80, weekly: 91, weeklyEnabled: false);

        Assert.Equal([new UsageNotification(UsageWindow.Session, 75)], notifications);
    }

    [Fact]
    public void NotificationsOff_NotifiesNothingAndRecordsNothing()
    {
        Assert.Empty(Evaluate(session: 96, weekly: 96, enabled: false));
        Assert.Empty(_tracker.SentKeys);
    }

    [Fact]
    public void WindowRollingOver_NotifiesTheResetAndLetsItsThresholdsFireAgain()
    {
        Evaluate(session: 80, weekly: 20);

        var reset = Evaluate(session: 1, weekly: 20);
        var again = Evaluate(session: 76, weekly: 20);

        Assert.Equal([new UsageNotification(UsageWindow.Session, null)], reset);
        Assert.Equal([new UsageNotification(UsageWindow.Session, 75)], again);
    }

    [Fact]
    public void ResetOfOneWindow_KeepsTheOtherWindowsDedupState()
    {
        Evaluate(session: 80, weekly: 80);

        Evaluate(session: 1, weekly: 80);
        var after = Evaluate(session: 2, weekly: 81);

        Assert.Empty(after); // weekly 75 was already notified, and still is
    }

    [Fact]
    public void FirstReadingNearZero_IsNotAReset()
    {
        // Nothing was seen before it, so there is no drop to speak of.
        Assert.Empty(Evaluate(session: 1, weekly: 1));
    }

    [Fact]
    public void SmallDropNearZero_IsNotAReset()
    {
        Evaluate(session: 4, weekly: 20);

        Assert.Empty(Evaluate(session: 2, weekly: 20));
    }

    [Fact]
    public void ResetIsSeenEvenIfNotificationsWereOffForTheReadingBefore()
    {
        // The previous reading is remembered whether or not it was allowed to notify.
        Evaluate(session: 60, weekly: 20, enabled: false);

        var notifications = Evaluate(session: 1, weekly: 20);

        Assert.Equal([new UsageNotification(UsageWindow.Session, null)], notifications);
    }

    [Fact]
    public void KeysAlreadySent_BeforeARestart_AreNotNotifiedAgain()
    {
        var evaluator = new UsageNotificationEvaluator(new NotificationDedupTracker(["session_75"]));

        var notifications = evaluator.Evaluate(92, 20, Thresholds, enabled: true, weeklyEnabled: true);

        Assert.Equal([new UsageNotification(UsageWindow.Session, 90)], notifications);
    }
}
