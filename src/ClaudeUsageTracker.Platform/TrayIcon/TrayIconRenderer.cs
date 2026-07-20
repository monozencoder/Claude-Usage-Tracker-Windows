using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using ClaudeUsageTracker.Core.Models;

namespace ClaudeUsageTracker.Platform.TrayIcon;

/// <summary>
/// Renders a small ring-gauge tray icon (percentage-used, color by status) at
/// runtime. Deliberately simple for v1 — no text (unreadable at 16-20px tray
/// sizes) and a single style, unlike the macOS app's 5-style/3-color-mode system.
/// </summary>
public sealed class TrayIconRenderer
{
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    /// <summary>
    /// Renders a colored ring gauge. Caller owns the returned <see cref="Icon"/> and
    /// must Dispose() it (which frees its underlying HICON) once it stops using it —
    /// notably, before/after assigning a new icon so the previous one isn't leaked.
    /// </summary>
    public Icon Render(double percentage, UsageStatusLevel status, int sizePx = 32)
    {
        using var bitmap = new Bitmap(sizePx, sizePx);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var strokeWidth = Math.Max(2f, sizePx / 8f);
            var rect = new RectangleF(strokeWidth / 2, strokeWidth / 2, sizePx - strokeWidth, sizePx - strokeWidth);

            using var trackPen = new Pen(Color.FromArgb(60, 128, 128, 128), strokeWidth);
            g.DrawEllipse(trackPen, rect);

            var sweepAngle = 360f * (float)Math.Clamp(percentage, 0, 100) / 100f;
            if (sweepAngle > 0)
            {
                using var progressPen = new Pen(ColorForStatus(status), strokeWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawArc(progressPen, rect, -90, sweepAngle);
            }
        }

        var hIcon = bitmap.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(hIcon);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    private static Color ColorForStatus(UsageStatusLevel status) => status switch
    {
        UsageStatusLevel.Safe => Color.FromArgb(255, 52, 199, 89),
        UsageStatusLevel.Moderate => Color.FromArgb(255, 255, 149, 0),
        UsageStatusLevel.Critical => Color.FromArgb(255, 255, 59, 48),
        _ => Color.Gray
    };
}
