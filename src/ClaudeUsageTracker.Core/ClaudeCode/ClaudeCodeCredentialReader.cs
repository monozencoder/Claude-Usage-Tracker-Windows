using System.Text.Json;
using ClaudeUsageTracker.Core.Models;

namespace ClaudeUsageTracker.Core.ClaudeCode;

/// <summary>
/// Reads (never writes) the Claude Code CLI's own OAuth credentials file. Claude
/// Code CLI owns this file's lifecycle entirely, including token refresh — this
/// class only parses whatever is currently on disk.
/// </summary>
public static class ClaudeCodeCredentialReader
{
    public static string CredentialsFilePath =>
        Path.Combine(ClaudeConfigDirectory, ".credentials.json");

    public static string ClaudeConfigDirectory =>
        Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } configuredDir
            ? configuredDir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");

    public static ClaudeCodeCredentials? TryRead()
    {
        string json;
        try
        {
            json = File.ReadAllText(CredentialsFilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return Parse(json);
    }

    public static ClaudeCodeCredentials? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth))
                return null;

            if (!oauth.TryGetProperty("accessToken", out var tokenElement) ||
                tokenElement.GetString() is not { Length: > 0 } accessToken)
            {
                return null;
            }

            long? expiresAt = oauth.TryGetProperty("expiresAt", out var expiresElement) && expiresElement.TryGetInt64(out var value)
                ? value
                : null;

            return new ClaudeCodeCredentials(accessToken, expiresAt);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
