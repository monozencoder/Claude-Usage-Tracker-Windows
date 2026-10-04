using ClaudeUsageTracker.Core.History;

namespace ClaudeUsageTracker.Tests.History;

public class UsageHistoryTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Add_KeepsTheFirstSample()
    {
        var history = new UsageHistory();

        Assert.True(history.Add(new UsageSample(Start, 10, 5)));
        Assert.Single(history.Samples);
    }

    [Fact]
    public void Add_SkipsASampleTooSoonAfterTheLast_EvenIfItChanged()
    {
        var history = new UsageHistory();
        history.Add(new UsageSample(Start, 10, 5));

        Assert.False(history.Add(new UsageSample(Start.AddSeconds(30), 12, 5)));
    }

    [Fact]
    public void Add_SkipsAnUnchangedSampleUntilTheHeartbeat()
    {
        var history = new UsageHistory();
        history.Add(new UsageSample(Start, 10, 5));

        Assert.False(history.Add(new UsageSample(Start.AddMinutes(5), 10, 5)));
        Assert.True(history.Add(new UsageSample(Start.AddMinutes(10), 10, 5)));
    }

    [Fact]
    public void Add_KeepsAChangedSampleAfterAMinute()
    {
        var history = new UsageHistory();
        history.Add(new UsageSample(Start, 10, 5));

        Assert.True(history.Add(new UsageSample(Start.AddMinutes(1), 11, 5)));
    }

    [Fact]
    public void Add_SkipsASampleFromBeforeTheLast()
    {
        var history = new UsageHistory();
        history.Add(new UsageSample(Start, 10, 5));

        Assert.False(history.Add(new UsageSample(Start.AddHours(-1), 50, 5)));
    }

    [Fact]
    public void Prune_DropsSamplesOlderThanTheRetention()
    {
        var history = new UsageHistory();
        history.Add(new UsageSample(Start, 10, 5));
        history.Add(new UsageSample(Start.AddDays(6), 20, 50));

        var dropped = history.Prune(Start.AddDays(7).AddMinutes(1));

        Assert.Equal(1, dropped);
        Assert.Equal(20, Assert.Single(history.Samples).Session);
    }

    [Fact]
    public void FormatAndTryParse_RoundTrip()
    {
        var sample = new UsageSample(Start, 12.5, 40);

        Assert.True(UsageHistory.TryParse(UsageHistory.Format(sample), out var parsed));
        Assert.Equal(sample, parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not,a,sample")]
    [InlineData("1791201600,12.5")]
    public void TryParse_RejectsMalformedLines(string line)
    {
        Assert.False(UsageHistory.TryParse(line, out _));
    }
}
