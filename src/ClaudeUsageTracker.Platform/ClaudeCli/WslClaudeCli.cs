using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using ClaudeUsageTracker.Core.ClaudeCode;
using ClaudeUsageTracker.Core.Models;

namespace ClaudeUsageTracker.Platform.ClaudeCli;

/// <summary>
/// Reads and refreshes Claude Code CLI credentials inside an installed WSL
/// distro by shelling out to wsl.exe, mirroring
/// <see cref="ClaudeCodeCredentialReader"/> and <see cref="ClaudeCliRefresher"/>
/// for the Windows-native case. This lets the app find credentials for users who
/// only run Claude Code from within WSL rather than Windows directly.
/// </summary>
public static class WslClaudeCli
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RefreshTimeout = TimeSpan.FromSeconds(30);

    public static IReadOnlyList<string> ListDistros()
    {
        var output = Run(["-l", "-q"], ProbeTimeout);
        if (output is null)
            return [];

        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Length > 0)
            .ToArray();
    }

    public static ClaudeCodeCredentials? TryReadCredentials(string distro)
    {
        var output = Run(["-d", distro, "--", "sh", "-lc", "cat ~/.claude/.credentials.json"], ProbeTimeout);
        return output is null ? null : ClaudeCodeCredentialReader.Parse(output);
    }

    /// <summary>
    /// Nudges the WSL distro's own Claude CLI to refresh its token, the same way
    /// <see cref="ClaudeCliRefresher.TryRefresh"/> does for the Windows CLI.
    /// Best-effort: failures are swallowed since the caller treats "credentials
    /// still expired after this" as the real failure signal.
    /// </summary>
    public static void TryRefresh(string distro)
    {
        const string command =
            "if command -v claude >/dev/null 2>&1; then claude -p .; " +
            "elif [ -x \"$HOME/.local/bin/claude\" ]; then \"$HOME/.local/bin/claude\" -p .; " +
            "else exit 127; fi";

        Run(["-d", distro, "--", "bash", "-lic", command], RefreshTimeout);
    }

    // wsl.exe writes UTF-16LE to a redirected stdout regardless of what the guest
    // process itself wrote, so StandardOutputEncoding must be set explicitly or
    // the output decodes as mojibake.
    private static string? Run(string[] args, TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "wsl.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.Unicode
        };

        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
                return null;

            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }

            return process.ExitCode == 0 ? output : null;
        }
        catch (Win32Exception)
        {
            // wsl.exe isn't installed/invocable — nothing more we can do here.
            return null;
        }
    }
}
