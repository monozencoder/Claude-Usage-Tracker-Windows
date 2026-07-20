namespace ClaudeUsageTracker.Core.Models;

/// <summary>
/// OAuth credentials as written by the Claude Code CLI to its own credentials
/// file. We only ever read this file — Claude Code CLI owns writing/rotating it.
/// </summary>
public sealed record ClaudeCodeCredentials(string AccessToken, long? ExpiresAtUnixMs)
{
    public bool IsExpired(DateTimeOffset now)
        => ExpiresAtUnixMs is { } expiresAt &&
           DateTimeOffset.FromUnixTimeMilliseconds(expiresAt) <= now;
}
