using System.ComponentModel;
using System.Diagnostics;

namespace ClaudeUsageTracker.Platform.ClaudeCli;

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

    public static void TryRefresh()
    {
        var claudePath = ResolveClaudePath();
        var isCmd = claudePath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);

        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        if (isCmd)
        {
            startInfo.FileName = "cmd.exe";
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(claudePath);
            startInfo.ArgumentList.Add("-p");
            startInfo.ArgumentList.Add(".");
        }
        else
        {
            startInfo.FileName = claudePath;
            startInfo.ArgumentList.Add("-p");
            startInfo.ArgumentList.Add(".");
        }

        startInfo.EnvironmentVariables.Remove("CLAUDECODE");
        startInfo.EnvironmentVariables.Remove("CLAUDE_CODE_ENTRYPOINT");

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
                return;

            if (!process.WaitForExit((int)RefreshTimeout.TotalMilliseconds))
                process.Kill(entireProcessTree: true);
        }
        catch (Win32Exception)
        {
            // Claude CLI isn't installed/invocable — nothing more we can do here.
        }
    }

    private static string ResolveClaudePath()
    {
        foreach (var name in (string[])["claude.cmd", "claude"])
        {
            if (CanInvoke(name))
                return name;
        }

        foreach (var name in (string[])["claude.cmd", "claude"])
        {
            if (TryResolveViaWhere(name, out var path))
                return path;
        }

        return "claude.cmd";
    }

    private static bool CanInvoke(string name)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = name,
                ArgumentList = { "--version" },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (process is null)
                return false;

            process.WaitForExit((int)ProbeTimeout.TotalMilliseconds);
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private static bool TryResolveViaWhere(string name, out string path)
    {
        path = string.Empty;
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "where.exe",
                ArgumentList = { name },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (process is null)
                return false;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit((int)ProbeTimeout.TotalMilliseconds);
            if (process.ExitCode != 0)
                return false;

            var firstLine = output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();

            if (string.IsNullOrEmpty(firstLine))
                return false;

            path = firstLine;
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}
