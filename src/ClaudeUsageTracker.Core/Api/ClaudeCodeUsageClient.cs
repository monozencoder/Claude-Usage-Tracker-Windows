using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ClaudeUsageTracker.Core.Api.Dtos;
using ClaudeUsageTracker.Core.Models;

namespace ClaudeUsageTracker.Core.Api;

/// <summary>
/// Reads Claude Code usage from Anthropic's actual API (api.anthropic.com) using
/// the OAuth access token Claude Code CLI itself uses — the same approach the CLI
/// takes, not claude.ai's bot-protected web app. Tries the dedicated usage
/// endpoint first, falling back to reading rate-limit headers off a minimal
/// Messages API call (mirrors Claude Code CLI's own fallback behavior).
/// </summary>
public sealed class ClaudeCodeUsageClient(HttpClient httpClient)
{
    // Mirrors Claude Code CLI's own fallback chain — some accounts/models are
    // rejected outright, so multiple models are tried until one yields the headers.
    private static readonly string[] ModelFallbackChain = ["claude-3-haiku-20240307", "claude-haiku-4-5-20251001"];

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<ClaudeUsage> GetUsageAsync(ClaudeCodeCredentials credentials, CancellationToken ct = default)
    {
        var now = DateTimeOffset.Now;
        var fromOAuthEndpoint = await TryGetUsageFromOAuthEndpointAsync(credentials, now, ct);
        if (fromOAuthEndpoint is null)
            return await GetUsageFromMessagesApiAsync(credentials, now, ct);

        if (fromOAuthEndpoint.SessionResetTime is not null && fromOAuthEndpoint.WeeklyResetTime is not null)
            return fromOAuthEndpoint;

        // Reset timers are sometimes missing from the dedicated endpoint — fill
        // them in from the Messages API fallback rather than discarding what we have.
        try
        {
            var fallback = await GetUsageFromMessagesApiAsync(credentials, now, ct);
            return fromOAuthEndpoint with
            {
                SessionResetTime = fromOAuthEndpoint.SessionResetTime ?? fallback.SessionResetTime,
                WeeklyResetTime = fromOAuthEndpoint.WeeklyResetTime ?? fallback.WeeklyResetTime
            };
        }
        catch (ClaudeApiException)
        {
            return fromOAuthEndpoint;
        }
    }

    private async Task<ClaudeUsage?> TryGetUsageFromOAuthEndpointAsync(ClaudeCodeCredentials credentials, DateTimeOffset now, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, ApiEndpoints.OAuthUsage);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
        request.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, ct);
        }
        catch (HttpRequestException)
        {
            return null; // treat as "endpoint unavailable", caller falls back to Messages API
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new AuthRequiredException();

            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadAsStringAsync(ct);
            OAuthUsageResponseDto? dto;
            try
            {
                dto = JsonSerializer.Deserialize<OAuthUsageResponseDto>(body, JsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }

            return dto is null ? null : UsageResponseParser.FromOAuthUsage(dto, now);
        }
    }

    private async Task<ClaudeUsage> GetUsageFromMessagesApiAsync(ClaudeCodeCredentials credentials, DateTimeOffset now, CancellationToken ct)
    {
        foreach (var model in ModelFallbackChain)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, ApiEndpoints.Messages)
            {
                Content = new StringContent(BuildProbeMessageBody(model), Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
            request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
            request.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");

            HttpResponseMessage response;
            try
            {
                response = await httpClient.SendAsync(request, ct);
            }
            catch (HttpRequestException)
            {
                continue; // try the next model
            }

            using (response)
            {
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    throw new AuthRequiredException();

                if (HasRateLimitHeaders(response))
                    return UsageResponseParser.FromRateLimitHeaders(response.Headers, now);
            }
        }

        throw new ClaudeApiException(0, "Could not read usage from Anthropic's rate-limit headers.");
    }

    private static string BuildProbeMessageBody(string model)
        => JsonSerializer.Serialize(new
        {
            model,
            max_tokens = 1,
            messages = new[] { new { role = "user", content = "." } }
        });

    private static bool HasRateLimitHeaders(HttpResponseMessage response)
        => response.Headers.Contains("anthropic-ratelimit-unified-5h-utilization")
        || response.Headers.Contains("anthropic-ratelimit-unified-7d-utilization")
        || response.Headers.Contains("anthropic-ratelimit-unified-status");
}
