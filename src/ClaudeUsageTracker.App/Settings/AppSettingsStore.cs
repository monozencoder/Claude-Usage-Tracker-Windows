namespace ClaudeUsageTracker.App.Settings;

/// <summary>
/// Owns the app's single <see cref="AppSettings"/> instance (settings.json). Everything reads
/// <see cref="Current"/>, and changes it through <see cref="Update"/>, which saves it and raises
/// <see cref="Changed"/> — so there are no stale copies that could overwrite each other's
/// changes on disk, and whoever shows a setting hears of a change wherever it was made.
/// </summary>
public sealed class AppSettingsStore
{
    private const string FileName = "settings.json";

    public AppSettings Current { get; } = JsonFile.Load<AppSettings>(FileName) ?? new AppSettings();

    /// <summary>Raised after a change to <see cref="Current"/> has been saved.</summary>
    public event Action? Changed;

    public void Update(Action<AppSettings> change)
    {
        change(Current);
        Commit();
    }

    /// <summary>Saves <see cref="Current"/> after it was changed in place (e.g. step by step during a drag), and raises <see cref="Changed"/>.</summary>
    public void Commit()
    {
        JsonFile.Save(FileName, Current);
        Changed?.Invoke();
    }
}
