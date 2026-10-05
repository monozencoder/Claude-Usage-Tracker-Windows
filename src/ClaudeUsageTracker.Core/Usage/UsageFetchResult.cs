using ClaudeUsageTracker.Core.ClaudeCode;
using ClaudeUsageTracker.Core.Models;

namespace ClaudeUsageTracker.Core.Usage;

/// <summary>Why a fetch produced no usage. The app turns it into text in the user's language.</summary>
public enum UsageFetchError
{
    /// <summary>The API rejected the access token (401/403).</summary>
    SignInRejected,

    /// <summary>A credentials file exists, but its token has expired.</summary>
    SignInExpired,

    /// <summary>Claude Code is installed, but there are no credentials.</summary>
    NotSignedIn,

    /// <summary>Claude Code isn't installed (and there are no credentials).</summary>
    NotInstalled,

    /// <summary>The usage endpoint answered with <see cref="UsageFetchResult.ErrorStatusCode"/>.</summary>
    ApiStatus,

    /// <summary>The Messages API probe failed.</summary>
    MessagesApiFailed,

    /// <summary>The request couldn't be sent, or timed out.</summary>
    Network
}

/// <summary>Outcome of one <see cref="UsageFetcher.FetchAsync"/> call: either usage, or why there is none.</summary>
public sealed record UsageFetchResult
{
    public ClaudeUsage? Usage { get; private init; }

    public UsageFetchError? Error { get; private init; }

    /// <summary>The HTTP status behind <see cref="UsageFetchError.ApiStatus"/>.</summary>
    public int? ErrorStatusCode { get; private init; }

    /// <summary>Tray status to show for this error, or null to leave the icon as it was (transient failures).</summary>
    public UsageStatusLevel? ErrorStatus { get; private init; }

    /// <summary>True when signing in via the Windows Claude Code CLI would fix the error.</summary>
    public bool CanSignIn { get; private init; }

    /// <summary>
    /// Set when the sign-in problem is in a WSL distro ("WSL (Ubuntu)"): it has to be fixed by
    /// signing in from inside that distro, which the app can't do.
    /// </summary>
    public string? WslLocationName { get; private init; }

    /// <summary>The free usage endpoint (token-free mode) answered HTTP 429; the caller should back off.</summary>
    public bool UsageEndpointRateLimited { get; private init; }

    /// <summary>
    /// Token-free mode only: the usage endpoint was asked too recently, so it wasn't asked again.
    /// How long until it may be. The caller keeps showing what it has.
    /// </summary>
    public TimeSpan? RetryAfter { get; private init; }

    public static UsageFetchResult Success(ClaudeUsage usage) => new() { Usage = usage };

    public static UsageFetchResult Failure(UsageFetchError error, UsageStatusLevel? status = null, bool canSignIn = false, int? statusCode = null)
        => new() { Error = error, ErrorStatus = status, CanSignIn = canSignIn, ErrorStatusCode = statusCode };

    /// <summary>A missing, expired or rejected sign-in. A WSL-only setup has to sign in from inside WSL; everything else gets the Sign in button.</summary>
    public static UsageFetchResult SignInFailure(UsageFetchError error, ClaudeCredentialLocation? location)
        => location is { IsWsl: true }
            ? new() { Error = error, ErrorStatus = UsageStatusLevel.Critical, WslLocationName = location.DisplayName }
            : new() { Error = error, ErrorStatus = UsageStatusLevel.Critical, CanSignIn = true };

    public static UsageFetchResult RateLimited() => new() { UsageEndpointRateLimited = true };

    /// <param name="rateLimited">Held back because the endpoint answered 429 last time, not just because it was asked moments ago.</param>
    public static UsageFetchResult Throttled(TimeSpan retryAfter, bool rateLimited)
        => new() { RetryAfter = retryAfter, UsageEndpointRateLimited = rateLimited };
}
