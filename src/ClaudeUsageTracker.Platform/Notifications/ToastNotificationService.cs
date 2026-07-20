using Microsoft.Toolkit.Uwp.Notifications;

namespace ClaudeUsageTracker.Platform.Notifications;

/// <summary>
/// Raises Windows toast notifications via the Windows Community Toolkit's
/// unpackaged-app-friendly compat layer (registers an AppUserModelID under the
/// hood; no MSIX packaging required).
/// </summary>
public sealed class ToastNotificationService : IToastNotificationService
{
    public void Show(string title, string message)
    {
        new ToastContentBuilder()
            .AddText(title)
            .AddText(message)
            .Show();
    }

    public void ClearAll() => ToastNotificationManagerCompat.History.Clear();
}
