namespace ClaudeUsageTracker.Core.Api;

/// <summary>No Claude Code CLI credentials file was found (or it has no usable access token).</summary>
public sealed class NoCredentialsException() : Exception("No Claude Code CLI credentials were found.");

/// <summary>
/// The API rejected the access token (401/403). The caller should ask Claude Code
/// CLI to refresh its token (see ClaudeCliRefresher) and retry once.
/// </summary>
public sealed class AuthRequiredException() : Exception("Anthropic's API rejected the current access token.");

/// <summary>Any other non-2xx response, or a request that couldn't be classified as success/auth-failure.</summary>
public sealed class ClaudeApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
