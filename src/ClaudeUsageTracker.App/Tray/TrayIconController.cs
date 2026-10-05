using System.Windows.Controls;
using ClaudeUsageTracker.App.Localization;
using ClaudeUsageTracker.Core.Models;
using ClaudeUsageTracker.Platform.TrayIcon;
using DrawingIcon = System.Drawing.Icon;
using H.NotifyIcon;

namespace ClaudeUsageTracker.App.Tray;

/// <summary>Owns the tray icon: click routing, right-click menu, and icon repainting on each usage update.</summary>
public sealed class TrayIconController : IDisposable
{
    private readonly TrayIconRenderer _renderer;
    private readonly TaskbarIcon _taskbarIcon;
    private DrawingIcon? _currentIcon;

    public event Action? Clicked;
    public event Action? RefreshRequested;
    public event Action? SettingsRequested;
    public event Action? ResetPositionRequested;
    public event Action? ExitRequested;

    /// <summary>Whether "Reset position" has anything to reset; checked each time the menu opens.</summary>
    public Func<bool>? CanResetPosition { get; set; }

    public event Action? FlyoutCompactToggled;
    public event Action? FlyoutClickThroughToggled;
    public event Action? TaskbarBarClickThroughToggled;

    /// <summary>The toggles' current state; checked each time the menu opens.</summary>
    public Func<bool>? IsFlyoutCompact { get; set; }
    public Func<bool>? IsFlyoutClickThrough { get; set; }
    public Func<bool>? IsTaskbarBarClickThrough { get; set; }

    /// <summary>Whether the taskbar bars are on at all, i.e. whether their toggle applies.</summary>
    public Func<bool>? IsTaskbarBarShown { get; set; }

    public TrayIconController(TrayIconRenderer renderer)
    {
        _renderer = renderer;

        // Segoe Fluent Icons glyphs: Refresh, Settings, Undo, PowerButton.
        var refreshItem = CreateItem("Tray_Refresh", "\uE72C", () => RefreshRequested?.Invoke());
        var settingsItem = CreateItem("Tray_Settings", "\uE713", () => SettingsRequested?.Invoke());
        var resetPositionItem = CreateItem("Tray_ResetPosition", "\uE7A7", () => ResetPositionRequested?.Invoke());
        var exitItem = CreateItem("Tray_Exit", "\uE7E8", () => ExitRequested?.Invoke());

        // Toggles: the glyph is a check mark (Accept) while on and blank while off, set as the menu opens.
        var flyoutCompactItem = CreateItem("Tray_FlyoutCompact", string.Empty, () => FlyoutCompactToggled?.Invoke());
        var flyoutClickThroughItem = CreateItem("Tray_FlyoutClickThrough", string.Empty, () => FlyoutClickThroughToggled?.Invoke());
        var taskbarBarClickThroughItem = CreateItem("Tray_TaskbarBarClickThrough", string.Empty, () => TaskbarBarClickThroughToggled?.Invoke());

        var contextMenu = new ContextMenu();
        contextMenu.Items.Add(refreshItem);
        contextMenu.Items.Add(settingsItem);
        contextMenu.Items.Add(resetPositionItem);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(flyoutCompactItem);
        contextMenu.Items.Add(flyoutClickThroughItem);
        contextMenu.Items.Add(taskbarBarClickThroughItem);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(exitItem);
        contextMenu.Opened += (_, _) =>
        {
            resetPositionItem.IsEnabled = CanResetPosition?.Invoke() ?? false;
            SetChecked(flyoutCompactItem, IsFlyoutCompact?.Invoke() ?? false);
            SetChecked(flyoutClickThroughItem, IsFlyoutClickThrough?.Invoke() ?? false);
            SetChecked(taskbarBarClickThroughItem, IsTaskbarBarClickThrough?.Invoke() ?? false);
            taskbarBarClickThroughItem.IsEnabled = IsTaskbarBarShown?.Invoke() ?? false;
        };

        _taskbarIcon = new TaskbarIcon
        {
            ToolTipText = "Claude Usage Tracker",
            ContextMenu = contextMenu
        };
        _taskbarIcon.TrayLeftMouseUp += (_, _) => Clicked?.Invoke();

        UpdateIcon(0, UsageStatusLevel.Safe);
        _taskbarIcon.ForceCreate();
    }

    private static MenuItem CreateItem(string textKey, string glyph, Action onClick)
    {
        // The font is set on the TextBlock itself: the app's implicit TextBlock style would
        // otherwise swap in the UI font and render the glyph as a box.
        var icon = new TextBlock
        {
            Text = glyph,
            FontFamily = (System.Windows.Media.FontFamily)System.Windows.Application.Current.Resources["IconFontFamily"],
            FontSize = 16
        };
        var item = new MenuItem { Header = Loc.Get(textKey), Icon = icon };
        item.Click += (_, _) => onClick();
        Loc.LanguageChanged += () => item.Header = Loc.Get(textKey);
        return item;
    }

    private static void SetChecked(MenuItem item, bool isChecked) => ((TextBlock)item.Icon).Text = isChecked ? "" : string.Empty;

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
