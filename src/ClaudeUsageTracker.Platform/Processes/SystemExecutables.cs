namespace ClaudeUsageTracker.Platform.Processes;

/// <summary>
/// Full paths of the Windows tools this app starts. Started by bare name, Windows would look
/// in the app's own folder and the current directory before System32, and run whatever
/// same-named file it found there.
/// </summary>
internal static class SystemExecutables
{
    public static string Cmd { get; } = Path.Combine(Environment.SystemDirectory, "cmd.exe");

    public static string Wsl { get; } = Path.Combine(Environment.SystemDirectory, "wsl.exe");
}
