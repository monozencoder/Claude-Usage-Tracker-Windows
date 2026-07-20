namespace ClaudeUsageTracker.Platform.Startup;

public interface ILaunchAtLoginService
{
    bool IsEnabled { get; }

    void SetEnabled(bool enabled);
}
