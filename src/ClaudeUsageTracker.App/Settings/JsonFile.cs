using System.IO;
using System.Text.Json;

namespace ClaudeUsageTracker.App.Settings;

/// <summary>Reads and writes one of the app's JSON files under %APPDATA%\ClaudeUsageTracker.</summary>
internal static class JsonFile
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeUsageTracker");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static bool Exists(string fileName) => File.Exists(Path.Combine(Folder, fileName));

    /// <returns>The file's content, or null if there is no such file or it can't be read.</returns>
    public static T? Load<T>(string fileName) where T : class
    {
        try
        {
            var path = Path.Combine(Folder, fileName);
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupt or unreadable file — the caller starts fresh rather than crashing the app.
            return null;
        }
    }

    public static void Save<T>(string fileName, T value)
    {
        Directory.CreateDirectory(Folder);

        // Written beside the real file and swapped in: a crash or power cut mid-write would
        // otherwise leave a truncated file, which Load answers by starting fresh.
        var path = Path.Combine(Folder, fileName);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(value, JsonOptions));
        File.Move(temporaryPath, path, overwrite: true);
    }
}
