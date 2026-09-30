using System.Net.Http;
using ClaudeUsageTracker.App.Localization;
using ClaudeUsageTracker.App.Settings;
using ClaudeUsageTracker.Core.Api;
using ClaudeUsageTracker.Core.Models;
using ClaudeUsageTracker.Platform.ClaudeCode;

namespace ClaudeUsageTracker.App.Services;

/// <summary>Outcome of one <see cref="UsageFetcher.FetchAsync"/> call: either usage, or a user-facing error.</summary>
public sealed record UsageFetchResult
{
    public ClaudeUsage? Usage { get; private init; }
    public string? Error { get; private init; }

    /// <summary>Tray status to show for this error, or null to leave the icon as it was (transient failures).</summary>
    public UsageStatusLevel? ErrorStatus { get; private init; }

    /// <summary>True when signing in via the Windows Claude Code CLI would fix the error.</summary>
    public bool CanSignIn { get; private init; }

    /// <summary>The free usage endpoint (token-free mode) answered HTTP 429; the caller should back off.</summary>
    public bool UsageEndpointRateLimited { get; private init; }

    public static UsageFetchResult Success(ClaudeUsage usage) => new() { Usage = usage };

    public static UsageFetchResult Failure(string error, UsageStatusLevel? status = null, bool canSignIn = false)
        => new() { Error = error, ErrorStatus = status, CanSignIn = canSignIn };

    /// <summary>
    /// Token-free mode only: the usage endpoint was asked too recently, so it wasn't asked again.
    /// How long until it may be. The caller keeps showing what it has.
    /// </summary>
    public TimeSpan? RetryAfter { get; private init; }

    public static UsageFetchResult RateLimited() => new() { UsageEndpointRateLimited = true };

    /// <param name="rateLimited">Held back because the endpoint answered 429 last time, not just because it was asked moments ago.</param>
    public static UsageFetchResult Throttled(TimeSpan retryAfter, bool rateLimited)
        => new() { RetryAfter = retryAfter, UsageEndpointRateLimited = rateLimited };
}

/// <summary>
/// Fetches usage end to end: locate Claude Code CLI's own credentials (Windows-native
/// or WSL, whichever has them), then read usage the way the current mode says:
/// <list type="bullet">
///   <item>Default: a one-token Messages API prompt (consumes a tiny amount of usage; not
///   rate-limited like the usage endpoint, so it can refresh often).</item>
///   <item><see cref="AppSettings.AvoidTokenUsage"/>: the free usage endpoint only (no usage
///   consumed; rate-limited, hence the fixed 5-minute interval and the back-off after a 429).</item>
/// </list>
/// An expired or rejected sign-in is reported (with a sign-in option), never refreshed by
/// the app. Credential discovery shells out to wsl.exe / where.exe, so it runs off the UI thread.
/// </summary>
public sealed class UsageFetcher(ClaudeCodeUsageClient usageClient, AppSettingsStore settingsStore)
{
    /// <summary>
    /// Shortest gap between two calls to the free usage endpoint. Refresh buttons, the tray menu,
    /// sign-in changes and Test connection all bypass the 5-minute timer; without this, a few
    /// clicks could trip its rate limit (HTTP 429), which can take an hour or more to lift.
    /// </summary>
    public static readonly TimeSpan MinUsageEndpointSpacing = TimeSpan.FromMinutes(2);

    // When the usage endpoint was last actually called (whatever the outcome).
    private DateTimeOffset? _lastUsageEndpointCall;

    // Set while the last call was answered with 429: calls then wait the longer retry interval.
    private bool _usageEndpointLimited;

    // The timer ticks a little before the time the call itself is stamped (credential lookup runs
    // first), so a wait of exactly one interval would push the retry a whole tick later.
    private static readonly TimeSpan TimerSlack = TimeSpan.FromSeconds(30);

    /// <summary>The model token-using refreshes prompt (switches automatically if it's retired).</summary>
    public string ProbeModel => usageClient.ProbeModel;

    /// <param name="avoidTokenUsage">Overrides the saved mode (Test connection checks the unsaved choice).</param>
    public async Task<UsageFetchResult> FetchAsync(bool? avoidTokenUsage = null, CancellationToken ct = default)
    {
        var avoidTokens = avoidTokenUsage ?? settingsStore.Current.AvoidTokenUsage;
        if (avoidTokens && _lastUsageEndpointCall is { } lastCall)
        {
            // Rate-limited: skip regular refreshes until 10 minutes after the 429, then retry.
            var spacing = _usageEndpointLimited
                ? TimeSpan.FromSeconds(AppSettings.TokenFreeRateLimitedRetrySeconds) - TimerSlack
                : MinUsageEndpointSpacing;
            var wait = lastCall + spacing - DateTimeOffset.Now;
            if (wait > TimeSpan.Zero)
                return UsageFetchResult.Throttled(wait, _usageEndpointLimited);
        }

        var resolution = await Task.Run(ClaudeCredentialResolver.Resolve, ct);
        if (resolution.Credentials is not { } credentials)
            return await Task.Run(() => DescribeMissingCredentials(resolution), ct);

        try
        {
            if (avoidTokens)
                _lastUsageEndpointCall = DateTimeOffset.Now;
            var usage = avoidTokens
                ? await usageClient.GetUsageAsync(credentials, ct)
                : await usageClient.GetUsageViaMessagesApiAsync(credentials, ct);
            if (avoidTokens)
                _usageEndpointLimited = false;
            return UsageFetchResult.Success(usage);
        }
        catch (AuthRequiredException)
        {
            return SignInFailure(Loc.Get("Error_SignInRejected"), resolution.Source);
        }
        catch (ClaudeApiException ex) when (avoidTokens && ex.StatusCode == 429)
        {
            _usageEndpointLimited = true;
            return UsageFetchResult.RateLimited();
        }
        catch (ClaudeApiException ex)
        {
            return UsageFetchResult.Failure(avoidTokens
                ? Loc.Format("Error_Status", ex.StatusCode)
                : Loc.Get("Error_MessagesApiFailed"));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return UsageFetchResult.Failure(Loc.Get("Error_Network"));
        }
    }

    private static UsageFetchResult DescribeMissingCredentials(ClaudeCredentialResolver.Result resolution)
    {
        if (resolution.CredentialsFileFound)
        {
            return SignInFailure(Loc.Get("Error_SignInExpired"), resolution.Source);
        }

        return ClaudeCli.IsInstalled
            ? UsageFetchResult.Failure(Loc.Get("Error_NotSignedIn"), UsageStatusLevel.Safe, canSignIn: true)
            : UsageFetchResult.Failure(Loc.Get("Error_NotInstalled"), UsageStatusLevel.Safe);
    }

    // A WSL-only setup has to sign in from inside WSL; everything else gets the Sign in button.
    private static UsageFetchResult SignInFailure(string problem, ClaudeCredentialSource? source)
        => source is { IsWsl: true }
            ? UsageFetchResult.Failure(Loc.Format("Error_SignInInWsl", problem, source.DisplayName), UsageStatusLevel.Critical)
            : UsageFetchResult.Failure(problem, UsageStatusLevel.Critical, canSignIn: true);
}
