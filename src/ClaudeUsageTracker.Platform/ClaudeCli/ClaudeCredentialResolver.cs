using ClaudeUsageTracker.Core.Models;

namespace ClaudeUsageTracker.Platform.ClaudeCli;

/// <summary>
/// Finds Claude Code CLI credentials wherever they currently live — trying
/// Windows-native first, then any installed WSL distro — nudging that
/// environment's own CLI to refresh an expired token before falling through to
/// the next source. Mirrors Claude Code CLI's own "just works from Windows or
/// WSL" behavior instead of assuming Windows-native credentials only.
/// </summary>
public static class ClaudeCredentialResolver
{
    public readonly record struct Result(
        ClaudeCodeCredentials? Credentials,
        bool CredentialsFileFound,
        ClaudeCredentialSource? Source);

    public static Result Resolve()
    {
        var sources = ClaudeCredentialSource.EnumerateAll();

        var currentIndex = FindNextWithCredentials(sources, 0);
        if (currentIndex < 0)
            return new Result(null, CredentialsFileFound: false, Source: null);

        while (true)
        {
            var source = sources[currentIndex];

            var creds = source.TryRead();
            if (IsUsable(creds))
                return new Result(creds, CredentialsFileFound: true, source);

            creds = source.TryRefreshAndReread();
            if (IsUsable(creds))
                return new Result(creds, CredentialsFileFound: true, source);

            var nextIndex = FindNextWithCredentials(sources, currentIndex + 1);
            if (nextIndex < 0)
                return new Result(null, CredentialsFileFound: true, Source: null);

            currentIndex = nextIndex;
        }
    }

    private static bool IsUsable(ClaudeCodeCredentials? credentials)
        => credentials is not null && !credentials.IsExpired(DateTimeOffset.Now);

    private static int FindNextWithCredentials(IReadOnlyList<ClaudeCredentialSource> sources, int startIndex)
    {
        for (var i = startIndex; i < sources.Count; i++)
        {
            if (sources[i].TryRead() is not null)
                return i;
        }

        return -1;
    }
}
