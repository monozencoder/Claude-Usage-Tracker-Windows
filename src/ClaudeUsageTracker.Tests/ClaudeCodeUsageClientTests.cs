using System.Net;
using ClaudeUsageTracker.Core.Api;
using ClaudeUsageTracker.Core.Models;
using Xunit;

namespace ClaudeUsageTracker.Tests;

public class ClaudeCodeUsageClientTests
{
    private static readonly ClaudeCodeCredentials Credentials = new("token-abc", null);

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(respond(request));
        }
    }

    private static bool IsOAuthUsageRequest(HttpRequestMessage request)
        => request.RequestUri!.ToString() == ApiEndpoints.OAuthUsage;

    [Fact]
    public async Task GetUsageAsync_ReturnsUsageFromOAuthEndpoint_WhenBothResetsPresent()
    {
        var handler = new StubHandler(_ =>
        {
            const string json = """
            { "five_hour": { "utilization": 30, "resets_at": "2026-07-20T18:00:00Z" },
              "seven_day": { "utilization": 5, "resets_at": "2026-07-25T00:00:00Z" } }
            """;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
        });
        var client = new ClaudeCodeUsageClient(new HttpClient(handler));

        var usage = await client.GetUsageAsync(Credentials);

        Assert.Equal(30, usage.SessionPercentage);
        Assert.Equal(5, usage.WeeklyPercentage);
        Assert.Equal(1, handler.RequestCount); // no Messages API fallback needed
    }

    [Fact]
    public async Task GetUsageAsync_ThrowsAuthRequired_WhenOAuthEndpointReturns401()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var client = new ClaudeCodeUsageClient(new HttpClient(handler));

        await Assert.ThrowsAsync<AuthRequiredException>(() => client.GetUsageAsync(Credentials));
    }

    [Fact]
    public async Task GetUsageAsync_FallsBackToMessagesApi_WhenOAuthEndpointUnavailable()
    {
        var handler = new StubHandler(req =>
        {
            if (IsOAuthUsageRequest(req))
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.TryAddWithoutValidation("anthropic-ratelimit-unified-5h-utilization", "0.2");
            response.Headers.TryAddWithoutValidation("anthropic-ratelimit-unified-7d-utilization", "0.05");
            return response;
        });
        var client = new ClaudeCodeUsageClient(new HttpClient(handler));

        var usage = await client.GetUsageAsync(Credentials);

        Assert.Equal(20, usage.SessionPercentage, precision: 3);
        Assert.Equal(5, usage.WeeklyPercentage, precision: 3);
    }

    [Fact]
    public async Task GetUsageAsync_ThrowsAuthRequired_WhenMessagesFallbackAlsoReturns401()
    {
        var handler = new StubHandler(req => IsOAuthUsageRequest(req)
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var client = new ClaudeCodeUsageClient(new HttpClient(handler));

        await Assert.ThrowsAsync<AuthRequiredException>(() => client.GetUsageAsync(Credentials));
    }

    [Fact]
    public async Task GetUsageAsync_TriesNextModel_WhenFirstModelResponseHasNoRateLimitHeaders()
    {
        var messagesCallCount = 0;
        var handler = new StubHandler(req =>
        {
            if (IsOAuthUsageRequest(req))
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            messagesCallCount++;
            if (messagesCallCount == 1)
                return new HttpResponseMessage(HttpStatusCode.OK); // no rate-limit headers -> should try next model

            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.TryAddWithoutValidation("anthropic-ratelimit-unified-5h-utilization", "0.5");
            return response;
        });
        var client = new ClaudeCodeUsageClient(new HttpClient(handler));

        var usage = await client.GetUsageAsync(Credentials);

        Assert.Equal(50, usage.SessionPercentage, precision: 3);
        Assert.Equal(2, messagesCallCount);
    }

    [Fact]
    public async Task GetUsageAsync_ThrowsClaudeApiException_WhenNoRateLimitHeadersFromAnyModel()
    {
        var handler = new StubHandler(req => IsOAuthUsageRequest(req)
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : new HttpResponseMessage(HttpStatusCode.OK)); // never carries rate-limit headers
        var client = new ClaudeCodeUsageClient(new HttpClient(handler));

        await Assert.ThrowsAsync<ClaudeApiException>(() => client.GetUsageAsync(Credentials));
    }

    [Fact]
    public async Task GetUsageAsync_FillsMissingResetTimes_FromMessagesApiFallback_WithoutOverwritingPercentages()
    {
        var handler = new StubHandler(req =>
        {
            if (IsOAuthUsageRequest(req))
            {
                const string json = """{ "five_hour": { "utilization": 30 }, "seven_day": { "utilization": 5 } }""";
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.TryAddWithoutValidation("anthropic-ratelimit-unified-5h-reset", "1795276800");
            response.Headers.TryAddWithoutValidation("anthropic-ratelimit-unified-7d-reset", "1795700000");
            response.Headers.TryAddWithoutValidation("anthropic-ratelimit-unified-5h-utilization", "0.99");
            return response;
        });
        var client = new ClaudeCodeUsageClient(new HttpClient(handler));

        var usage = await client.GetUsageAsync(Credentials);

        Assert.Equal(30, usage.SessionPercentage); // from the OAuth endpoint, not overwritten by the fallback's utilization
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1795276800), usage.SessionResetTime);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1795700000), usage.WeeklyResetTime);
    }
}
