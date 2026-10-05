using ClaudeUsageTracker.Core.Api;
using ClaudeUsageTracker.Core.ClaudeCode;
using ClaudeUsageTracker.Core.Models;

namespace ClaudeUsageTracker.Core.Usage;

/// <summary>
/// What <see cref="UsageFetcher"/> remembers between calls to the free usage endpoint. Kept
/// by the app in a file rather than in memory, so restarting it doesn't forget to space its
/// calls out, which is what trips the endpoint's rate limit.
/// </summary>
public interface IUsageFetchState
{
    /// <summary>When the usage endpoint was last actually called (whatever the outcome).</summary>
    DateTimeOffset? LastUsageEndpointCall { get; set; }

    /// <summary>Set while the last call was answered with 429: calls then wait the longer retry interval.</summary>
    bool UsageEndpointRateLimited { get; set; }

    /// <summary>Persists the two values above after a change.</summary>
    void Save();
}

/// <summary>
/// Fetches usage end to end: locate Claude Code CLI's own credentials (Windows-native
/// or WSL, whichever has them), then read usage the way the current mode says:
/// <list type="bullet">
///   <item>Token-using: a one-token Messages API prompt (consumes a tiny amount of usage; not
///   rate-limited like the usage endpoint, so it can refresh often).</item>
///   <item>Token-free: the free usage endpoint only (no usage consumed; rate-limited, hence
///   the fixed 5-minute interval and the back-off after a 429).</item>
/// </list>
/// An expired or rejected sign-in is reported (with a sign-in option), never refreshed by
/// the app. Credential discovery may shell out to wsl.exe, so it runs off the calling thread.
/// </summary>
public sealed class UsageFetcher(
    ClaudeCodeUsageClient usageClient,
    IClaudeCodeEnvironment environment,
    IUsageFetchState state,
    TimeProvider? timeProvider = null)
{
    // The timer ticks a little before the time the call itself is stamped (credential lookup runs
    // first), so a wait of exactly one interval would push the retry a whole tick later.
    private static readonly TimeSpan TimerSlack = TimeSpan.FromSeconds(30);

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    /// <summary>The model token-using refreshes prompt (switches automatically if it's retired).</summary>
    public string ProbeModel => usageClient.ProbeModel;

    /// <param name="avoidTokenUsage">Token-free mode: only the free usage endpoint is asked.</param>
    public async Task<UsageFetchResult> FetchAsync(bool avoidTokenUsage, CancellationToken ct = default)
    {
        if (avoidTokenUsage && state.LastUsageEndpointCall is { } lastCall)
        {
            // Rate-limited: skip regular refreshes until 10 minutes after the 429, then retry.
            var spacing = state.UsageEndpointRateLimited
                ? TimeSpan.FromSeconds(UsagePolling.RateLimitedRetrySeconds) - TimerSlack
                : UsagePolling.MinUsageEndpointSpacing;
            var wait = lastCall + spacing - _time.GetUtcNow();
            // A longer wait means the saved time is in the future (the clock was set back): ignore it.
            if (wait > TimeSpan.Zero && wait <= spacing)
                return UsageFetchResult.Throttled(wait, state.UsageEndpointRateLimited);
        }

        var lookup = await Task.Run(environment.FindCredentials, ct);
        if (lookup.Credentials is not { } credentials)
            return await Task.Run(() => DescribeMissingCredentials(lookup), ct);

        try
        {
            if (avoidTokenUsage)
            {
                state.LastUsageEndpointCall = _time.GetUtcNow();
                state.Save();
            }
            var usage = avoidTokenUsage
                ? await usageClient.GetUsageAsync(credentials, ct)
                : await usageClient.GetUsageViaMessagesApiAsync(credentials, ct);
            if (avoidTokenUsage)
                SetUsageEndpointLimited(false);
            return UsageFetchResult.Success(usage);
        }
        catch (AuthRequiredException)
        {
            return UsageFetchResult.SignInFailure(UsageFetchError.SignInRejected, lookup.Location);
        }
        catch (ClaudeApiException ex) when (avoidTokenUsage && ex.StatusCode == 429)
        {
            SetUsageEndpointLimited(true);
            return UsageFetchResult.RateLimited();
        }
        catch (ClaudeApiException ex)
        {
            return avoidTokenUsage
                ? UsageFetchResult.Failure(UsageFetchError.ApiStatus, statusCode: ex.StatusCode)
                : UsageFetchResult.Failure(UsageFetchError.MessagesApiFailed);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return UsageFetchResult.Failure(UsageFetchError.Network);
        }
    }

    private void SetUsageEndpointLimited(bool limited)
    {
        if (state.UsageEndpointRateLimited == limited)
            return;
        state.UsageEndpointRateLimited = limited;
        state.Save();
    }

    private UsageFetchResult DescribeMissingCredentials(ClaudeCredentialLookup lookup)
    {
        if (lookup.CredentialsFileFound)
            return UsageFetchResult.SignInFailure(UsageFetchError.SignInExpired, lookup.Location);

        return environment.IsCliInstalled
            ? UsageFetchResult.Failure(UsageFetchError.NotSignedIn, UsageStatusLevel.Safe, canSignIn: true)
            : UsageFetchResult.Failure(UsageFetchError.NotInstalled, UsageStatusLevel.Safe);
    }
}
