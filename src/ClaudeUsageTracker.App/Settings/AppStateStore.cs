using ClaudeUsageTracker.Core.Usage;

namespace ClaudeUsageTracker.App.Settings;

/// <summary>Owns the app's single <see cref="AppState"/> instance (state.json).</summary>
public sealed class AppStateStore : IUsageFetchState
{
    private const string FileName = "state.json";

    /// <param name="legacyState">
    /// The state an older version kept in the settings file, if that file still has it. Taken
    /// over (and written to its own file straight away, as the settings file drops it the next
    /// time it's saved) when there is no state file yet.
    /// </param>
    public AppStateStore(AppState? legacyState)
    {
        if (legacyState is not null && !JsonFile.Exists(FileName))
        {
            Current = legacyState;
            Save();
            return;
        }

        Current = JsonFile.Load<AppState>(FileName) ?? new AppState();
    }

    public AppState Current { get; }

    public void Save() => JsonFile.Save(FileName, Current);

    DateTimeOffset? IUsageFetchState.LastUsageEndpointCall
    {
        get => Current.LastUsageEndpointCall;
        set => Current.LastUsageEndpointCall = value;
    }

    bool IUsageFetchState.UsageEndpointRateLimited
    {
        get => Current.UsageEndpointRateLimited;
        set => Current.UsageEndpointRateLimited = value;
    }
}
