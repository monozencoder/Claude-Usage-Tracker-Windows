using ClaudeUsageTracker.Core.ClaudeCode;

namespace ClaudeUsageTracker.Platform.ClaudeCode;

/// <summary>
/// Finds Claude Code CLI credentials wherever they currently live — Windows-native
/// first, then any installed WSL distro — mirroring Claude Code CLI's own "just works
/// from Windows or WSL" behavior. Read-only: an expired token is reported, not
/// refreshed (refreshing via the CLI would run a prompt and consume usage). Claude
/// Code refreshes it on its own the next time it's used.
/// </summary>
internal static class ClaudeCredentialResolver
{
    public static ClaudeCredentialLookup Resolve()
    {
        ClaudeCredentialSource? firstExpired = null;
        foreach (var source in ClaudeCredentialSource.EnumerateAll())
        {
            if (source.TryRead() is not { } credentials)
                continue;
            if (!credentials.IsExpired(DateTimeOffset.Now))
                return new ClaudeCredentialLookup(credentials, source.Location);
            firstExpired ??= source;
        }

        return new ClaudeCredentialLookup(null, firstExpired?.Location);
    }
}
