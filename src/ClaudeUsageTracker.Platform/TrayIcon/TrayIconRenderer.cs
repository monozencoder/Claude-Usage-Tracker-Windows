using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using ClaudeUsageTracker.Core.Models;

namespace ClaudeUsageTracker.Platform.TrayIcon;

/// <summary>What the tray icon draws.</summary>
public enum TrayIconStyle
{
    /// <summary>A ring gauge of the session usage.</summary>
    Ring,

    /// <summary>The session ring with a second, smaller ring of the weekly usage inside it.</summary>
    DoubleRing,

    /// <summary>The session usage as a number.</summary>
    Number
}

/// <summary>The usage the tray icon shows: each window's percentage used and the status that colors it.</summary>
public readonly record struct TrayIconContent(
    double SessionPercentage, UsageStatusLevel SessionStatus, double WeeklyPercentage, UsageStatusLevel WeeklyStatus);

/// <summary>
/// Renders the tray icon at runtime in one of the <see cref="TrayIconStyle"/>s, colored by
/// status. Unlike the macOS app's 5-style/3-color-mode system, deliberately few and simple:
/// a tray icon is 16-20px, where little more than a ring or two digits stays legible.
/// </summary>
public sealed class TrayIconRenderer
{
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    /// <summary>
    /// Renders the icon. Caller owns the returned <see cref="Icon"/> and
    /// must Dispose() it (which frees its underlying HICON) once it stops using it —
    /// notably, before/after assigning a new icon so the previous one isn't leaked.
    /// </summary>
    public Icon Render(TrayIconContent content, TrayIconStyle style = TrayIconStyle.Ring, int sizePx = 32)
    {
        using var bitmap = new Bitmap(sizePx, sizePx);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var strokeWidth = Math.Max(2f, sizePx / 8f);
            var outer = new RectangleF(strokeWidth / 2, strokeWidth / 2, sizePx - strokeWidth, sizePx - strokeWidth);
            switch (style)
            {
                case TrayIconStyle.Number:
                    DrawNumber(g, content.SessionPercentage, content.SessionStatus, sizePx);
                    break;
                case TrayIconStyle.DoubleRing:
                    DrawRing(g, outer, strokeWidth, content.SessionPercentage, content.SessionStatus);
                    // Inside the outer ring, a sliver of a gap apart so the two read as separate.
                    var inset = strokeWidth + Math.Max(1f, sizePx / 16f);
                    DrawRing(g, RectangleF.Inflate(outer, -inset, -inset), strokeWidth, content.WeeklyPercentage, content.WeeklyStatus);
                    break;
                default:
                    DrawRing(g, outer, strokeWidth, content.SessionPercentage, content.SessionStatus);
                    break;
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

    private static void DrawRing(Graphics g, RectangleF rect, float strokeWidth, double percentage, UsageStatusLevel status)
    {
        using var trackPen = new Pen(Color.FromArgb(60, 128, 128, 128), strokeWidth);
        g.DrawEllipse(trackPen, rect);

        var sweepAngle = 360f * (float)Math.Clamp(percentage, 0, 100) / 100f;
        if (sweepAngle > 0)
        {
            using var progressPen = new Pen(ColorForStatus(status), strokeWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(progressPen, rect, -90, sweepAngle);
        }
    }

    // The whole icon is the number, as large as its digits allow: three ("100") have to be
    // smaller than two to fit, and one can afford to be a little larger.
    private static void DrawNumber(Graphics g, double percentage, UsageStatusLevel status, int sizePx)
    {
        var text = ((int)Math.Round(Math.Clamp(percentage, 0, 100))).ToString(CultureInfo.InvariantCulture);
        var emSize = sizePx * (text.Length switch { 1 => 0.84f, 2 => 0.78f, _ => 0.54f });

        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var font = new Font("Segoe UI", emSize, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(ColorForStatus(status));
        // Typographic: without GDI+'s default side padding, which would push the digits off-center and apart.
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        g.DrawString(text, font, brush, new RectangleF(0, 0, sizePx, sizePx), format);
    }

    private static Color ColorForStatus(UsageStatusLevel status) => status switch
    {
        UsageStatusLevel.Safe => Color.FromArgb(255, 52, 199, 89),
        UsageStatusLevel.Moderate => Color.FromArgb(255, 255, 149, 0),
        UsageStatusLevel.Critical => Color.FromArgb(255, 255, 59, 48),
        _ => Color.Gray
    };
}
