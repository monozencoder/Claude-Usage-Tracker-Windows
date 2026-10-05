namespace ClaudeUsageTracker.Core.Notifications;

/// <summary>The two usage windows a notification can be about.</summary>
public enum UsageWindow { Session, Weekly }

/// <summary>A notification to raise. The app words it in the user's language.</summary>
/// <param name="Threshold">The usage percentage that was reached, or null when the window has reset.</param>
public readonly record struct UsageNotification(UsageWindow Window, int? Threshold);

/// <summary>
/// Decides which threshold and window-reset notifications a new usage reading calls for, for the
/// session window and, if the user wants them, the weekly one — deduped (through
/// <paramref name="dedupTracker"/>) so the same threshold doesn't re-notify every refresh cycle.
/// </summary>
public sealed class UsageNotificationEvaluator(NotificationDedupTracker dedupTracker)
{
    private const string SessionKeyPrefix = "session_";
    private const string WeeklyKeyPrefix = "weekly_";

    private double _lastSessionPercentage = -1;
    private double _lastWeeklyPercentage = -1;

    /// <summary>
    /// Takes a reading and returns the notifications it calls for, in the order to show them.
    /// The tracker's dedup state changed exactly when the result isn't empty; persisting it is
    /// the caller's job.
    /// </summary>
    /// <param name="thresholds">The usage percentages to notify at, ascending.</param>
    public IReadOnlyList<UsageNotification> Evaluate(
        double sessionPercentage, double weeklyPercentage, IReadOnlyList<int> thresholds, bool enabled, bool weeklyEnabled)
    {
        var previousSession = _lastSessionPercentage;
        var previousWeekly = _lastWeeklyPercentage;
        _lastSessionPercentage = sessionPercentage;
        _lastWeeklyPercentage = weeklyPercentage;

        var notifications = new List<UsageNotification>();
        if (!enabled)
            return notifications;

        EvaluateWindow(UsageWindow.Session, SessionKeyPrefix, previousSession, sessionPercentage, thresholds, notifications);
        if (weeklyEnabled)
            EvaluateWindow(UsageWindow.Weekly, WeeklyKeyPrefix, previousWeekly, weeklyPercentage, thresholds, notifications);
        return notifications;
    }

    private void EvaluateWindow(
        UsageWindow window, string keyPrefix, double previous, double percentage, IReadOnlyList<int> thresholds, List<UsageNotification> notifications)
    {
        // A drop from a meaningfully-used window back near zero means the window rolled over.
        if (previous > 5 && percentage < 5)
        {
            dedupTracker.ResetForWindow(keyPrefix);
            notifications.Add(new UsageNotification(window, null));
        }

        foreach (var threshold in thresholds)
        {
            if (percentage >= threshold && dedupTracker.ShouldNotify($"{keyPrefix}{threshold}"))
                notifications.Add(new UsageNotification(window, threshold));
        }
    }
}
