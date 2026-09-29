using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ClaudeUsageTracker.App.Services;
using ClaudeUsageTracker.App.ViewModels;

namespace ClaudeUsageTracker.App.Views;

public partial class FlyoutWindow : Window
{
    // Only ticks while the flyout is visible, so the relative "Updated N min ago"
    // text stays current without polling in the background. The text has minute
    // granularity, so a few seconds of lag is invisible.
    private readonly DispatcherTimer _elapsedTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    public FlyoutWindow()
    {
        InitializeComponent();
        _elapsedTimer.Tick += (_, _) =>
        {
            if (DataContext is FlyoutViewModel viewModel)
                viewModel.RefreshLastUpdatedText(DateTimeOffset.Now);
        };
    }

    public void ToggleNearCursor()
    {
        if (IsVisible)
        {
            HideToTray();
            return;
        }

        // Render invisibly first so ActualWidth/ActualHeight are valid for positioning
        // before the window becomes visible at the wrong spot.
        Opacity = 0;
        Show();
        UpdateLayout();
        WindowPositioner.PositionNearCursor(this, RootPanel.Margin);
        AnimateIn();
        Activate();

        if (DataContext is FlyoutViewModel viewModel)
            viewModel.RefreshLastUpdatedText(DateTimeOffset.Now);
        _elapsedTimer.Start();
    }

    // Tucks the window away behind the tray icon (used by both the minimize and
    // close header buttons) without exiting the app.
    private void HideToTray()
    {
        Hide();
        _elapsedTimer.Stop();
    }

    private void OnMinimizeClicked(object sender, RoutedEventArgs e) => HideToTray();

    private void OnCloseClicked(object sender, RoutedEventArgs e) => HideToTray();

    // Small fade + rise animation so the flyout feels like a Windows 11 quick-settings
    // panel appearing, rather than an abrupt on/off toggle.
    private void AnimateIn()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease });
        RootTranslate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
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
