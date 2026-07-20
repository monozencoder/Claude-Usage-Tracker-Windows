namespace ClaudeUsageTracker.Platform.Notifications;

/// <summary>
/// Tracks which threshold notifications (e.g. "session_75") have already fired so
/// the same threshold doesn't re-notify every refresh cycle. Mirrors the macOS
/// app's sentNotifications set. Persistence is the caller's responsibility (App
/// layer's AppSettingsStore) — this class just holds/mutates the in-memory set.
/// </summary>
public sealed class NotificationDedupTracker(IEnumerable<string>? initialKeys = null)
{
    private readonly HashSet<string> _sentKeys = initialKeys is null
        ? []
        : [.. initialKeys];

    public IReadOnlyCollection<string> SentKeys => _sentKeys;

    /// <summary>Returns true (and records the key) the first time it's asked about; false on repeats.</summary>
    public bool ShouldNotify(string key) => _sentKeys.Add(key);

    /// <summary>Clears dedup state for a window (e.g. "session_") once that window has reset, so thresholds can refire next cycle.</summary>
    public void ResetForWindow(string windowKeyPrefix) => _sentKeys.RemoveWhere(k => k.StartsWith(windowKeyPrefix, StringComparison.Ordinal));

    public void Clear() => _sentKeys.Clear();
}
