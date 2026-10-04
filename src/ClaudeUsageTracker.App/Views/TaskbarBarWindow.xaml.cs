using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace ClaudeUsageTracker.App.Views;

public partial class TaskbarBarWindow : Window
{
    /// <param name="taskbar">
    /// The taskbar's window, made this window's owner: an owned window always stays above its
    /// owner, so the strip stays on the (topmost) taskbar without fighting it for the top spot,
    /// and goes wherever the taskbar goes in the z-order — e.g. behind a full-screen app.
    /// </param>
    public TaskbarBarWindow(IntPtr taskbar)
    {
        InitializeComponent();
        UseLightTaskbar(false);
        new WindowInteropHelper(this).Owner = taskbar;

        // Never takes focus (a click must not pull it away from the app in use) and stays out of Alt+Tab.
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            SetWindowLongPtr(handle, GwlExStyle, GetWindowLongPtr(handle, GwlExStyle) | WsExNoActivate | WsExToolWindow);
        };
    }

    public event Action? Clicked;

    private bool _lightTaskbar;
    private bool _overFullScreen;

    /// <summary>Picks text and track colors that read on the taskbar: light on a dark one, dark on a light one.</summary>
    public void UseLightTaskbar(bool light)
    {
        _lightTaskbar = light;
        ApplyColors();
    }

    /// <summary>
    /// Whether the strip is drawn over a full-screen app instead of the taskbar. There's no telling
    /// what's behind it then, so it brings its own dark backdrop, and light text whatever the taskbar's color.
    /// </summary>
    public bool OverFullScreen
    {
        get => _overFullScreen;
        set
        {
            if (_overFullScreen == value)
                return;
            _overFullScreen = value;
            ApplyColors();
        }
    }

    private void ApplyColors()
    {
        var light = _lightTaskbar && !_overFullScreen;
        Resources["TaskbarBarTextBrush"] = Frozen(light ? Color.FromRgb(0x1B, 0x1B, 0x1B) : Colors.White);
        Resources["TaskbarBarTrackBrush"] = Frozen(light ? Color.FromArgb(0x30, 0, 0, 0) : Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF));
        // Transparent rather than null, so the gaps between the lines are clickable too.
        Resources["TaskbarBarBackdropBrush"] = _overFullScreen ? Frozen(Color.FromArgb(0xA6, 0, 0, 0)) : Brushes.Transparent;
        Resources["TaskbarBarHoverBrush"] = Frozen(
            _overFullScreen ? Color.FromArgb(0xCC, 0, 0, 0)
            : light ? Color.FromArgb(0x12, 0, 0, 0)
            : Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    // A press only becomes a drag once the cursor has moved this far (screen pixels), so a click
    // with a slightly unsteady hand still opens the flyout.
    private const int DragThreshold = 5;

    private bool _pressed;
    private bool _dragging;
    private int _pressX;

    /// <summary>Raised when the user starts dragging the strip sideways along the taskbar.</summary>
    public event Action? DragStarted;

    /// <summary>Raised as it's dragged, with how far the cursor is from where the drag started (screen pixels, negative is left).</summary>
    public event Action<int>? DragMoved;

    /// <summary>Raised when the drag ends, however it ended.</summary>
    public event Action? DragCompleted;

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Screen position, not relative to the window, which moves under the cursor.
        _pressX = System.Windows.Forms.Cursor.Position.X;
        _pressed = ((UIElement)sender).CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_pressed)
            return;

        var delta = System.Windows.Forms.Cursor.Position.X - _pressX;
        if (!_dragging)
        {
            if (Math.Abs(delta) < DragThreshold)
                return;
            _dragging = true;
            Mouse.OverrideCursor = Cursors.SizeWE;
            DragStarted?.Invoke();
        }

        DragMoved?.Invoke(delta);
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var clicked = _pressed && !_dragging;
        ((UIElement)sender).ReleaseMouseCapture();
        if (clicked)
            Clicked?.Invoke();
    }

    // Ends the press however it ended (button released, or capture taken away).
    private void OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        _pressed = false;
        if (!_dragging)
            return;
        _dragging = false;
        Mouse.OverrideCursor = null;
        DragCompleted?.Invoke();
    }

    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExNoActivate = 0x08000000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern long GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern long SetWindowLongPtr(IntPtr hWnd, int index, long value);
}
