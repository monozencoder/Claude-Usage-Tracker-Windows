using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ClaudeUsageTracker.App.Services;
using ClaudeUsageTracker.App.ViewModels;

namespace ClaudeUsageTracker.App.Views;

public partial class FlyoutWindow : Window
{
    // Guards against the "click tray icon while open -> Deactivated hides it -> the
    // same click immediately reopens it" flicker: a click that arrives right after a
    // deactivation is treated as the one that closed the window, not a new open request.
    private DateTime _lastDeactivatedAtUtc = DateTime.MinValue;
    private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(200);

    public FlyoutWindow()
    {
        InitializeComponent();
    }

    public void ToggleNearCursor()
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        if (DateTime.UtcNow - _lastDeactivatedAtUtc < ReopenGuard)
            return;

        // Render invisibly first so ActualWidth/ActualHeight are valid for positioning
        // before the window becomes visible at the wrong spot.
        Opacity = 0;
        Show();
        UpdateLayout();
        WindowPositioner.PositionNearCursor(this);
        AnimateIn();
        Activate();
    }

    // Small fade + rise animation so the flyout feels like a Windows 11 quick-settings
    // panel appearing, rather than an abrupt on/off toggle.
    private void AnimateIn()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease });
        RootTranslate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (DataContext is FlyoutViewModel { IsPinned: true })
            return;

        _lastDeactivatedAtUtc = DateTime.UtcNow;
        Hide();
    }

    // Lets the borderless window be dragged by its header, skipping clicks that
    // land on one of the header buttons so they still work normally.
    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && IsWithinButton(source))
            return;

        DragMove();
    }

    private static bool IsWithinButton(DependencyObject element)
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is ButtonBase)
                return true;
        }
        return false;
    }
}
