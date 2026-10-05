using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using ClaudeUsageTracker.App.Services;
using System.Windows.Media;
using System.Windows.Threading;
using ClaudeUsageTracker.App.Settings;
using ClaudeUsageTracker.App.ViewModels;
using ClaudeUsageTracker.App.Views;
using Microsoft.Win32;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;
using static ClaudeUsageTracker.App.Interop.NativeMethods;

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
    private readonly AppSettingsStore _settingsStore;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    // One strip per taskbar, by the taskbar's window handle.
    private readonly Dictionary<IntPtr, TaskbarBarWindow> _strips = [];
    private bool _lightTaskbar;
    private bool _enabled;

    public TaskbarBarController(FlyoutViewModel viewModel, AppSettingsStore settingsStore)
    {
        _viewModel = viewModel;
        _settingsStore = settingsStore;
        _timer.Tick += (_, _) => Update();

        // Using the taskbar puts it in front of the strips. A second until the next tick is a long
        // time to have them gone, so they're put back as the foreground changes — and once more
        // shortly after, as the taskbar may only come forward after the event.
        _settleTimer.Tick += (_, _) =>
        {
            _settleTimer.Stop();
            Refresh();
        };
        _onForegroundChanged = (_, _, _, _, _, _, _) =>
        {
            Refresh();
            _settleTimer.Stop();
            _settleTimer.Start();
        };
        _foregroundHook = SetWinEventHook(EventSystemForeground, EventSystemForeground, IntPtr.Zero, _onForegroundChanged, 0, 0, WineventOutOfContext);
    }

    private readonly DispatcherTimer _settleTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };

    // Held in a field: the hook only has a function pointer to it, which doesn't keep it alive.
    private readonly WinEventProc _onForegroundChanged;
    private readonly IntPtr _foregroundHook;

    public event Action? Clicked;

    /// <summary>The strips currently on the taskbars.</summary>
    public IEnumerable<TaskbarBarWindow> Strips => _strips.Values;

    /// <summary>Raised with the monitor's device name and its new offset after a strip was dragged along its taskbar.</summary>
    public event Action<string, int>? OffsetChanged;

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

    /// <summary>Re-places the strips now, after the per-monitor settings changed, instead of on the next tick.</summary>
    public void Refresh()
    {
        if (_enabled)
            Update();
    }

    public void Dispose()
    {
        _timer.Stop();
        _settleTimer.Stop();
        UnhookWinEvent(_foregroundHook);
        CloseAll();
    }

    private void Update()
    {
        var taskbars = FindTaskbars();

        // Taskbars that are gone: a monitor unplugged, or Explorer restarting (the strips come
        // back with the new taskbars).
        foreach (var gone in _strips.Keys.Where(taskbar => !taskbars.Contains(taskbar)).ToList())
            Close(gone);

        var light = IsTaskbarLight();
        if (light != _lightTaskbar)
        {
            _lightTaskbar = light;
            foreach (var strip in _strips.Values)
                strip.UseLightTaskbar(light);
        }

        foreach (var taskbar in taskbars)
            UpdateStrip(taskbar, IsPrimary(taskbar));
    }

    private void UpdateStrip(IntPtr taskbar, bool isPrimary)
    {
        var monitor = WinForms.Screen.FromHandle(taskbar);
        var placement = _settingsStore.Current.TaskbarBarDisplays.GetValueOrDefault(monitor.DeviceName);
        if (placement is { Show: false } || !GetWindowRect(taskbar, out var taskbarRect))
        {
            Close(taskbar);
            return;
        }

        if (!_strips.TryGetValue(taskbar, out var strip))
        {
            strip = new TaskbarBarWindow { DataContext = _viewModel };
            strip.UseLightTaskbar(_lightTaskbar);
            strip.Clicked += () => Clicked?.Invoke();
            var created = strip;
            strip.DragStarted += () => _dragStartOffset = Placement(taskbar).Offset;
            strip.DragMoved += pixels => Drag(taskbar, created, pixels);
            strip.DragCompleted += () =>
            {
                _settingsStore.Save();
                OffsetChanged?.Invoke(WinForms.Screen.FromHandle(taskbar).DeviceName, Placement(taskbar).Offset);
            };
            strip.Closed += (_, _) =>
            {
                if (_strips.TryGetValue(taskbar, out var current) && current == created)
                    _strips.Remove(taskbar);
            };
            new WindowInteropHelper(strip).EnsureHandle();
            _strips[taskbar] = strip;
        }

        var bounds = Drawing.Rectangle.FromLTRB(taskbarRect.Left, taskbarRect.Top, taskbarRect.Right, taskbarRect.Bottom);
        var screen = monitor.Bounds;

        // Only a horizontal taskbar has room for it. An auto-hidden one is parked almost entirely
        // off screen, and a full-screen app covers the taskbar: unless the user wants the strip
        // over that app, it must not be left floating (being topmost, it would stay above what
        // covers the taskbar).
        var horizontal = bounds.Width > bounds.Height;
        var onScreen = Drawing.Rectangle.Intersect(bounds, screen).Height >= bounds.Height / 2;
        var covered = IsCoveredByFullScreenWindow(taskbar, new WindowInteropHelper(strip).Handle, bounds, screen);
        if (!horizontal || !onScreen || (covered && !_settingsStore.Current.ShowTaskbarBarOverFullScreen))
        {
            strip.Hide();
            return;
        }

        strip.OverFullScreen = covered;
        var settings = _settingsStore.Current;
        strip.RowsMode = settings.TaskbarBarRows;
        strip.LabelMode = settings.TaskbarBarLabels;
        strip.ShowResetTime = settings.TaskbarBarShowResetTime;
        strip.ShowMascot = settings.TaskbarBarShowMascot;
        strip.MascotAnimation = settings.MascotAnimation;

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

        var offset = Math.Clamp(placement?.Offset ?? 0, AppSettings.MinTaskbarBarOffset, AppSettings.MaxTaskbarBarOffset);
        var width = (int)Math.Round(strip.ActualWidth * dpi.DpiScaleX);
        var x = right - width + (int)Math.Round((offset - GapToNotificationArea) * dpi.DpiScaleX);
        // However far it's shifted, it stays on its taskbar.
        x = Math.Max(bounds.Left, Math.Min(x, bounds.Right - width));
        // The taskbar is topmost too and goes to the front of the topmost windows whenever it's
        // used, over the strip, so the strip is put back in front of it. Only when it has to be:
        // raising it every time would also put it over a menu or tooltip that overlaps it. Over a
        // full-screen app there's no taskbar to be in front of, and it's raised regardless.
        var handle = new WindowInteropHelper(strip).Handle;
        var raise = covered || IsAbove(taskbar, handle);
        SetWindowPos(handle, raise ? HwndTopmost : IntPtr.Zero, x, bounds.Top, 0, 0,
            SwpNoSize | SwpNoActivate | (raise ? 0 : SwpNoZOrder));
    }

    /// <summary>Whether <paramref name="window"/> is in front of <paramref name="other"/> in the z-order.</summary>
    private static bool IsAbove(IntPtr window, IntPtr other)
    {
        for (var above = GetWindow(other, GwHwndPrev); above != IntPtr.Zero; above = GetWindow(above, GwHwndPrev))
        {
            if (above == window)
                return true;
        }
        return false;
    }

    // The offset the strip being dragged had when the drag started.
    private int _dragStartOffset;

    // Dragging a strip sets its monitor's offset, the same setting as the slider in the settings
    // window. Kept to even numbers, the slider's steps.
    private void Drag(IntPtr taskbar, TaskbarBarWindow strip, int pixels)
    {
        var placement = Placement(taskbar);
        var moved = (int)Math.Round(pixels / VisualTreeHelper.GetDpi(strip).DpiScaleX / 2) * 2;
        var offset = Math.Clamp(_dragStartOffset + moved, AppSettings.MinTaskbarBarOffset, AppSettings.MaxTaskbarBarOffset);
        if (offset == placement.Offset)
            return;

        placement.Offset = offset;
        UpdateStrip(taskbar, IsPrimary(taskbar));
    }

    /// <summary>The settings of this taskbar's monitor, added (as the defaults) if it has none yet.</summary>
    private TaskbarBarDisplaySettings Placement(IntPtr taskbar)
    {
        var displays = _settingsStore.Current.TaskbarBarDisplays;
        var device = WinForms.Screen.FromHandle(taskbar).DeviceName;
        if (!displays.TryGetValue(device, out var placement))
            displays[device] = placement = new TaskbarBarDisplaySettings();
        return placement;
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

    private static bool IsPrimary(IntPtr taskbar)
    {
        var className = new StringBuilder(64);
        GetClassName(taskbar, className, className.Capacity);
        return className.ToString() == PrimaryTaskbarClass;
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

    // What's drawn in the middle of the taskbar isn't the taskbar but a window filling its whole
    // monitor (a game, a video, a slide show) — whether or not that window is the one in use: a
    // full-screen game keeps covering the taskbar while another monitor has the focus.
    private static bool IsCoveredByFullScreenWindow(IntPtr taskbar, IntPtr strip, Drawing.Rectangle bounds, Drawing.Rectangle screen)
    {
        var middle = new Point { X = bounds.Left + bounds.Width / 2, Y = bounds.Top + bounds.Height / 2 };
        var top = GetAncestor(WindowFromPoint(middle), GaRoot);
        // Shifted far enough, the strip itself is what's in the middle: look beside it instead.
        if (top == strip)
            top = GetAncestor(WindowFromPoint(new Point { X = bounds.Left + 8, Y = middle.Y }), GaRoot);
        if (top == IntPtr.Zero || top == taskbar || !GetWindowRect(top, out var rect))
            return false;
        return rect.Left <= screen.Left && rect.Top <= screen.Top && rect.Right >= screen.Right && rect.Bottom >= screen.Bottom;
    }

    private static readonly IntPtr HwndTopmost = new(-1);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string? windowName);

    private const uint EventSystemForeground = 0x0003;
    private const uint WineventOutOfContext = 0x0000;

    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hWnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc callback, uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hook);

    private const uint GwHwndPrev = 3;

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint command);

    private const uint GaRoot = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X, Y;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);
}
