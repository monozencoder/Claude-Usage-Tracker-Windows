using System.Text.Json;

namespace ClaudeUsageTracker.Core.ClaudeCode;

/// <summary>
/// Account info reported by <c>claude auth status --json</c>. That command only reads
/// Claude Code CLI's local state — it sends no prompt and consumes no usage.
/// </summary>
public sealed record ClaudeAuthStatus(bool LoggedIn, string? Email, string? OrganizationName, string? SubscriptionType)
{
    /// <summary>Subscription as shown to users, e.g. "max" → "Max"; null when unknown.</summary>
    public string? SubscriptionDisplayName =>
        string.IsNullOrWhiteSpace(SubscriptionType)
            ? null
            : char.ToUpperInvariant(SubscriptionType[0]) + SubscriptionType[1..];

    /// <returns>The parsed status, or null if <paramref name="json"/> isn't the expected object.</returns>
    public static ClaudeAuthStatus? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("loggedIn", out var loggedIn) ||
                loggedIn.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return null;
            }

            return new ClaudeAuthStatus(
                loggedIn.GetBoolean(),
                GetString(root, "email"),
                GetString(root, "orgName"),
                GetString(root, "subscriptionType"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? GetString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } s
            ? s
            : null;
}
