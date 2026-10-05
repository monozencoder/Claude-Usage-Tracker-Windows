using System.Net;
using ClaudeUsageTracker.Core.Api;
using ClaudeUsageTracker.Core.ClaudeCode;
using ClaudeUsageTracker.Core.Models;
using ClaudeUsageTracker.Core.Usage;

namespace ClaudeUsageTracker.Tests.Usage;

public class UsageFetcherTests
{
    private static readonly DateTimeOffset Start = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    private static readonly ClaudeCredentialLocation Windows = new("Windows", IsWsl: false);
    private static readonly ClaudeCredentialLocation Wsl = new("WSL (Ubuntu)", IsWsl: true);

    private const string UsageJson = """
        { "five_hour": { "utilization": 30, "resets_at": "2026-07-20T18:00:00Z" },
          "seven_day": { "utilization": 5, "resets_at": "2026-07-25T00:00:00Z" } }
        """;

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class State : IUsageFetchState
    {
        public DateTimeOffset? LastUsageEndpointCall { get; set; }
        public bool UsageEndpointRateLimited { get; set; }
        public int Saves { get; private set; }

        public void Save() => Saves++;
    }

    private sealed class Environment : IClaudeCodeEnvironment
    {
        public ClaudeCredentialLookup Lookup { get; set; } = new(new ClaudeCodeCredentials("token-abc", null), Windows);
        public bool IsCliInstalled { get; set; } = true;

        public ClaudeCredentialLookup FindCredentials() => Lookup;
        public ClaudeAuthStatus? GetAuthStatus() => null;
        public bool StartLogin() => true;
    }

    /// <summary>Answers every request the same way, and counts those to the usage endpoint and the Messages API.</summary>
    private sealed class Api : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public bool Unreachable { get; set; }
        public int UsageCalls { get; private set; }
        public int MessagesCalls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var isUsage = request.RequestUri!.AbsoluteUri == ApiEndpoints.OAuthUsage;
            if (isUsage)
                UsageCalls++;
            else
                MessagesCalls++;

            if (Unreachable)
                throw new HttpRequestException("unreachable");

