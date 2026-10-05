using ClaudeUsageTracker.Core.ClaudeCode;

namespace ClaudeUsageTracker.Platform.ClaudeCode;

/// <summary>The Claude Code CLI as installed on this PC: Windows-native, or inside a WSL distro.</summary>
public sealed class ClaudeCodeEnvironment : IClaudeCodeEnvironment
{
    public bool IsCliInstalled => ClaudeCli.IsInstalled;

    public ClaudeCredentialLookup FindCredentials() => ClaudeCredentialResolver.Resolve();

    public ClaudeAuthStatus? GetAuthStatus() => ClaudeCli.GetAuthStatus();

    public bool StartLogin() => ClaudeCli.StartLogin();
}
