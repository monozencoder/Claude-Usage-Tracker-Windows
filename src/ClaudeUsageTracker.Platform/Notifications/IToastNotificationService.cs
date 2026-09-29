namespace ClaudeUsageTracker.Platform.Notifications;

public interface IToastNotificationService
{
    void Show(string title, string message);
}
