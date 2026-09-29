using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using ClaudeUsageTracker.Core.ClaudeCode;
using ClaudeUsageTracker.Platform.Processes;

namespace ClaudeUsageTracker.Platform.ClaudeCode;

/// <summary>
/// Talks to the Windows-installed Claude Code CLI for account management only. Nothing
/// here sends a prompt, so none of it consumes usage: <c>claude auth status</c> reads
/// local state, and <c>claude auth login</c> runs the CLI's own browser sign-in. This
/// app never handles OAuth itself — the CLI writes its credentials file, the app reads it.
/// </summary>
public static class ClaudeCli
{
    private static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan LocateTimeout = TimeSpan.FromSeconds(5);

    // Where Claude Code's native installer puts the executable (no Node.js needed).
    private static readonly string NativeInstallPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude.exe");

    // Only a successful lookup is cached, so installing Claude Code while the app runs is picked up.
    private static string? _cachedPath;

    public static bool IsInstalled => Locate() is not null;

    /// <returns>The account status, or null if the CLI isn't installed or didn't answer.</returns>
    public static ClaudeAuthStatus? GetAuthStatus()
    {
        if (Locate() is not { } path)
            return null;

        var (fileName, arguments) = CommandFor(path, ["auth", "status", "--json"]);
        var result = ProcessRunner.Run(fileName, arguments, StatusTimeout, Encoding.UTF8);
        return result is { } r ? ClaudeAuthStatus.Parse(r.StandardOutput) : null;
    }

    /// <summary>
    /// Opens <c>claude auth login</c> in its own console window, which in turn opens the
    /// browser sign-in (Google account etc.). The console stays visible so the user can
    /// answer anything the CLI asks. Returns without waiting for sign-in to finish.
    /// </summary>
    /// <returns>False if the CLI isn't installed or couldn't be started.</returns>
    public static bool StartLogin()
    {
        if (Locate() is not { } path)
            return false;

        var (fileName, arguments) = CommandFor(path, ["auth", "login"]);
        var startInfo = new ProcessStartInfo(fileName) { UseShellExecute = true };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(startInfo);
            return process is not null;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private static string? Locate()
    {
        if (_cachedPath is not null && File.Exists(_cachedPath))
            return _cachedPath;

        _cachedPath = File.Exists(NativeInstallPath)
            ? NativeInstallPath
            : FindOnPath("claude.exe") ?? FindOnPath("claude.cmd"); // .cmd = npm global install
        return _cachedPath;
    }

    private static string? FindOnPath(string name)
        => ProcessRunner.Run("where.exe", [name], LocateTimeout) is { ExitCode: 0 } found
            ? found.StandardOutput
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault()
            : null;

    // npm's .cmd shims can't be started directly without a shell.
    private static (string FileName, string[] Arguments) CommandFor(string claudePath, string[] arguments)
        => claudePath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
            ? ("cmd.exe", ["/c", claudePath, .. arguments])
            : (claudePath, arguments);
}
