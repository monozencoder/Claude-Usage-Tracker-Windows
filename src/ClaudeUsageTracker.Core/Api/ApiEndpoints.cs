namespace ClaudeUsageTracker.Core.Api;

public static class ApiEndpoints
{
    public const string OAuthUsage = "https://api.anthropic.com/api/oauth/usage";
    public const string Messages = "https://api.anthropic.com/v1/messages";
    public const string Models = "https://api.anthropic.com/v1/models?limit=100";
}
