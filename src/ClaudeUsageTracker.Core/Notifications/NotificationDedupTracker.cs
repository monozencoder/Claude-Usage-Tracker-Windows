namespace ClaudeUsageTracker.Core.Notifications;

/// <summary>
/// Tracks which threshold notifications (e.g. "session_75") have already fired so
/// the same threshold doesn't re-notify every refresh cycle, and when the window they were
/// sent in ends, after which they no longer count. Mirrors the macOS app's sentNotifications
/// set. Persistence is the caller's responsibility (App layer's AppStateStore) — this class
/// just holds/mutates the in-memory state and says when it changed (<see cref="TakeChanged"/>).
/// </summary>
public sealed class NotificationDedupTracker(
    IEnumerable<string>? initialKeys = null,
    IReadOnlyDictionary<string, DateTimeOffset>? initialWindowEnds = null)
{
    private readonly HashSet<string> _sentKeys = initialKeys is null
        ? []
        : [.. initialKeys];

    private readonly Dictionary<string, DateTimeOffset> _windowEnds = initialWindowEnds is null
        ? []
        : new(initialWindowEnds);

    private bool _changed;

    public IReadOnlyCollection<string> SentKeys => _sentKeys;

    /// <summary>When the window the sent keys belong to ends, by window key prefix (e.g. "session_").</summary>
    public IReadOnlyDictionary<string, DateTimeOffset> WindowEnds => _windowEnds;

    /// <summary>Returns true (and records the key) the first time it's asked about; false on repeats.</summary>
    public bool ShouldNotify(string key)
    {
        var added = _sentKeys.Add(key);
        _changed |= added;
        return added;
    }

    /// <summary>Records when the window whose keys are being sent ends.</summary>
    public void SetWindowEnd(string windowKeyPrefix, DateTimeOffset end)
    {
        if (_windowEnds.TryGetValue(windowKeyPrefix, out var current) && current == end)
            return;
        _windowEnds[windowKeyPrefix] = end;
        _changed = true;
    }

    /// <summary>Whether the window the sent keys belong to is over, as far as its end is known.</summary>
    public bool HasWindowEnded(string windowKeyPrefix, DateTimeOffset now) =>
        _windowEnds.TryGetValue(windowKeyPrefix, out var end) && now >= end;

    /// <summary>Whether there are sent keys for a window whose end isn't known (they were saved by a version that didn't record it).</summary>
    public bool LacksWindowEnd(string windowKeyPrefix) =>
        !_windowEnds.ContainsKey(windowKeyPrefix) && _sentKeys.Any(key => key.StartsWith(windowKeyPrefix, StringComparison.Ordinal));

    /// <summary>Clears dedup state for a window (e.g. "session_") once that window has reset, so thresholds can refire next cycle.</summary>
    public void ResetForWindow(string windowKeyPrefix)
    {
        var removedKeys = _sentKeys.RemoveWhere(k => k.StartsWith(windowKeyPrefix, StringComparison.Ordinal));
        _changed |= _windowEnds.Remove(windowKeyPrefix) || removedKeys > 0;
    }

    /// <summary>Whether anything changed since this was last asked, i.e. whether there is something to persist.</summary>
    public bool TakeChanged()
    {
        var changed = _changed;
        _changed = false;
        return changed;
    }
}
