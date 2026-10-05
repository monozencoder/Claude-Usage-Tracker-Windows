namespace ClaudeUsageTracker.Core.Notifications;

/// <summary>The two usage windows a notification can be about.</summary>
public enum UsageWindow { Session, Weekly }

/// <summary>A notification to raise. The app words it in the user's language.</summary>
/// <param name="Threshold">The usage percentage that was reached, or null when the window has reset.</param>
public readonly record struct UsageNotification(UsageWindow Window, int? Threshold);

/// <summary>One usage window as of a reading: how much of it is used, and when it resets, if known.</summary>
public readonly record struct UsageWindowReading(double Percentage, DateTimeOffset? ResetTime = null);

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
    /// Whether the tracker's dedup state changed (it can without a notification) is the tracker's
    /// to say; persisting it is the caller's job.
    /// </summary>
    /// <param name="thresholds">The usage percentages to notify at.</param>
    public IReadOnlyList<UsageNotification> Evaluate(
        UsageWindowReading session, UsageWindowReading weekly, DateTimeOffset now, IReadOnlyList<int> thresholds, bool enabled, bool weeklyEnabled)
    {
        var previousSession = _lastSessionPercentage;
        var previousWeekly = _lastWeeklyPercentage;
        _lastSessionPercentage = session.Percentage;
        _lastWeeklyPercentage = weekly.Percentage;

        var notifications = new List<UsageNotification>();
        if (!enabled)
            return notifications;

        EvaluateWindow(UsageWindow.Session, SessionKeyPrefix, previousSession, session, now, thresholds, notifications);
        if (weeklyEnabled)
            EvaluateWindow(UsageWindow.Weekly, WeeklyKeyPrefix, previousWeekly, weekly, now, thresholds, notifications);
        return notifications;
    }

    private void EvaluateWindow(
        UsageWindow window, string keyPrefix, double previous, UsageWindowReading reading, DateTimeOffset now,
        IReadOnlyList<int> thresholds, List<UsageNotification> notifications)
    {
        // A drop from a meaningfully-used window back near zero means the window rolled over. So
        // does the end of the window the sent keys belong to having passed: the only sign of it
        // if the app wasn't running then, or if the new window was already in use by the next reading.
        var dropped = previous > 5 && reading.Percentage < 5;
        if (dropped || dedupTracker.HasWindowEnded(keyPrefix, now))
        {
            dedupTracker.ResetForWindow(keyPrefix);
            // Only news if the old window was seen in use since the app started.
            if (previous > 5)
                notifications.Add(new UsageNotification(window, null));
        }

        // Several thresholds can be reached by one reading (a jump between refreshes, or usage
        // that built up while the app wasn't running). All of them count as sent, but only the
        // highest is worth a notification.
        int? highest = null;
        foreach (var threshold in thresholds)
        {
            if (reading.Percentage >= threshold && dedupTracker.ShouldNotify($"{keyPrefix}{threshold}"))
                highest = Math.Max(highest ?? threshold, threshold);
        }
        if (highest is not null)
            notifications.Add(new UsageNotification(window, highest));

        // When the window those keys belong to ends, for the check above next time.
        if (reading.ResetTime is { } end && end > now && (highest is not null || dedupTracker.LacksWindowEnd(keyPrefix)))
            dedupTracker.SetWindowEnd(keyPrefix, end);
    }
}
