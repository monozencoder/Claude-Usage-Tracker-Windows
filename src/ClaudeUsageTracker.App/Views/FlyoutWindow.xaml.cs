using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ClaudeUsageTracker.App.Settings;
using ClaudeUsageTracker.App.ViewModels;
using Microsoft.Win32;

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

        // A monitor unplugged / resolution changed while the flyout is open can strand it off screen.
        // Raised on a system-events thread, hence the dispatch.
        EventHandler onDisplayChanged = (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (!IsVisible)
                return;
            if (SavedPosition is null)
                Place();
            else
                WindowPositioner.KeepOnScreen(this, RootPanel.Margin);
        });
        SystemEvents.DisplaySettingsChanged += onDisplayChanged;

        // The height follows the content (e.g. a warning banner appearing). Anchored to the tray
        // corner, it has to be re-placed so it grows away from the taskbar instead of under it.
        SizeChanged += (_, _) =>
        {
            if (IsVisible && SavedPosition is null)
                Place();
        };
        Closed += (_, _) => SystemEvents.DisplaySettingsChanged -= onDisplayChanged;

        // The flyout animates itself; DWM's own show/hide transition on top of that reads as flicker.
        SourceInitialized += (_, _) =>
        {
            var disabled = 1;
            DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, DwmwaTransitionsForceDisabled, ref disabled, sizeof(int));
        };
    }

    private const int DwmwaTransitionsForceDisabled = 3;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>
    /// Where the user last dragged the flyout (screen pixels), or null to open it near the
    /// tray/cursor. Kept as-is even when clamping moves the window, so it returns to the
    /// saved spot if the monitor it was on comes back.
    /// </summary>
    public ScreenPoint? SavedPosition { get; set; }

    /// <summary>Raised after the user drags the flyout somewhere new.</summary>
    public event Action<ScreenPoint>? PositionSaved;

    /// <summary>Forgets the dragged-to spot; if the flyout is open, moves it back to the tray corner.</summary>
    public void ResetPosition()
    {
        SavedPosition = null;
        if (IsVisible)
            Place();
    }

    /// <summary>Shows the flyout (at <see cref="SavedPosition"/>, or in the tray corner), or hides it if shown.</summary>
    public void Toggle()
    {
        if (_hidePending)
            return;
        if (IsVisible)
        {
            HideToTray();
            return;
        }

        ResetToAnimationStart();
        // After the first show the size is known, so move while still hidden: the window then
        // never appears at its previous spot. The first show has to lay out before it can place.
        if (ActualWidth > 0)
            Place();
        Show();
        UpdateLayout();
        Place();
        AnimateIn();
        Activate();

        if (DataContext is FlyoutViewModel viewModel)
            viewModel.RefreshLastUpdatedText(DateTimeOffset.Now);
        _elapsedTimer.Start();
    }

    private void Place()
    {
        if (SavedPosition is { } position)
            WindowPositioner.PositionAt(this, position, RootPanel.Margin);
        else
            // Rise away from a bottom taskbar, drop away from a top one.
            _slideFrom = WindowPositioner.PositionAtTrayCorner(this, RootPanel.Margin) == WindowPositioner.TaskbarEdge.Top
                ? -SlideInOffset
                : SlideInOffset;
    }

    // Tucks the window away behind the tray icon (the close button) without exiting the app.
    //
    // A transparent (layered) window keeps its last frame while hidden, and Show() puts that
    // frame on screen for an instant before WPF draws the new one — the fully opaque flyout
    // flashing in before the fade-in starts. So clear it first: go transparent, let that frame
    // render (Background runs after Render), then hide.
    private void HideToTray()
    {
        _elapsedTimer.Stop();
        ResetToAnimationStart();
        _hidePending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            Hide();
            _hidePending = false;
        });
    }

    private bool _hidePending;

    private void OnCloseClicked(object sender, RoutedEventArgs e) => HideToTray();

    private const double SlideInOffset = 10;

    // Where the slide-in starts: below the resting spot (rising), or above it under a top taskbar.
    private double _slideFrom = SlideInOffset;

    // Small fade + rise animation so the flyout feels like a Windows 11 quick-settings
    // panel appearing, rather than an abrupt on/off toggle.
    private void AnimateIn()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease });
        RootTranslate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(_slideFrom, 0, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
    }

    // A finished animation keeps holding its end value (Opacity 1, offset 0), which
    // overrides any local value. Without clearing it, the next Show() would flash the
    // window fully visible at its old spot before the animation snaps it back to the start.
    private void ResetToAnimationStart()
    {
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        RootTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        RootTranslate.Y = _slideFrom;
    }

    // Lets the borderless window be dragged by anywhere on its panel, skipping clicks that
    // land on a button (settings, close, refresh, sign in) so they still work normally.
    private void OnPanelMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && IsWithinButton(source))
            return;

        // DragMove blocks until the button is released, so the position after it is where it was dropped.
        var before = WindowPositioner.GetPosition(this);
        DragMove();
        var after = WindowPositioner.GetPosition(this);
        if (after == before)
            return;

        SavedPosition = after;
        PositionSaved?.Invoke(after);
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