            var response = new HttpResponseMessage(Status) { Content = new StringContent(isUsage ? UsageJson : "{}") };
            if (!isUsage && Status == HttpStatusCode.OK)
                response.Headers.TryAddWithoutValidation("anthropic-ratelimit-unified-5h-utilization", "0.3");
            return Task.FromResult(response);
        }
    }

    private readonly Clock _clock = new(Start);
    private readonly State _state = new();
    private readonly Environment _environment = new();
    private readonly Api _api = new();

    private UsageFetcher CreateFetcher() =>
        new(new ClaudeCodeUsageClient(new HttpClient(_api)), _environment, _state, _clock);

    [Fact]
    public async Task TokenFree_ReadsUsageAndRecordsWhenTheEndpointWasCalled()
    {
        var result = await CreateFetcher().FetchAsync(avoidTokenUsage: true);

        Assert.Equal(30, result.Usage!.SessionPercentage);
        Assert.Equal(Start, _state.LastUsageEndpointCall);
        Assert.False(_state.UsageEndpointRateLimited);
        Assert.Equal(0, _api.MessagesCalls); // never spends usage in this mode
    }

    [Fact]
    public async Task TokenFree_HoldsBackASecondCallWithinTheMinimumSpacing()
    {
        var fetcher = CreateFetcher();
        await fetcher.FetchAsync(avoidTokenUsage: true);
        _clock.Now = Start.AddSeconds(30);

        var result = await fetcher.FetchAsync(avoidTokenUsage: true);

        Assert.Equal(UsagePolling.MinUsageEndpointSpacing - TimeSpan.FromSeconds(30), result.RetryAfter);
        Assert.False(result.UsageEndpointRateLimited);
        Assert.Null(result.Usage);
        Assert.Equal(1, _api.UsageCalls);
    }

    [Fact]
    public async Task TokenFree_CallsAgainOnceTheMinimumSpacingHasPassed()
    {
        var fetcher = CreateFetcher();
        await fetcher.FetchAsync(avoidTokenUsage: true);
        _clock.Now = Start + UsagePolling.MinUsageEndpointSpacing;

        var result = await fetcher.FetchAsync(avoidTokenUsage: true);

        Assert.NotNull(result.Usage);
        Assert.Equal(2, _api.UsageCalls);
    }

    [Fact]
    public async Task TokenFree_StillHoldsBackAfterARestart()
    {
        // The spacing lives in the saved state, so a fresh fetcher (a restarted app) honors it too.
        await CreateFetcher().FetchAsync(avoidTokenUsage: true);
        _clock.Now = Start.AddSeconds(30);

        var result = await CreateFetcher().FetchAsync(avoidTokenUsage: true);

        Assert.NotNull(result.RetryAfter);
        Assert.Equal(1, _api.UsageCalls);
    }

    [Fact]
    public async Task TokenFree_IgnoresASavedCallTimeInTheFuture()
    {
        // The clock was set back: waiting for a time hours ahead would hold refreshes off indefinitely.
        _state.LastUsageEndpointCall = Start.AddHours(3);

        var result = await CreateFetcher().FetchAsync(avoidTokenUsage: true);

        Assert.NotNull(result.Usage);
    }

    [Fact]
    public async Task TokenFree_BacksOffAfterA429UntilTheRetryInterval()
    {
        var fetcher = CreateFetcher();
        _api.Status = HttpStatusCode.TooManyRequests;

        var limited = await fetcher.FetchAsync(avoidTokenUsage: true);

        Assert.True(limited.UsageEndpointRateLimited);
        Assert.Null(limited.RetryAfter);
        Assert.True(_state.UsageEndpointRateLimited);

        // Past the ordinary spacing, but still within the longer back-off.
        _clock.Now = Start.AddMinutes(5);
        var heldBack = await fetcher.FetchAsync(avoidTokenUsage: true);

        Assert.NotNull(heldBack.RetryAfter);
        Assert.True(heldBack.UsageEndpointRateLimited);
        Assert.Equal(1, _api.UsageCalls);

        // The regular 10-minute tick gets through, and a success lifts the back-off.
        _clock.Now = Start.AddSeconds(UsagePolling.RateLimitedRetrySeconds);
        _api.Status = HttpStatusCode.OK;
        var recovered = await fetcher.FetchAsync(avoidTokenUsage: true);

        Assert.NotNull(recovered.Usage);
        Assert.False(_state.UsageEndpointRateLimited);
    }

    [Fact]
    public async Task TokenFree_SavesStateOnlyWhenItChanges()
    {
        await CreateFetcher().FetchAsync(avoidTokenUsage: true);

        // Once for the call time; the rate-limit flag was already off.
        Assert.Equal(1, _state.Saves);
    }

    [Fact]
    public async Task TokenFree_ReportsOtherStatusCodes()
    {
        _api.Status = HttpStatusCode.InternalServerError;

        var result = await CreateFetcher().FetchAsync(avoidTokenUsage: true);

        Assert.Equal(UsageFetchError.ApiStatus, result.Error);
        Assert.Equal(500, result.ErrorStatusCode);
        Assert.Null(result.ErrorStatus); // transient: the tray icon keeps what it showed
    }

    [Fact]
    public async Task TokenUsing_ProbesTheMessagesApiAndIsNeverHeldBack()
    {
        var fetcher = CreateFetcher();

        var first = await fetcher.FetchAsync(avoidTokenUsage: false);
        var second = await fetcher.FetchAsync(avoidTokenUsage: false);

        Assert.NotNull(first.Usage);
        Assert.NotNull(second.Usage);
        Assert.Equal(2, _api.MessagesCalls);
        Assert.Equal(0, _api.UsageCalls);
        Assert.Null(_state.LastUsageEndpointCall);
        Assert.Equal(0, _state.Saves);
    }

    [Fact]
    public async Task TokenUsing_ReportsAProbeFailure()
    {
        _api.Status = HttpStatusCode.InternalServerError;

        var result = await CreateFetcher().FetchAsync(avoidTokenUsage: false);

        // A 500 carries no rate-limit headers to read the usage from.
        Assert.Equal(UsageFetchError.MessagesApiFailed, result.Error);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RejectedToken_AsksToSignIn(bool avoidTokenUsage)
    {
        _api.Status = HttpStatusCode.Unauthorized;

        var result = await CreateFetcher().FetchAsync(avoidTokenUsage);

        Assert.Equal(UsageFetchError.SignInRejected, result.Error);
        Assert.Equal(UsageStatusLevel.Critical, result.ErrorStatus);
        Assert.True(result.CanSignIn);
        Assert.Null(result.WslLocationName);
    }

    [Fact]
    public async Task RejectedToken_FromWsl_NamesTheDistroInsteadOfOfferingSignIn()
    {
        _environment.Lookup = new(new ClaudeCodeCredentials("token-abc", null), Wsl);
        _api.Status = HttpStatusCode.Unauthorized;

        var result = await CreateFetcher().FetchAsync(avoidTokenUsage: true);

        Assert.Equal(UsageFetchError.SignInRejected, result.Error);
        Assert.False(result.CanSignIn); // its CLI can't be driven from Windows
        Assert.Equal("WSL (Ubuntu)", result.WslLocationName);
    }

    [Fact]
    public async Task ExpiredCredentials_AreReportedWithoutCallingTheApi()
    {
        _environment.Lookup = new(null, Windows);

        var result = await CreateFetcher().FetchAsync(avoidTokenUsage: true);

        Assert.Equal(UsageFetchError.SignInExpired, result.Error);
        Assert.True(result.CanSignIn);
        Assert.Equal(0, _api.UsageCalls);
        Assert.Null(_state.LastUsageEndpointCall); // nothing was called, so nothing to space out
    }

    [Fact]
    public async Task NoCredentials_WithTheCliInstalled_AsksToSignIn()
    {
        _environment.Lookup = new(null, null);

        var result = await CreateFetcher().FetchAsync(avoidTokenUsage: true);

        Assert.Equal(UsageFetchError.NotSignedIn, result.Error);
        Assert.Equal(UsageStatusLevel.Safe, result.ErrorStatus);
        Assert.True(result.CanSignIn);
    }

    [Fact]
    public async Task NoCredentials_WithoutTheCli_SaysItIsNotInstalled()
    {
        _environment.Lookup = new(null, null);
        _environment.IsCliInstalled = false;

        var result = await CreateFetcher().FetchAsync(avoidTokenUsage: true);

        Assert.Equal(UsageFetchError.NotInstalled, result.Error);
        Assert.False(result.CanSignIn); // there is no CLI to sign in with
    }

    [Fact]
    public async Task NetworkFailure_IsReportedAsTransient()
    {
        _api.Unreachable = true;

        var result = await CreateFetcher().FetchAsync(avoidTokenUsage: true);

        Assert.Equal(UsageFetchError.Network, result.Error);
        Assert.Null(result.ErrorStatus);
        Assert.False(result.CanSignIn);
    }
}
