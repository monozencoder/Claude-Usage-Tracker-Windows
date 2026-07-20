using System.Windows;
using WinForms = System.Windows.Forms;

namespace ClaudeUsageTracker.App.Services;

/// <summary>
/// Positions a borderless window near the current cursor position, clamped to the
/// work area of whichever monitor the cursor is on. WPF has no first-class "anchor
/// to tray icon" API, so this uses the cursor position at click time instead of
/// trying to query the tray icon's screen rect (which isn't reliably available).
/// </summary>
public static class WindowPositioner
{
    public static void PositionNearCursor(Window window, double margin = 8)
    {
        var cursor = WinForms.Cursor.Position; // device pixels
        var workArea = WinForms.Screen.FromPoint(cursor).WorkingArea; // device pixels

        var source = PresentationSource.FromVisual(window);
        var dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        var dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

        double ToDipX(double px) => px / dpiX;
        double ToDipY(double py) => py / dpiY;

        var workLeft = ToDipX(workArea.Left);
        var workTop = ToDipY(workArea.Top);
        var workRight = ToDipX(workArea.Right);
        var workBottom = ToDipY(workArea.Bottom);

        var cursorX = ToDipX(cursor.X);
        var cursorY = ToDipY(cursor.Y);

        var left = cursorX - window.ActualWidth / 2;
        var top = cursorY - window.ActualHeight - margin;

        // Taskbar (and therefore the tray icon) may be at the top of the screen —
        // if placing above the cursor would go off the top of the work area, place below instead.
        if (top < workTop)
            top = cursorY + margin;

        left = Math.Clamp(left, workLeft + margin, Math.Max(workLeft + margin, workRight - window.ActualWidth - margin));
        top = Math.Clamp(top, workTop + margin, Math.Max(workTop + margin, workBottom - window.ActualHeight - margin));

        window.Left = left;
        window.Top = top;
    }
}
