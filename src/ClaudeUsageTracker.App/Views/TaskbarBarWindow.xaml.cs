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

    /// <summary>Picks text and track colors that read on the taskbar: light on a dark one, dark on a light one.</summary>
    public void UseLightTaskbar(bool light)
    {
        Resources["TaskbarBarTextBrush"] = Frozen(light ? Color.FromRgb(0x1B, 0x1B, 0x1B) : Colors.White);
        Resources["TaskbarBarTrackBrush"] = Frozen(light ? Color.FromArgb(0x30, 0, 0, 0) : Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF));
        Resources["TaskbarBarHoverBrush"] = Frozen(light ? Color.FromArgb(0x12, 0, 0, 0) : Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => Clicked?.Invoke();

    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExNoActivate = 0x08000000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern long GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern long SetWindowLongPtr(IntPtr hWnd, int index, long value);
}
