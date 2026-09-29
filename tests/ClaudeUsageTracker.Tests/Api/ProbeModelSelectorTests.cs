using ClaudeUsageTracker.Core.Api;

namespace ClaudeUsageTracker.Tests.Api;

public class ProbeModelSelectorTests
{
    private static (string, DateTimeOffset) M(string id, int year) => (id, new DateTimeOffset(year, 1, 1, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Pick_PrefersNewestHaiku_OverPricierFamilies()
    {
        var picked = ProbeModelSelector.Pick([M("claude-opus-6", 2028), M("claude-haiku-4-5-20251001", 2025), M("claude-haiku-5", 2027)]);

        Assert.Equal("claude-haiku-5", picked);
    }

    [Fact]
    public void Pick_FallsBackToSonnet_ThenOpus()
    {
        Assert.Equal("claude-sonnet-6", ProbeModelSelector.Pick([M("claude-opus-6", 2028), M("claude-sonnet-6", 2027)]));
        Assert.Equal("claude-opus-6", ProbeModelSelector.Pick([M("claude-opus-6", 2028)]));
    }

    [Fact]
    public void Pick_NeverPicksPremiumFamilies()
    {
        Assert.Null(ProbeModelSelector.Pick([M("claude-fable-6", 2028), M("claude-mythos-6", 2028)]));
    }
}
