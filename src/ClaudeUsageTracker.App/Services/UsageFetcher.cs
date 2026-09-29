using System.Net.Http;
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

    public static UsageFetchResult Success(ClaudeUsage usage) => new() { Usage = usage };

    public static UsageFetchResult Failure(string error, UsageStatusLevel? status = null)
        => new() { Error = error, ErrorStatus = status };
}

/// <summary>
/// Fetches usage end to end: locate Claude Code CLI's own credentials (Windows-native
/// or WSL, whichever has them), call Anthropic's API, and — if the token is rejected —
/// nudge that CLI to refresh it and retry once. Credential discovery and refresh shell
/// out to claude / wsl.exe and can take many seconds, so they run off the UI thread.
/// </summary>
public sealed class UsageFetcher(ClaudeCodeUsageClient usageClient)
{
    private const string SignInHint = "Run \"claude\" in a terminal to sign in again.";

    public async Task<UsageFetchResult> FetchAsync(CancellationToken ct = default)
    {
        var resolution = await Task.Run(ClaudeCredentialResolver.Resolve, ct);
        if (resolution.Credentials is not { } credentials)
        {
            return resolution.CredentialsFileFound
                ? UsageFetchResult.Failure($"Claude Code CLI's token is expired. {SignInHint}", UsageStatusLevel.Critical)
                : UsageFetchResult.Failure(
                    "Claude Code CLI credentials not found on Windows or in any installed WSL distro. " +
                    "Install Claude Code and run \"claude\" to sign in.",
                    UsageStatusLevel.Safe);
        }

        try
        {
            try
            {
                return UsageFetchResult.Success(await usageClient.GetUsageAsync(credentials, ct));
            }
            catch (AuthRequiredException)
            {
                var refreshed = await Task.Run(() => resolution.Source?.TryRefreshAndReread(), ct);
                if (refreshed is null)
                {
                    return UsageFetchResult.Failure(
                        $"Claude Code CLI's token was rejected and could not be refreshed. {SignInHint}",
                        UsageStatusLevel.Critical);
                }

                return UsageFetchResult.Success(await usageClient.GetUsageAsync(refreshed, ct));
            }
        }
        catch (AuthRequiredException)
        {
            return UsageFetchResult.Failure($"Anthropic rejected Claude Code CLI's token. {SignInHint}", UsageStatusLevel.Critical);
        }
        catch (ClaudeApiException ex)
        {
            return UsageFetchResult.Failure($"Failed to read usage (status {ex.StatusCode}).");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return UsageFetchResult.Failure("Couldn't reach Anthropic's API. Check your network connection.");
        }
    }
}
