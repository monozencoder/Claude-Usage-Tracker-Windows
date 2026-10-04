using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ClaudeUsageTracker.App.ViewModels;
using ClaudeUsageTracker.App.Views;
using Microsoft.Win32;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace ClaudeUsageTracker.App.Tray;

/// <summary>
/// Shows the usage bars on the taskbar itself — every monitor's — in the empty stretch left of
/// the notification area. Windows 11 has no supported way to put anything in the taskbar
/// (deskbands are gone), so this lays a small window over each one and keeps it there: a taskbar
/// moves, resizes, auto-hides, changes color, comes and goes with its monitor, and is recreated
/// when Explorer restarts, none of which it announces — hence the polling.
/// </summary>
public sealed class TaskbarBarController : IDisposable
{
    private const string PrimaryTaskbarClass = "Shell_TrayWnd";
    private const string SecondaryTaskbarClass = "Shell_SecondaryTrayWnd";

    // Gap between the strip and the notification area.
    private const double GapToNotificationArea = 4;

    // Where the strip's right edge goes when the notification area can't be measured. The other
    // monitors' taskbars never can be: theirs is just the clock and the notification bell.
    private const double PrimaryFallbackRightInset = 320;
    private const double SecondaryRightInset = 124;

    private readonly FlyoutViewModel _viewModel;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    // One strip per taskbar, by the taskbar's window handle.
    private readonly Dictionary<IntPtr, TaskbarBarWindow> _strips = [];
    private bool _lightTaskbar;
    private bool _enabled;

    public TaskbarBarController(FlyoutViewModel viewModel)
    {
        _viewModel = viewModel;
        _timer.Tick += (_, _) => Update();
    }

    public event Action? Clicked;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
                return;
            _enabled = value;
            if (value)
            {
                _timer.Start();
                Update();
            }
            else
            {
                _timer.Stop();
                CloseAll();
            }
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        CloseAll();
    }

    private void Update()
    {
        var taskbars = FindTaskbars();

        // Taskbars that are gone: a monitor unplugged, or Explorer restarting (the strips come
        // back with the new taskbars). An owned window is destroyed with its owner anyway.
        foreach (var gone in _strips.Keys.Where(taskbar => !taskbars.Contains(taskbar)).ToList())
            Close(gone);

        var light = IsTaskbarLight();
        if (light != _lightTaskbar)
        {
            _lightTaskbar = light;
            foreach (var strip in _strips.Values)
                strip.UseLightTaskbar(light);
        }

        for (var i = 0; i < taskbars.Count; i++)
            UpdateStrip(taskbars[i], isPrimary: i == 0);
    }

    private void UpdateStrip(IntPtr taskbar, bool isPrimary)
    {
        if (!GetWindowRect(taskbar, out var taskbarRect))
        {
            Close(taskbar);
            return;
        }

        if (!_strips.TryGetValue(taskbar, out var strip))
        {
            strip = new TaskbarBarWindow(taskbar) { DataContext = _viewModel };
            strip.UseLightTaskbar(_lightTaskbar);
            strip.Clicked += () => Clicked?.Invoke();
            var created = strip;
            strip.Closed += (_, _) =>
            {
                if (_strips.TryGetValue(taskbar, out var current) && current == created)
                    _strips.Remove(taskbar);
            };
            new WindowInteropHelper(strip).EnsureHandle();
            _strips[taskbar] = strip;
        }

        var bounds = Drawing.Rectangle.FromLTRB(taskbarRect.Left, taskbarRect.Top, taskbarRect.Right, taskbarRect.Bottom);
        var screen = WinForms.Screen.FromHandle(taskbar).Bounds;

        // Only a horizontal taskbar has room for it. An auto-hidden one is parked almost entirely
        // off screen, and a full-screen app covers the taskbar: the strip must not be left floating.
        var horizontal = bounds.Width > bounds.Height;
        var onScreen = Drawing.Rectangle.Intersect(bounds, screen).Height >= bounds.Height / 2;
        if (!horizontal || !onScreen || IsCoveredByFullScreenWindow(screen))
        {
            strip.Hide();
            return;
        }

        // The strip takes the DPI of the monitor it's on, so right after it's first moved onto a
        // monitor with a different scale this is still the old one; the next tick corrects it.
        var dpi = VisualTreeHelper.GetDpi(strip);
        strip.Height = bounds.Height / dpi.DpiScaleY;
        if (!strip.IsVisible)
            strip.Show();
        strip.UpdateLayout(); // the width follows the content

        var right = bounds.Right - (int)Math.Round((isPrimary ? PrimaryFallbackRightInset : SecondaryRightInset) * dpi.DpiScaleX);
        if (isPrimary)
        {
            var notificationArea = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
            if (notificationArea != IntPtr.Zero && GetWindowRect(notificationArea, out var notificationRect)
                && notificationRect.Left > bounds.Left && notificationRect.Left < bounds.Right)
                right = notificationRect.Left;
        }

        var x = right - (int)Math.Round((strip.ActualWidth + GapToNotificationArea) * dpi.DpiScaleX);
        SetWindowPos(new WindowInteropHelper(strip).Handle, IntPtr.Zero, x, bounds.Top, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    /// <summary>The main taskbar first (if there is one), then the other monitors'.</summary>
    private static List<IntPtr> FindTaskbars()
    {
        var taskbars = new List<IntPtr>();
        var primary = FindWindowEx(IntPtr.Zero, IntPtr.Zero, PrimaryTaskbarClass, null);
        if (primary != IntPtr.Zero)
            taskbars.Add(primary);

        var secondary = IntPtr.Zero;
        while ((secondary = FindWindowEx(IntPtr.Zero, secondary, SecondaryTaskbarClass, null)) != IntPtr.Zero)
            taskbars.Add(secondary);
        return taskbars;
    }

    private void Close(IntPtr taskbar)
    {
        if (_strips.Remove(taskbar, out var strip))
            strip.Close();
    }

    private void CloseAll()
    {
        foreach (var taskbar in _strips.Keys.ToList())
            Close(taskbar);
    }

    // The taskbar follows Windows' own mode ("Choose your default Windows mode"), not the app mode.
    private static bool IsTaskbarLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
    }

    // A foreground window that fills this taskbar's whole monitor (a video, a game, a slide show).
    // The desktop also spans the monitor, so it and the taskbars are excluded by class.
    private static bool IsCoveredByFullScreenWindow(Drawing.Rectangle screen)
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || !GetWindowRect(foreground, out var rect))
            return false;
        if (rect.Left > screen.Left || rect.Top > screen.Top || rect.Right < screen.Right || rect.Bottom < screen.Bottom)
            return false;

        var className = new StringBuilder(64);
        GetClassName(foreground, className, className.Capacity);
        return className.ToString() is not ("Progman" or "WorkerW" or PrimaryTaskbarClass or SecondaryTaskbarClass);
    }

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string? windowName);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}
