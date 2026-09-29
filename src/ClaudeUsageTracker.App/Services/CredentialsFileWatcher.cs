using System.IO;
using System.Windows.Threading;
using ClaudeUsageTracker.Core.ClaudeCode;

namespace ClaudeUsageTracker.App.Services;

/// <summary>
/// Raises <see cref="Changed"/> (on the UI thread) shortly after Claude Code CLI rewrites
/// its Windows credentials file — after <c>claude auth login</c>, or when Claude Code
/// renews an expired token the next time it's used — so the tracker recovers right away
/// instead of waiting for the next timer tick. WSL credentials aren't watched; the
/// periodic refresh still picks them up.
/// </summary>
public sealed class CredentialsFileWatcher : IDisposable
{
    // The CLI may write the file in several steps; wait for it to settle before re-reading.
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(1);

    private readonly FileSystemWatcher? _watcher;
    private readonly DispatcherTimer _debounceTimer;

    public CredentialsFileWatcher()
    {
        _debounceTimer = new DispatcherTimer { Interval = Debounce };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            Changed?.Invoke();
        };

        var path = ClaudeCodeCredentialReader.CredentialsFilePath;
        var directory = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(directory))
            return; // Claude Code never ran here; the periodic refresh covers a later install.

        _watcher = new FileSystemWatcher(directory, Path.GetFileName(path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime
        };
        _watcher.Changed += OnFileEvent;
        _watcher.Created += OnFileEvent;
        _watcher.Renamed += OnFileEvent;
        _watcher.EnableRaisingEvents = true;
    }

    public event Action? Changed;

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounceTimer.Stop();
    }

    // FileSystemWatcher raises events on a thread-pool thread.
    private void OnFileEvent(object sender, FileSystemEventArgs e)
        => _debounceTimer.Dispatcher.BeginInvoke(() =>
        {
            _debounceTimer.Stop();
            _debounceTimer.Start();
        });
}
