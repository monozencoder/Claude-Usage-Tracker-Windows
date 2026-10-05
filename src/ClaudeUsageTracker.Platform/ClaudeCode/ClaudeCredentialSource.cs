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

    private ClaudeCredentialSource(string displayName, bool isWsl, Func<ClaudeCodeCredentials?> read)
    {
        DisplayName = displayName;
        IsWsl = isWsl;
        _read = read;
    }

    /// <summary>"Windows", or "WSL (Ubuntu)" etc.</summary>
    public string DisplayName { get; }

    /// <summary>True for a WSL distro: its CLI can't be driven from here (e.g. to sign in).</summary>
    public bool IsWsl { get; }

    public static ClaudeCredentialSource Windows { get; } =
        new("Windows", isWsl: false, ClaudeCodeCredentialReader.TryRead);

    public static ClaudeCredentialSource ForWsl(string distro) =>
        new($"WSL ({distro})", isWsl: true, () => WslClaudeCli.TryReadCredentials(distro));

    /// <summary>
    /// Windows-native first, then every installed WSL distro, in listed order. Lazy: wsl.exe
    /// is only run once the caller moves past the Windows source, so a caller that stops
    /// there (usable Windows credentials) never starts it.
    /// </summary>
    public static IEnumerable<ClaudeCredentialSource> EnumerateAll()
    {
        yield return Windows;
        foreach (var distro in WslClaudeCli.ListDistros())
            yield return ForWsl(distro);
    }

    public ClaudeCodeCredentials? TryRead() => _read();
}
