namespace ClaudeUsageTracker.Core.Api;

/// <summary>Turns API model ids into the names people know them by.</summary>
public static class ModelNames
{
    /// <summary>
    /// "claude-haiku-4-5" → "Claude Haiku 4.5", "claude-sonnet-4-5-20250929" → "Claude Sonnet 4.5".
    /// The family is capitalized, version parts are joined with dots and a trailing date snapshot
    /// is dropped. Ids that don't look like "claude-&lt;family&gt;-&lt;version&gt;" come back unchanged.
    /// </summary>
    public static string ToDisplayName(string modelId)
    {
        var parts = modelId.Split('-').ToList();
        if (parts.Count < 3 || !parts[0].Equals("claude", StringComparison.OrdinalIgnoreCase))
            return modelId;

        // Snapshot ids end in an 8-digit date.
        if (parts[^1].Length == 8 && parts[^1].All(char.IsAsciiDigit))
            parts.RemoveAt(parts.Count - 1);

        var family = parts[1];
        var version = parts.Skip(2).ToList();
        if (family.Length == 0 || version.Count == 0 || !version.All(p => p.Length > 0 && p.All(char.IsAsciiDigit)))
            return modelId;

        return $"Claude {char.ToUpperInvariant(family[0])}{family[1..]} {string.Join('.', version)}";
    }
}
