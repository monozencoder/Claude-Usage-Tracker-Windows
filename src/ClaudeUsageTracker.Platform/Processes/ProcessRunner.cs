using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace ClaudeUsageTracker.Platform.Processes;

/// <summary>
/// Runs a short-lived, windowless child process with a hard timeout. Stdin is closed
/// immediately (so CLIs that read piped input don't wait on it) and stdout/stderr are
/// drained concurrently, so a chatty process can't deadlock on a full pipe buffer.
/// </summary>
internal static class ProcessRunner
{
    // After the process exits, how long to wait for its output pipes to close. A
    // grandchild that inherited the pipe handles can keep them open indefinitely.
    private static readonly TimeSpan OutputDrainTimeout = TimeSpan.FromSeconds(2);

    public readonly record struct Result(int ExitCode, string StandardOutput);

    /// <returns>The exit code and stdout, or null if the process couldn't be started or timed out (it is then killed).</returns>
    public static Result? Run(
        string fileName,
        IEnumerable<string> arguments,
        TimeSpan timeout,
        Encoding? standardOutputEncoding = null,
        Action<ProcessStartInfo>? configure = null)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (standardOutputEncoding is not null)
            startInfo.StandardOutputEncoding = standardOutputEncoding;
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        configure?.Invoke(startInfo);

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
                return null;

            process.StandardInput.Close();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(timeout))
            {
                TryKill(process);
                return null;
            }

            try
            {
                Task.WaitAll([stdout, stderr], OutputDrainTimeout);
            }
            catch (AggregateException)
            {
                // A pipe read failed; whatever stdout managed to capture is still usable below.
            }

            return new Result(process.ExitCode, stdout.IsCompletedSuccessfully ? stdout.Result : string.Empty);
        }
        catch (Win32Exception)
        {
            // Executable not found / not invocable.
            return null;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Exited between the timeout and the kill.
        }
    }
}
