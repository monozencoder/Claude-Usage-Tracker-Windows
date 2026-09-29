namespace ClaudeUsageTracker.Core.Api;

/// <summary>
/// Picks the model for the one-token usage probe from the Models API list, for when the
/// preferred model has been retired. The probe only needs response headers, so the
/// cheapest family wins: the newest Haiku, then Sonnet, then Opus. Premium families
/// (Fable, Mythos, ...) are never picked — they'd cost far more for the same headers.
/// </summary>
public static class ProbeModelSelector
{
    private static readonly string[] FamiliesCheapestFirst = ["haiku", "sonnet", "opus"];

    /// <returns>The model id to probe with, or null if nothing suitable is listed.</returns>
    public static string? Pick(IEnumerable<(string Id, DateTimeOffset CreatedAt)> models)
    {
        var list = models.ToList();
        foreach (var family in FamiliesCheapestFirst)
        {
            var newest = list
                .Where(m => m.Id.Contains(family, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(m => m.CreatedAt)
                .Select(m => m.Id)
                .FirstOrDefault();
            if (newest is not null)
                return newest;
        }

        return null;
    }
}
