using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using ClaudeUsageTracker.App.Settings;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;
using static ClaudeUsageTracker.App.Interop.NativeMethods;

namespace ClaudeUsageTracker.App.Views;

/// <summary>
/// Places the borderless flyout: by default in the tray corner of the taskbar's monitor, like
/// Windows 11 Quick Settings (bottom-right above a bottom taskbar, top-right below a top one,
/// and so on), so it opens in the same spot however it's opened. Also restores a saved
/// position, keeping it on a monitor that still exists.
/// </summary>
public static class WindowPositioner
{
    /// <summary>Which screen edge the taskbar is on.</summary>
    public enum TaskbarEdge { Left, Top, Right, Bottom }

    /// <summary>
    /// Puts the window's visible content in the tray corner of the work area, <paramref name="margin"/>
    /// DIPs in from both edges, and returns the taskbar's edge (so the caller can animate away from it).
    /// </summary>
    /// <param name="contentInset">
    /// Transparent space between the window edge and its visible content (e.g. room for a
    /// drop shadow). Placement applies to the visible content, not the window.
    /// </param>
    public static TaskbarEdge PositionAtTrayCorner(Window window, Thickness contentInset = default, double margin = 12)
    {
        var (dpiX, dpiY) = GetDpi(window);
        var content = ContentSize(window, contentInset, dpiX, dpiY);
        var marginX = (int)Math.Round(margin * dpiX);
        var marginY = (int)Math.Round(margin * dpiY);

        var data = new AppBarData { Size = Marshal.SizeOf<AppBarData>() };
        var found = SHAppBarMessage(AbmGetTaskbarPos, ref data) != IntPtr.Zero;
        var edge = found ? (TaskbarEdge)data.Edge : TaskbarEdge.Bottom;
        var taskbar = Drawing.Rectangle.FromLTRB(data.Rect.Left, data.Rect.Top, data.Rect.Right, data.Rect.Bottom);
        var screen = found ? WinForms.Screen.FromRectangle(taskbar) : WinForms.Screen.PrimaryScreen!;
        var area = screen.WorkingArea;

        // An auto-hiding taskbar isn't excluded from the work area but still pops up over it, so
        // stay clear of whichever reaches further in: the work area edge or the taskbar itself.
        var left = edge == TaskbarEdge.Left && found ? Math.Max(area.Left, taskbar.Right) : area.Left;
        var top = edge == TaskbarEdge.Top && found ? Math.Max(area.Top, taskbar.Bottom) : area.Top;
        var right = edge == TaskbarEdge.Right && found ? Math.Min(area.Right, taskbar.Left) : area.Right;
        var bottom = edge == TaskbarEdge.Bottom && found ? Math.Min(area.Bottom, taskbar.Top) : area.Bottom;

        // The tray sits at the right end of a horizontal taskbar and the bottom of a vertical one.
        var x = edge == TaskbarEdge.Left ? left + marginX : right - content.Width - marginX;
        var y = edge == TaskbarEdge.Top ? top + marginY : bottom - content.Height - marginY;

        MoveContentTo(window, contentInset, dpiX, dpiY, x, y);
        return edge;
    }

    /// <summary>The window's top-left in screen pixels.</summary>
    public static ScreenPoint GetPosition(Window window)
    {
        GetWindowRect(new WindowInteropHelper(window).Handle, out var rect);
        return new ScreenPoint(rect.Left, rect.Top);
    }

    /// <summary>Moves the window's top-left to <paramref name="position"/> (screen pixels) as-is, on screen or not.</summary>
    public static void MoveTo(Window window, ScreenPoint position)
        => SetWindowPos(new WindowInteropHelper(window).Handle, IntPtr.Zero, position.X, position.Y,
            0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);

    /// <summary>
    /// Moves the window's top-left to <paramref name="position"/> (screen pixels), then keeps its
    /// visible content fully on screen: if that spot is off every monitor — say the monitor it was
    /// saved on has been unplugged or the resolution dropped — the content is moved onto the work
    /// area of the nearest monitor (the same rule Windows applies when restoring window placement).
    /// </summary>
    public static void PositionAt(Window window, ScreenPoint position, Thickness contentInset = default)
    {
        var (dpiX, dpiY) = GetDpi(window);
        var size = ContentSize(window, contentInset, dpiX, dpiY);
        var content = new Drawing.Rectangle(
            position.X + (int)Math.Round(contentInset.Left * dpiX),
            position.Y + (int)Math.Round(contentInset.Top * dpiY),
            size.Width,
            size.Height);

        // Screen.FromRectangle is MonitorFromRect(MONITOR_DEFAULTTONEAREST): the monitor the content
        // overlaps most, or the closest one if it overlaps none.
        var workArea = WinForms.Screen.FromRectangle(content).WorkingArea;
        var x = Math.Clamp(content.X, workArea.Left, Math.Max(workArea.Left, workArea.Right - content.Width));
        var y = Math.Clamp(content.Y, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - content.Height));

        MoveContentTo(window, contentInset, dpiX, dpiY, x, y);
    }

    /// <summary>Pulls the window back onto a monitor if it's (partly) off screen, e.g. after a display change.</summary>
    public static void KeepOnScreen(Window window, Thickness contentInset = default)
        => PositionAt(window, GetPosition(window), contentInset);

    private static (double X, double Y) GetDpi(Window window)
    {
        var transform = PresentationSource.FromVisual(window)?.CompositionTarget?.TransformToDevice;
        return (transform?.M11 ?? 1.0, transform?.M22 ?? 1.0);
    }

    /// <summary>Size of the visible content (window minus the transparent inset), in pixels.</summary>
    private static Drawing.Size ContentSize(Window window, Thickness contentInset, double dpiX, double dpiY) => new(
        (int)Math.Round((window.ActualWidth - contentInset.Left - contentInset.Right) * dpiX),
        (int)Math.Round((window.ActualHeight - contentInset.Top - contentInset.Bottom) * dpiY));

    /// <summary>Moves the window so its visible content's top-left lands on (x, y) in screen pixels.</summary>
    private static void MoveContentTo(Window window, Thickness contentInset, double dpiX, double dpiY, int x, int y)
        => SetWindowPos(new WindowInteropHelper(window).Handle, IntPtr.Zero,
            x - (int)Math.Round(contentInset.Left * dpiX), y - (int)Math.Round(contentInset.Top * dpiY),
            0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);

    private const uint AbmGetTaskbarPos = 0x00000005;

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public int Size;
        public IntPtr Hwnd;
        public uint CallbackMessage;
        public uint Edge; // ABE_LEFT / TOP / RIGHT / BOTTOM = 0..3, same order as TaskbarEdge
        public NativeRect Rect;
        public IntPtr LParam;
    }

    [DllImport("shell32.dll")]
    private static extern IntPtr SHAppBarMessage(uint message, ref AppBarData data);
}
