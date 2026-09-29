using ClaudeUsageTracker.Core.ClaudeCode;
using ClaudeUsageTracker.Core.Models;

namespace ClaudeUsageTracker.Platform.ClaudeCode;

/// <summary>
/// A single place Claude Code CLI credentials might live: Windows-native, or a
/// specific installed WSL distro. Claude Code CLI behaves identically from
/// either environment, so the app checks both when looking for credentials.
/// </summary>
public sealed class ClaudeCredentialSource
{
    private readonly Func<ClaudeCodeCredentials?> _read;
    private readonly Action _refresh;

    private ClaudeCredentialSource(Func<ClaudeCodeCredentials?> read, Action refresh)
    {
        _read = read;
        _refresh = refresh;
    }

    public static ClaudeCredentialSource Windows { get; } =
        new(ClaudeCodeCredentialReader.TryRead, ClaudeCliRefresher.TryRefresh);

    public static ClaudeCredentialSource ForWsl(string distro) =>
        new(() => WslClaudeCli.TryReadCredentials(distro), () => WslClaudeCli.TryRefresh(distro));

    /// <summary>Windows-native first, then every installed WSL distro, in listed order.</summary>
    public static IReadOnlyList<ClaudeCredentialSource> EnumerateAll()
    {
        var sources = new List<ClaudeCredentialSource> { Windows };
        sources.AddRange(WslClaudeCli.ListDistros().Select(ForWsl));
        return sources;
    }

    public ClaudeCodeCredentials? TryRead() => _read();

    /// <summary>Nudges this source's own Claude CLI to refresh its token, then re-reads.</summary>
    public ClaudeCodeCredentials? TryRefreshAndReread()
    {
        _refresh();
        return _read();
    }
}
