using System.Globalization;

namespace ClaudeUsageTracker.Core.History;

/// <summary>The session and weekly percentages as they were at one moment.</summary>
public readonly record struct UsageSample(DateTimeOffset Time, double Session, double Weekly);

/// <summary>
/// The usage samples of the last week, oldest first, for the history chart. Pure in-memory
/// logic (which samples to keep, and their one-line text form); reading and writing the file
/// is the caller's job.
/// </summary>
public sealed class UsageHistory
{
    /// <summary>How far back samples are kept: the weekly window, the longest the chart shows.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    /// <summary>
    /// Samples further apart than this weren't taken in one run of regular refreshes (the app was
    /// closed, the PC asleep, the API failing), so nothing is known about the time between them.
    /// </summary>
    public static readonly TimeSpan GapThreshold = TimeSpan.FromMinutes(30);

    // Refreshes can come every 10 seconds; a sample a minute is plenty for a chart of hours.
    private static readonly TimeSpan MinSpacing = TimeSpan.FromSeconds(55);

    // While nothing changes a sample is still kept this often, well inside GapThreshold, so a
    // quiet stretch reads as a flat line rather than as missing data.
    private static readonly TimeSpan Heartbeat = TimeSpan.FromMinutes(10);

    private readonly List<UsageSample> _samples = [];

    public IReadOnlyList<UsageSample> Samples => _samples;

    /// <summary>
    /// Adds <paramref name="sample"/> unless it's too close to the last one to be worth keeping.
    /// Returns whether it was added.
    /// </summary>
    public bool Add(UsageSample sample)
    {
        if (_samples.Count > 0)
        {
            var last = _samples[^1];
            var since = sample.Time - last.Time;
            var changed = sample.Session != last.Session || sample.Weekly != last.Weekly;
            // Also drops a sample from before the last one (the clock was set back): the list stays ordered.
            if (since < MinSpacing || (!changed && since < Heartbeat))
                return false;
        }

        _samples.Add(sample);
        return true;
    }

    /// <summary>Drops the samples older than <see cref="Retention"/>. Returns how many were dropped.</summary>
    public int Prune(DateTimeOffset now) => _samples.RemoveAll(sample => sample.Time < now - Retention);

    /// <summary>One line of the history file: Unix seconds, session %, weekly %.</summary>
    public static string Format(UsageSample sample) => string.Create(CultureInfo.InvariantCulture,
        $"{sample.Time.ToUnixTimeSeconds()},{sample.Session:0.##},{sample.Weekly:0.##}");

    public static bool TryParse(string line, out UsageSample sample)
    {
        sample = default;
        var parts = line.Split(',');
        if (parts.Length != 3
            || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var session)
            || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var weekly))
            return false;

        try
        {
            sample = new UsageSample(DateTimeOffset.FromUnixTimeSeconds(seconds), session, weekly);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
