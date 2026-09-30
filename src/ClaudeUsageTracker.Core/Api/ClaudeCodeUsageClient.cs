using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ClaudeUsageTracker.Core.Api.Dtos;
using ClaudeUsageTracker.Core.Models;

namespace ClaudeUsageTracker.Core.Api;

/// <summary>
/// Reads Claude Code usage from Anthropic's API (api.anthropic.com) using the access
/// token Claude Code CLI itself stores — not claude.ai's bot-protected web app.
/// <see cref="GetUsageAsync"/> consumes no usage but is rate-limited;
/// <see cref="GetUsageViaMessagesApiAsync"/> consumes a tiny amount of usage per call but
/// can be polled often. The app decides which one to use.
/// </summary>
public sealed class ClaudeCodeUsageClient(HttpClient httpClient)
{
    /// <summary>
    /// The cheapest current model (the probe only needs the response headers). An alias, so it
    /// follows new Haiku 4.5 snapshots; once retired, <see cref="ProbeModelSelector"/> picks a successor.
    /// </summary>
    public const string PreferredProbeModel = "claude-haiku-4-5";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    // Replaced (for this client's lifetime) when the current probe model turns out to be retired.
    // Deliberately not persisted: each launch starts from the preferred (cheapest) model again, so
    // a newer Haiku is picked up instead of staying on a pricier fallback. That costs at most one
    // 404 per launch, which runs no prompt.
    private string _probeModel = PreferredProbeModel;

    // How to ask for no thinking, tried in order until the model accepts the probe. Haiku 4.5
    // takes the plain request (no thinking unless asked). Newer models think by default and may
    // reject a 1-token prompt; they accept one of the others: "between_tools" (Claude Sonnet 5.5)
    // or "disabled" (Claude Sonnet 5, Claude Opus 5). A rejected request (400) runs no prompt.
    private static readonly object?[] ThinkingOptions = [null, new { type = "between_tools" }, new { type = "disabled" }];

    // Index into ThinkingOptions that the current probe model last accepted.
    private int _thinkingOption;

    /// <summary>The model the next <see cref="GetUsageViaMessagesApiAsync"/> call will prompt.</summary>
    public string ProbeModel => _probeModel;

    /// <summary>
    /// Reads usage from the OAuth usage endpoint. The endpoint only reports usage and never
    /// runs a prompt, so this doesn't consume any of the quota it reports.
    /// </summary>
    /// <exception cref="AuthRequiredException">The token was rejected (401/403).</exception>
    /// <exception cref="ClaudeApiException">Any other non-success status (e.g. 429), or an unreadable response.</exception>
    /// <exception cref="HttpRequestException">The request couldn't be sent (network failure).</exception>
    public async Task<ClaudeUsage> GetUsageAsync(ClaudeCodeCredentials credentials, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiEndpoints.OAuthUsage);
        AddAuthHeaders(request, credentials);

        using var response = await httpClient.SendAsync(request, ct);
        var status = (int)response.StatusCode;

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new AuthRequiredException();

        if (!response.IsSuccessStatusCode)
            throw new ClaudeApiException(status, $"The usage endpoint returned status {status}.");

        var body = await response.Content.ReadAsStringAsync(ct);
        try
        {
            var dto = JsonSerializer.Deserialize<OAuthUsageResponseDto>(body, JsonOptions)
                      ?? throw new ClaudeApiException(status, "The usage endpoint returned an empty response.");
            return UsageResponseParser.FromOAuthUsage(dto, DateTimeOffset.Now);
        }
        catch (JsonException)
        {
            throw new ClaudeApiException(status, "The usage endpoint returned an unreadable response.");
        }
    }

    /// <summary>
    /// <b>Consumes usage</b> (measured: 8 input + 1 output tokens on Haiku 4.5). Sends a real
    /// one-token prompt to the Messages API and reads the anthropic-ratelimit-unified-* headers
    /// off the response (mirrors Claude Code CLI's own fallback). If the probe model has been
    /// retired (404), a successor is looked up via the Models API (free) and used from then on.
    /// </summary>
    /// <exception cref="AuthRequiredException">The token was rejected (401/403).</exception>
    /// <exception cref="ClaudeApiException">No usable model, or the response carried no rate-limit headers.</exception>
    /// <exception cref="HttpRequestException">The request couldn't be sent (network failure).</exception>
    public async Task<ClaudeUsage> GetUsageViaMessagesApiAsync(ClaudeCodeCredentials credentials, CancellationToken ct = default)
    {
        var usage = await TryProbeAsync(credentials, _probeModel, ct);
        if (usage is not null)
            return usage;

        // The model is gone: ask which ones exist and switch for good.
        var successor = await FindProbeModelAsync(credentials, ct);
        if (successor is null || successor == _probeModel)
            throw new ClaudeApiException(404, $"Model {_probeModel} is unavailable and no replacement was found.");

        _probeModel = successor;
        _thinkingOption = 0;
        return await TryProbeAsync(credentials, successor, ct)
               ?? throw new ClaudeApiException(404, $"Replacement model {successor} is unavailable too.");
    }

    /// <returns>The usage, or null if <paramref name="model"/> doesn't exist (404).</returns>
    private async Task<ClaudeUsage?> TryProbeAsync(ClaudeCodeCredentials credentials, string model, CancellationToken ct)
    {
        for (var option = _thinkingOption; ; option++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, ApiEndpoints.Messages)
            {
                Content = new StringContent(BuildProbeMessageBody(model, ThinkingOptions[option]), Encoding.UTF8, "application/json")
            };
            AddAuthHeaders(request, credentials);
            request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");

            using var response = await httpClient.SendAsync(request, ct);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new AuthRequiredException();
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;
            // Rejected as sent (e.g. this model won't take a 1-token prompt with thinking on): try the next shape.
            if (response.StatusCode == HttpStatusCode.BadRequest && option + 1 < ThinkingOptions.Length)
                continue;
            if (!HasRateLimitHeaders(response))
                throw new ClaudeApiException((int)response.StatusCode, "Could not read usage from Anthropic's rate-limit headers.");

            _thinkingOption = option;
            return UsageResponseParser.FromRateLimitHeaders(response.Headers, DateTimeOffset.Now);
        }
    }

    /// <summary>Lists the models this token can use (no prompt, no usage) and picks the probe model.</summary>
    private async Task<string?> FindProbeModelAsync(ClaudeCodeCredentials credentials, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiEndpoints.Models);
        AddAuthHeaders(request, credentials);
        request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");

        using var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        try
        {
            var dto = JsonSerializer.Deserialize<ModelsListResponseDto>(await response.Content.ReadAsStringAsync(ct), JsonOptions);
            return dto is null ? null : ProbeModelSelector.Pick(dto.Data.Select(m => (m.Id, m.CreatedAt)));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void AddAuthHeaders(HttpRequestMessage request, ClaudeCodeCredentials credentials)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
        request.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");
    }

    private static string BuildProbeMessageBody(string model, object? thinking)
        => thinking is null
            ? JsonSerializer.Serialize(new
            {
                model,
                max_tokens = 1,
                messages = new[] { new { role = "user", content = "." } }
            })
            : JsonSerializer.Serialize(new
            {
                model,
                max_tokens = 1,
                thinking,
                messages = new[] { new { role = "user", content = "." } }
            });

    private static bool HasRateLimitHeaders(HttpResponseMessage response)
        => response.Headers.Contains("anthropic-ratelimit-unified-5h-utilization")
        || response.Headers.Contains("anthropic-ratelimit-unified-7d-utilization")
        || response.Headers.Contains("anthropic-ratelimit-unified-status");
}
