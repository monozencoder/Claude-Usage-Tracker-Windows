using ClaudeUsageTracker.Core.Notifications;

namespace ClaudeUsageTracker.Tests.Notifications;

public class UsageNotificationEvaluatorTests
{
    private static readonly int[] Thresholds = [75, 90, 95];

    private readonly NotificationDedupTracker _tracker = new();
    private readonly UsageNotificationEvaluator _evaluator;

    public UsageNotificationEvaluatorTests() => _evaluator = new UsageNotificationEvaluator(_tracker);

    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private IReadOnlyList<UsageNotification> Evaluate(
        double session, double weekly, bool enabled = true, bool weeklyEnabled = true,
        DateTimeOffset? now = null, DateTimeOffset? sessionReset = null, DateTimeOffset? weeklyReset = null)
        => Evaluate(_evaluator, session, weekly, enabled, weeklyEnabled, now, sessionReset, weeklyReset);

    private static IReadOnlyList<UsageNotification> Evaluate(
        UsageNotificationEvaluator evaluator, double session, double weekly, bool enabled = true, bool weeklyEnabled = true,
        DateTimeOffset? now = null, DateTimeOffset? sessionReset = null, DateTimeOffset? weeklyReset = null)
        => evaluator.Evaluate(new(session, sessionReset), new(weekly, weeklyReset), now ?? Now, Thresholds, enabled, weeklyEnabled);

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
    public void JumpingPastSeveralThresholds_NotifiesOnlyTheHighest()
    {
        var notifications = Evaluate(session: 92, weekly: 20);
        var later = Evaluate(session: 93, weekly: 20);
        var next = Evaluate(session: 96, weekly: 20);

        Assert.Equal([new UsageNotification(UsageWindow.Session, 90)], notifications);
        Assert.Empty(later); // 75 counts as sent too
        Assert.Equal([new UsageNotification(UsageWindow.Session, 95)], next);
    }

    [Fact]
    public void BothWindows_AreNotifiedSessionFirst()
    {
        var notifications = Evaluate(session: 80, weekly: 91);

        Assert.Equal(
            [
                new UsageNotification(UsageWindow.Session, 75),
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

        var notifications = Evaluate(evaluator, session: 92, weekly: 20);

        Assert.Equal([new UsageNotification(UsageWindow.Session, 90)], notifications);
    }

    [Fact]
    public void KeysSentInAWindowThatEndedWhileNotRunning_NoLongerCount()
    {
        // Notified at 75 and 90 in a session that was to reset an hour ago; the app starts in the next one.
        var tracker = new NotificationDedupTracker(
            ["session_75", "session_90", "weekly_75"],
            new Dictionary<string, DateTimeOffset> { ["session_"] = Now.AddHours(-1), ["weekly_"] = Now.AddDays(2) });
        var evaluator = new UsageNotificationEvaluator(tracker);

        var notifications = Evaluate(evaluator, session: 80, weekly: 80, sessionReset: Now.AddHours(3), weeklyReset: Now.AddDays(2));

        // No reset notification: that's old news by now. The weekly window is still the same one.
        Assert.Equal([new UsageNotification(UsageWindow.Session, 75)], notifications);
        Assert.Equal(Now.AddHours(3), tracker.WindowEnds["session_"]);
        Assert.True(tracker.TakeChanged());
    }

    [Fact]
    public void KeysSentInTheWindowStillRunning_AfterARestart_StillCount()
    {
        var tracker = new NotificationDedupTracker(
            ["session_75"], new Dictionary<string, DateTimeOffset> { ["session_"] = Now.AddHours(1) });
        var evaluator = new UsageNotificationEvaluator(tracker);

        Assert.Empty(Evaluate(evaluator, session: 80, weekly: 20, sessionReset: Now.AddHours(1)));
        Assert.False(tracker.TakeChanged());
    }

    [Fact]
    public void WindowEndingWhileRunning_WithTheNextAlreadyInUse_IsStillAReset()
    {
        Evaluate(session: 80, weekly: 20, sessionReset: Now.AddMinutes(2));

        // Five minutes on, the next session is already past 5%: no drop to near zero to go by.
        var after = Evaluate(session: 8, weekly: 20, now: Now.AddMinutes(5), sessionReset: Now.AddHours(5));
        var again = Evaluate(session: 76, weekly: 20, now: Now.AddMinutes(10), sessionReset: Now.AddHours(5));

        Assert.Equal([new UsageNotification(UsageWindow.Session, null)], after);
        Assert.Equal([new UsageNotification(UsageWindow.Session, 75)], again);
    }

    [Fact]
    public void KeysSavedWithoutAWindowEnd_TakeOnTheCurrentWindows()
    {
        var tracker = new NotificationDedupTracker(["session_75"]);
        var evaluator = new UsageNotificationEvaluator(tracker);

        Assert.Empty(Evaluate(evaluator, session: 80, weekly: 20, sessionReset: Now.AddHours(1)));
        Assert.Equal(Now.AddHours(1), tracker.WindowEnds["session_"]);
        Assert.False(tracker.WindowEnds.ContainsKey("weekly_")); // nothing sent for it
    }
}
