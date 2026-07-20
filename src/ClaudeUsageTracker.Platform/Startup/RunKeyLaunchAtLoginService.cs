using Microsoft.Win32;

namespace ClaudeUsageTracker.Platform.Startup;

/// <summary>
/// Registers/unregisters the app under HKCU\...\Run so it starts at Windows login.
/// Non-elevated, per-user — the simplest reliable mechanism for a tray app.
/// </summary>
public sealed class RunKeyLaunchAtLoginService(string appName, Func<string> executablePathProvider) : ILaunchAtLoginService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(appName) is string existing &&
                   string.Equals(existing.Trim('"'), executablePathProvider(), StringComparison.OrdinalIgnoreCase);
        }
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                         ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

        if (enabled)
            key.SetValue(appName, $"\"{executablePathProvider()}\"");
        else
            key.DeleteValue(appName, throwOnMissingValue: false);
    }
}
