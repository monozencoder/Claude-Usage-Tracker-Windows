using System.Windows.Controls;
using ClaudeUsageTracker.Core.Models;
using ClaudeUsageTracker.Platform.TrayIcon;
using DrawingIcon = System.Drawing.Icon;
using H.NotifyIcon;

namespace ClaudeUsageTracker.App.Services;

/// <summary>Owns the tray icon: click routing, right-click menu, and icon repainting on each usage update.</summary>
public sealed class TrayIconController : IDisposable
{
    private readonly TrayIconRenderer _renderer;
    private readonly TaskbarIcon _taskbarIcon;
    private DrawingIcon? _currentIcon;

    public event Action? Clicked;
    public event Action? RefreshRequested;
    public event Action? ExitRequested;

    public TrayIconController(TrayIconRenderer renderer)
    {
        _renderer = renderer;

        var refreshItem = new MenuItem { Header = "Refresh" };
        refreshItem.Click += (_, _) => RefreshRequested?.Invoke();

        var exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => ExitRequested?.Invoke();

        var contextMenu = new ContextMenu();
        contextMenu.Items.Add(refreshItem);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(exitItem);

        _taskbarIcon = new TaskbarIcon
        {
            ToolTipText = "Claude Usage Tracker",
            ContextMenu = contextMenu
        };
        _taskbarIcon.TrayLeftMouseUp += (_, _) => Clicked?.Invoke();

        UpdateIcon(0, UsageStatusLevel.Safe);
        _taskbarIcon.ForceCreate();
    }

    public void UpdateIcon(double percentage, UsageStatusLevel status)
    {
        var newIcon = _renderer.Render(percentage, status);
        _taskbarIcon.Icon = newIcon;
        _currentIcon?.Dispose();
        _currentIcon = newIcon;
    }

    public void Dispose()
    {
        _currentIcon?.Dispose();
        _taskbarIcon.Dispose();
    }
}
