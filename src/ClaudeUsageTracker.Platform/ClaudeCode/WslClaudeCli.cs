using System.Text;
using ClaudeUsageTracker.Core.ClaudeCode;
using ClaudeUsageTracker.Core.Models;
using ClaudeUsageTracker.Platform.Processes;

namespace ClaudeUsageTracker.Platform.ClaudeCode;

/// <summary>
/// Reads Claude Code CLI credentials inside an installed WSL distro by shelling out
/// to wsl.exe, mirroring <see cref="ClaudeCodeCredentialReader"/> for the
/// Windows-native case. This lets the app find credentials for users who only run
/// Claude Code from within WSL rather than Windows directly.
/// </summary>
internal static class WslClaudeCli
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

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

    // wsl.exe writes UTF-16LE to a redirected stdout regardless of what the guest
    // process itself wrote, so StandardOutputEncoding must be set explicitly or
    // the output decodes as mojibake.
    private static string? Run(string[] args, TimeSpan timeout)
        => ProcessRunner.Run(SystemExecutables.Wsl, args, timeout, Encoding.Unicode) is { ExitCode: 0 } result
            ? result.StandardOutput
            : null;
}
