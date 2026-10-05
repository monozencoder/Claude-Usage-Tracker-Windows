using ClaudeUsageTracker.Core.Models;

namespace ClaudeUsageTracker.Core.ClaudeCode;

/// <summary>Where a credentials file was found: "Windows", or "WSL (Ubuntu)" etc.</summary>
/// <param name="IsWsl">True for a WSL distro: its CLI can't be driven from here (e.g. to sign in).</param>
public sealed record ClaudeCredentialLocation(string DisplayName, bool IsWsl);

/// <summary>Outcome of looking for Claude Code CLI's credentials.</summary>
/// <param name="Credentials">Usable (unexpired) credentials, or null.</param>
/// <param name="Location">Where <paramref name="Credentials"/> came from — or, if they're
/// null but a credentials file exists, where the expired one was found.</param>
public sealed record ClaudeCredentialLookup(ClaudeCodeCredentials? Credentials, ClaudeCredentialLocation? Location)
{
    public bool CredentialsFileFound => Location is not null;
}

/// <summary>
/// The Claude Code CLI installed on this machine, as far as the app deals with it: its
/// credentials are read, its account status is asked for, and its own sign-in is started.
/// Nothing here sends a prompt. Every member may start a process, so call them off the UI thread.
/// </summary>
public interface IClaudeCodeEnvironment
{
    bool IsCliInstalled { get; }

    /// <summary>Finds the credentials wherever they currently live. Read-only: an expired token is reported, not refreshed.</summary>
    ClaudeCredentialLookup FindCredentials();

    /// <returns>The account status, or null if the CLI isn't installed or didn't answer.</returns>
    ClaudeAuthStatus? GetAuthStatus();

    /// <summary>Starts the CLI's own browser sign-in, without waiting for it to finish.</summary>
    /// <returns>False if the CLI isn't installed or couldn't be started.</returns>
    bool StartLogin();
}
