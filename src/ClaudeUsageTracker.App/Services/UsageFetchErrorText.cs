using ClaudeUsageTracker.App.Localization;
using ClaudeUsageTracker.Core.Usage;

namespace ClaudeUsageTracker.App.Services;

/// <summary>Words a failed <see cref="UsageFetchResult"/> for the user, in the current UI language.</summary>
public static class UsageFetchErrorText
{
    /// <returns>The message, or null if <paramref name="result"/> carries no error.</returns>
    public static string? Describe(UsageFetchResult result) => result.Error switch
    {
        UsageFetchError.SignInRejected => SignInProblem(Loc.Get("Error_SignInRejected"), result),
        UsageFetchError.SignInExpired => SignInProblem(Loc.Get("Error_SignInExpired"), result),
        UsageFetchError.NotSignedIn => Loc.Get("Error_NotSignedIn"),
        UsageFetchError.NotInstalled => Loc.Get("Error_NotInstalled"),
        UsageFetchError.ApiStatus => Loc.Format("Error_Status", result.ErrorStatusCode),
        UsageFetchError.MessagesApiFailed => Loc.Get("Error_MessagesApiFailed"),
        UsageFetchError.Network => Loc.Get("Error_Network"),
        _ => null
    };

    // A WSL-only setup has to sign in from inside WSL, so the message says where.
    private static string SignInProblem(string problem, UsageFetchResult result)
        => result.WslLocationName is { } wsl ? Loc.Format("Error_SignInInWsl", problem, wsl) : problem;
}
