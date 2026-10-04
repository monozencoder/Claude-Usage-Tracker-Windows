using System.IO;
using ClaudeUsageTracker.Core.History;

namespace ClaudeUsageTracker.App.Services;

/// <summary>
/// Keeps the last week of usage samples for the history chart, in a small text file next to
/// the settings (one sample a line, appended as they come). A history that can't be read or
/// written is simply shorter: it's a convenience, never worth an error.
/// </summary>
public sealed class UsageHistoryStore
{
    private static readonly string HistoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClaudeUsageTracker", "history.csv");

    private readonly UsageHistory _history = new();

    public UsageHistoryStore()
    {
        Load();
    }

    public IReadOnlyList<UsageSample> Samples => _history.Samples;

    /// <summary>Raised after a sample was added.</summary>
    public event Action? Changed;

    public void Record(UsageSample sample)
    {
        if (!_history.Add(sample))
            return;

        _history.Prune(sample.Time);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(HistoryPath)!);
            File.AppendAllLines(HistoryPath, [UsageHistory.Format(sample)]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Still charted for this run; just not there after a restart.
        }

        Changed?.Invoke();
    }

    // Reads the file and, since it only ever grows otherwise, rewrites it without what has aged out.
    private void Load()
    {
        try
        {
            if (!File.Exists(HistoryPath))
                return;

            var lines = File.ReadAllLines(HistoryPath);
            foreach (var line in lines)
            {
                if (UsageHistory.TryParse(line, out var sample))
                    _history.Add(sample);
            }

            _history.Prune(DateTimeOffset.Now);
            if (_history.Samples.Count != lines.Length)
                File.WriteAllLines(HistoryPath, _history.Samples.Select(UsageHistory.Format));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
