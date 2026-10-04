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
        _baseInset = RootPanel.Margin;
        _basePanelWidth = Width - _baseInset.Left - _baseInset.Right;
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
            // Also what keeps it in the tray corner while it's being resized there.
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

    private double _restingOpacity = 1;

    /// <summary>
    /// How opaque the flyout is once shown (1 = solid); the fade-in ends here. Applied to the
    /// whole window, so the shadow fades with the panel instead of showing through it.
    /// </summary>
    public double RestingOpacity
    {
        get => _restingOpacity;
        set
        {
            _restingOpacity = value;
            if (!IsVisible || _hidePending)
                return;
            // Drop the fade-in (running or finished), which would otherwise override the local value.
            BeginAnimation(OpacityProperty, null);
            Opacity = value;
        }
    }

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

        BeginAnimation(OpacityProperty, new DoubleAnimation(0, _restingOpacity, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease });
        RootTranslate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(_slideFrom, 0, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
    }

    // A finished animation keeps holding its end value (resting opacity, offset 0), which
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

    public const double MinScale = 0.75;
    public const double MaxScale = 2.0;

    // Dragging lands exactly on 100% when it gets this close, so the original size is easy to get back to.
    private const double ScaleSnapDistance = 0.04;

    // The XAML layout at 100%: the shadow room around the panel, and the panel's width.
    private readonly Thickness _baseInset;
    private readonly double _basePanelWidth;

    private double _scale = 1;

    /// <summary>
    /// Size of the flyout relative to its designed size (1 = 100%). Everything scales together —
    /// text, bars, spacing — so the layout keeps its proportions; the height still follows the content.
    /// </summary>
    public double Scale
    {
        get => _scale;
        set
        {
            _scale = Math.Clamp(value, MinScale, MaxScale);
            var inset = InsetAt(_scale);
            RootPanel.LayoutTransform = _scale == 1 ? Transform.Identity : new ScaleTransform(_scale, _scale);
            RootPanel.Margin = ResizeGrips.Margin = inset;
            Width = _basePanelWidth * _scale + inset.Left + inset.Right;
            // Display mode snaps glyphs to pixels for the unscaled size and looks uneven once scaled.
            TextOptions.SetTextFormattingMode(this, _scale == 1 ? TextFormattingMode.Display : TextFormattingMode.Ideal);
        }
    }

    /// <summary>Raised with the new <see cref="Scale"/> after the user finishes resizing the flyout.</summary>
    public event Action<double>? ScaleSaved;

    // Shadow room grows with the panel, but never shrinks below what the shadow needs at 100%.
    private Thickness InsetAt(double scale)
    {
        var factor = Math.Max(scale, 1);
        return new Thickness(_baseInset.Left * factor, _baseInset.Top * factor, _baseInset.Right * factor, _baseInset.Bottom * factor);
    }

    // State of a grip drag. Everything is measured from where the drag started (cursor in screen
    // pixels, not relative to the window, which moves and resizes under it), so errors don't add up.
    private bool _resizing;
    private int _resizeHorizontal;
    private int _resizeVertical;
    private System.Drawing.Point _resizeStartCursor;
    private double _resizeStartScale;
    private ScreenPoint _resizeStartPosition = new(0, 0);
    private Size _resizeStartSize;

    private void OnGripMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var grip = (FrameworkElement)sender;
        var directions = ((string)grip.Tag).Split(',');
        _resizeHorizontal = int.Parse(directions[0]);
        _resizeVertical = int.Parse(directions[1]);
        _resizeStartCursor = System.Windows.Forms.Cursor.Position;
        _resizeStartScale = _scale;
        _resizeStartPosition = WindowPositioner.GetPosition(this);
        _resizeStartSize = new Size(ActualWidth, ActualHeight);
        _resizing = grip.CaptureMouse();
        e.Handled = true;
    }

    private void OnGripMouseMove(object sender, MouseEventArgs e)
    {
        if (!_resizing)
            return;

        var dpi = VisualTreeHelper.GetDpi(this);
        var cursor = System.Windows.Forms.Cursor.Position;
        var startInset = InsetAt(_resizeStartScale);

        // How far the dragged edge moved outward, as a change in scale. A corner follows
        // whichever direction was dragged further, like an aspect-locked resize elsewhere.
        var basePanelHeight = (_resizeStartSize.Height - startInset.Top - startInset.Bottom) / _resizeStartScale;
        var byWidth = (cursor.X - _resizeStartCursor.X) / dpi.DpiScaleX * _resizeHorizontal / _basePanelWidth;
        var byHeight = (cursor.Y - _resizeStartCursor.Y) / dpi.DpiScaleY * _resizeVertical / basePanelHeight;
        var change = _resizeVertical == 0 || (_resizeHorizontal != 0 && Math.Abs(byWidth) > Math.Abs(byHeight)) ? byWidth : byHeight;

        var scale = Math.Clamp(_resizeStartScale + change, MinScale, MaxScale);
        if (Math.Abs(scale - 1) < ScaleSnapDistance)
            scale = 1;
        if (scale == _scale)
            return;

        Scale = scale;
        UpdateLayout(); // the new height is only known after layout

        // In the tray corner SizeChanged re-places it. Elsewhere, hold the edges opposite the
        // dragged one where they were (the panel's edges, not the window's: the inset changes too).
        if (SavedPosition is null)
            return;
        var inset = InsetAt(scale);
        var x = _resizeHorizontal < 0
            ? _resizeStartSize.Width - startInset.Right - (ActualWidth - inset.Right)
            : startInset.Left - inset.Left;
        var y = _resizeVertical < 0
            ? _resizeStartSize.Height - startInset.Bottom - (ActualHeight - inset.Bottom)
            : startInset.Top - inset.Top;
        WindowPositioner.MoveTo(this, new ScreenPoint(
            _resizeStartPosition.X + (int)Math.Round(x * dpi.DpiScaleX),
            _resizeStartPosition.Y + (int)Math.Round(y * dpi.DpiScaleY)));
    }

    private void OnGripMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => ((UIElement)sender).ReleaseMouseCapture();

    // Ends the drag however it ended (button released, or capture taken away).
    private void OnGripLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (!_resizing)
            return;
        _resizing = false;

        if (_scale != _resizeStartScale)
            ScaleSaved?.Invoke(_scale);

        // Resizing from a left/top edge moves the window, and growing can push it off screen.
        if (SavedPosition is null)
            return;
        WindowPositioner.KeepOnScreen(this, RootPanel.Margin);
        var position = WindowPositioner.GetPosition(this);
        if (position == SavedPosition)
            return;
        SavedPosition = position;
        PositionSaved?.Invoke(position);
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
