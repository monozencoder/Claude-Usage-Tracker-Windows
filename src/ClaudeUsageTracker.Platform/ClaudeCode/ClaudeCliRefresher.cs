using ClaudeUsageTracker.Platform.Processes;

namespace ClaudeUsageTracker.Platform.ClaudeCode;

/// <summary>
/// Nudges the installed Claude Code CLI to refresh its own OAuth token by
/// invoking it with a throwaway minimal prompt. Claude Code CLI owns its
/// credentials file and refresh-token rotation entirely — this app never
/// performs an OAuth refresh itself, only asks the CLI to do it, then re-reads
/// the resulting file. Best-effort: all failures are swallowed since the caller
/// treats "credentials still expired after this" as the real failure signal.
/// </summary>
public static class ClaudeCliRefresher
{
    private static readonly TimeSpan RefreshTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    private static readonly string[] CandidateNames = ["claude.cmd", "claude"];

    public static void TryRefresh()
    {
        var claudePath = ResolveClaudePath();
        string[] claudeArgs = ["-p", "."];

        // .cmd shims (npm installs) have to go through cmd.exe.
        var isCmd = claudePath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);
        var (fileName, arguments) = isCmd
            ? ("cmd.exe", (string[])["/c", claudePath, .. claudeArgs])
            : (claudePath, claudeArgs);

        ProcessRunner.Run(fileName, arguments, RefreshTimeout, configure: startInfo =>
        {
            // Don't let the child think it's running nested inside another Claude Code session.
            startInfo.EnvironmentVariables.Remove("CLAUDECODE");
            startInfo.EnvironmentVariables.Remove("CLAUDE_CODE_ENTRYPOINT");
        });
    }

    private static string ResolveClaudePath()
    {
        foreach (var name in CandidateNames)
        {
            if (ProcessRunner.Run(name, ["--version"], ProbeTimeout) is not null)
                return name;
        }

        foreach (var name in CandidateNames)
        {
            if (TryResolveViaWhere(name) is { } path)
                return path;
        }

        return CandidateNames[0];
    }

    private static string? TryResolveViaWhere(string name)
    {
        var result = ProcessRunner.Run("where.exe", [name], ProbeTimeout);
        if (result is not { ExitCode: 0 } found)
            return null;

        return found.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
    }
}
