using ClaudeUsageTracker.Core.Models;

namespace ClaudeUsageTracker.Platform.ClaudeCode;

/// <summary>
/// Finds Claude Code CLI credentials wherever they currently live — Windows-native
/// first, then any installed WSL distro — mirroring Claude Code CLI's own "just works
/// from Windows or WSL" behavior. Read-only: an expired token is reported, not
/// refreshed (refreshing via the CLI would run a prompt and consume usage). Claude
/// Code refreshes it on its own the next time it's used.
/// </summary>
public static class ClaudeCredentialResolver
{
    /// <param name="Credentials">Usable (unexpired) credentials, or null.</param>
    /// <param name="Source">Where <paramref name="Credentials"/> came from — or, if they're
    /// null but a credentials file exists, where the expired one was found.</param>
    public readonly record struct Result(ClaudeCodeCredentials? Credentials, ClaudeCredentialSource? Source)
    {
        public bool CredentialsFileFound => Source is not null;
    }

    public static Result Resolve()
    {
        ClaudeCredentialSource? firstExpired = null;
        foreach (var source in ClaudeCredentialSource.EnumerateAll())
        {
            if (source.TryRead() is not { } credentials)
                continue;
            if (!credentials.IsExpired(DateTimeOffset.Now))
                return new Result(credentials, source);
            firstExpired ??= source;
        }

        return new Result(null, firstExpired);
    }
}
