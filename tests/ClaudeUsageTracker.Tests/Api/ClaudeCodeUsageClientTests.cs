using System.Net;
using ClaudeUsageTracker.Core.Api;
using ClaudeUsageTracker.Core.Models;

namespace ClaudeUsageTracker.Tests.Api;

public class ClaudeCodeUsageClientTests
{
    private static readonly ClaudeCodeCredentials Credentials = new("token-abc", null);

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        // Request bodies, captured here because the client disposes each request once it's sent.
        public List<string?> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
            return respond(request);
        }
    }

    private static ClaudeCodeUsageClient ClientReturning(HttpStatusCode status, string? json, out StubHandler handler)
    {
        handler = new StubHandler(_ => new HttpResponseMessage(status)
        {
            Content = json is null ? null : new StringContent(json)
        });
        return new ClaudeCodeUsageClient(new HttpClient(handler));
    }

    [Fact]
    public async Task GetUsageAsync_ReadsUsageFromOAuthEndpoint()
    {
        const string json = """
        { "five_hour": { "utilization": 30, "resets_at": "2026-07-20T18:00:00Z" },
          "seven_day": { "utilization": 5, "resets_at": "2026-07-25T00:00:00Z" } }
        """;
        var client = ClientReturning(HttpStatusCode.OK, json, out _);

        var usage = await client.GetUsageAsync(Credentials);

        Assert.Equal(30, usage.SessionPercentage);
        Assert.Equal(5, usage.WeeklyPercentage);
        Assert.Equal(new DateTimeOffset(2026, 7, 20, 18, 0, 0, TimeSpan.Zero), usage.SessionResetTime);
    }

    [Fact]
    public async Task GetUsageAsync_SendsOnlyASingleGetToTheUsageEndpoint()
    {
        // Guards the "never consumes quota" guarantee: no prompt/Messages API call, ever.
        var client = ClientReturning(HttpStatusCode.OK, "{}", out var handler);

        await client.GetUsageAsync(Credentials);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(ApiEndpoints.OAuthUsage, request.RequestUri!.ToString());
        Assert.Equal("token-abc", request.Headers.Authorization!.Parameter);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task GetUsageAsync_ThrowsAuthRequired_WhenTokenRejected(HttpStatusCode status)
    {
        var client = ClientReturning(status, null, out _);

        await Assert.ThrowsAsync<AuthRequiredException>(() => client.GetUsageAsync(Credentials));
    }

    [Fact]
    public async Task GetUsageAsync_ThrowsClaudeApiExceptionWithStatus_OnOtherErrors()
    {
        var client = ClientReturning(HttpStatusCode.ServiceUnavailable, null, out _);

        var ex = await Assert.ThrowsAsync<ClaudeApiException>(() => client.GetUsageAsync(Credentials));
        Assert.Equal(503, ex.StatusCode);
    }

    [Fact]
    public async Task GetUsageAsync_ThrowsClaudeApiException_OnMalformedJson()
    {
        var client = ClientReturning(HttpStatusCode.OK, "not json", out _);

        await Assert.ThrowsAsync<ClaudeApiException>(() => client.GetUsageAsync(Credentials));
    }

    private static HttpResponseMessage WithRateLimitHeaders(params (string Name, string Value)[] headers)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        foreach (var (name, value) in headers)
            response.Headers.TryAddWithoutValidation(name, value);
        return response;
    }

    [Fact]
    public async Task GetUsageViaMessagesApiAsync_ReadsRateLimitHeadersFromAOneTokenPrompt()
    {
        var handler = new StubHandler(_ => WithRateLimitHeaders(
            ("anthropic-ratelimit-unified-5h-utilization", "0.2"),
            ("anthropic-ratelimit-unified-7d-utilization", "0.05")));
        var client = new ClaudeCodeUsageClient(new HttpClient(handler));

        var usage = await client.GetUsageViaMessagesApiAsync(Credentials);

        Assert.Equal(20, usage.SessionPercentage, precision: 3);
        Assert.Equal(5, usage.WeeklyPercentage, precision: 3);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(ApiEndpoints.Messages, request.RequestUri!.ToString());
        Assert.Contains($"\"model\":\"{ClaudeCodeUsageClient.PreferredProbeModel}\"", Assert.Single(handler.Bodies));
    }

    private static StubHandler RetiredModelHandler(string retiredModel, string modelsJson, Action? onModelsListed = null)
        => new(request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                onModelsListed?.Invoke();
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(modelsJson) };
            }

            var body = request.Content!.ReadAsStringAsync().Result;
            return body.Contains($"\"model\":\"{retiredModel}\"")
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : WithRateLimitHeaders(("anthropic-ratelimit-unified-5h-utilization", "0.5"));
        });

    [Fact]
    public async Task GetUsageViaMessagesApiAsync_SwitchesToListedSuccessor_WhenProbeModelIsRetired()
    {
        const string models = """
        { "data": [
            { "id": "claude-opus-6", "created_at": "2027-03-01T00:00:00Z" },
            { "id": "claude-haiku-5", "created_at": "2027-01-01T00:00:00Z" } ] }
        """;
        var listCalls = 0;
        var handler = RetiredModelHandler(ClaudeCodeUsageClient.PreferredProbeModel, models, () => listCalls++);
        var client = new ClaudeCodeUsageClient(new HttpClient(handler));

        var usage = await client.GetUsageViaMessagesApiAsync(Credentials);
        await client.GetUsageViaMessagesApiAsync(Credentials);

        Assert.Equal(50, usage.SessionPercentage, precision: 3);
        Assert.Equal("claude-haiku-5", client.ProbeModel);
        Assert.Equal(1, listCalls); // the successor is remembered, not looked up again
    }

    [Fact]
    public async Task GetUsageViaMessagesApiAsync_Throws_WhenRetiredAndNoSuccessorListed()
    {
        const string models = """{ "data": [ { "id": "claude-fable-6", "created_at": "2027-01-01T00:00:00Z" } ] }""";
        var client = new ClaudeCodeUsageClient(new HttpClient(RetiredModelHandler(ClaudeCodeUsageClient.PreferredProbeModel, models)));

        await Assert.ThrowsAsync<ClaudeApiException>(() => client.GetUsageViaMessagesApiAsync(Credentials));
    }

    // A model that thinks by default and rejects the plain 1-token probe, but accepts it with
    // thinking turned off via "between_tools" (as Claude Sonnet 5.5 does).
    private static StubHandler RejectsProbeUnless(string acceptedThinkingType)
        => new(request =>
        {
            var body = request.Content!.ReadAsStringAsync().Result;
            return body.Contains($"\"thinking\":{{\"type\":\"{acceptedThinkingType}\"}}")
                ? WithRateLimitHeaders(("anthropic-ratelimit-unified-5h-utilization", "0.4"))
                : new HttpResponseMessage(HttpStatusCode.BadRequest);
        });

    [Fact]
    public async Task GetUsageViaMessagesApiAsync_RetriesWithThinkingOff_WhenModelRejectsThePlainProbe()
    {
        var handler = RejectsProbeUnless("between_tools");
        var client = new ClaudeCodeUsageClient(new HttpClient(handler));

        var usage = await client.GetUsageViaMessagesApiAsync(Credentials);

        Assert.Equal(40, usage.SessionPercentage, precision: 3);
        Assert.Equal(2, handler.Requests.Count);
        Assert.DoesNotContain("thinking", handler.Bodies[0]);
    }

    [Fact]
    public async Task GetUsageViaMessagesApiAsync_RemembersTheAcceptedThinkingShape()
    {
        var handler = RejectsProbeUnless("disabled");
        var client = new ClaudeCodeUsageClient(new HttpClient(handler));

        await client.GetUsageViaMessagesApiAsync(Credentials);
        var firstRefreshRequests = handler.Requests.Count;
        await client.GetUsageViaMessagesApiAsync(Credentials);

        Assert.Equal(3, firstRefreshRequests);
        Assert.Equal(4, handler.Requests.Count); // the next refresh goes straight to the shape that worked
        Assert.Contains("\"thinking\":{\"type\":\"disabled\"}", handler.Bodies[^1]);
    }

    [Fact]
    public async Task GetUsageViaMessagesApiAsync_Throws_WhenEveryThinkingShapeIsRejected()
    {
        var client = ClientReturning(HttpStatusCode.BadRequest, null, out var handler);

        var error = await Assert.ThrowsAsync<ClaudeApiException>(() => client.GetUsageViaMessagesApiAsync(Credentials));

        Assert.Equal(400, error.StatusCode);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task GetUsageViaMessagesApiAsync_ThrowsClaudeApiException_WhenResponseHasNoHeaders()
    {
        var client = ClientReturning(HttpStatusCode.OK, null, out _);

        await Assert.ThrowsAsync<ClaudeApiException>(() => client.GetUsageViaMessagesApiAsync(Credentials));
    }

    [Fact]
    public async Task GetUsageViaMessagesApiAsync_ThrowsAuthRequired_WhenTokenRejected()
    {
        var client = ClientReturning(HttpStatusCode.Unauthorized, null, out _);

        await Assert.ThrowsAsync<AuthRequiredException>(() => client.GetUsageViaMessagesApiAsync(Credentials));
    }
}
