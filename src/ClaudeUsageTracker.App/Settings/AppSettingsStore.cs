using System.IO;
using System.Text.Json;

namespace ClaudeUsageTracker.App.Settings;

/// <summary>
/// Owns the app's single <see cref="AppSettings"/> instance. Everything reads and
/// mutates <see cref="Current"/> and then calls <see cref="Save"/>, so there are no
/// stale copies that could overwrite each other's changes on disk.
/// </summary>
public sealed class AppSettingsStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClaudeUsageTracker", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppSettings Current { get; } = Load();

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Current, JsonOptions));
    }

    private static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();

            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupt or unreadable settings file — start fresh rather than crashing the app.
            return new AppSettings();
        }
    }
}
