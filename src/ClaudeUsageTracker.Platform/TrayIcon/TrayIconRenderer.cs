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
    Number,

    /// <summary>The session usage as a number, in a color for the status, on the body of a small orange pixel-art creature.</summary>
    NumberOnCreature
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
                    DrawNumber(g, content.SessionPercentage, ColorForStatus(content.SessionStatus), new RectangleF(0, 0, sizePx, sizePx), 1f);
                    break;
                case TrayIconStyle.NumberOnCreature:
                    DrawCreature(g, content.SessionPercentage, content.SessionStatus, sizePx);
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

    // An orange pixel-art creature: a wide body with two square eyes, a stubby arm on each
    // side, and four legs in two pairs. The number goes under the eyes, colored by status, and to
    // be legible at the tray's 16px it needs most of the icon: so the body is taller than the
    // creature's would be, and the eyes sit right at its top.
    // Drawn on a 16 x 16 grid of blocks, so at 16px every block is exactly one pixel.
    private static void DrawCreature(Graphics g, double percentage, UsageStatusLevel status, int sizePx)
    {
        var unit = sizePx / 16f;
        var state = g.Save();
        g.SmoothingMode = SmoothingMode.None;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        void Block(Brush brush, int x, int y, int width, int height) => g.FillRectangle(brush, x * unit, y * unit, width * unit, height * unit);

        using (var body = new SolidBrush(CreatureColor))
        {
            Block(body, 2, 1, 12, 12);
            Block(body, 0, 5, 2, 3); // arms
            Block(body, 14, 5, 2, 3);
            foreach (var x in (int[])[2, 5, 10, 13])
                Block(body, x, 13, 1, 2); // legs: a pair under each side, a gap in the middle
        }
        Block(Brushes.Black, 4, 2, 1, 1); // eyes
        Block(Brushes.Black, 11, 2, 1, 1);
        g.Restore(state);

        DrawNumber(g, percentage, NumberColorOnCreature(status), new RectangleF(2 * unit, 3 * unit, 12 * unit, 10 * unit), 0.98f);
    }

    // The number fills the given area, as large as its digits allow: three ("100") have to be
    // smaller than two to fit, and one can afford to be a little larger.
    private static void DrawNumber(Graphics g, double percentage, Color color, RectangleF area, float scale)
    {
        var text = ((int)Math.Round(Math.Clamp(percentage, 0, 100))).ToString(CultureInfo.InvariantCulture);
        var emSize = area.Width * scale * (text.Length switch { 1 => 0.84f, 2 => 0.78f, _ => 0.54f });

        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var font = new Font("Segoe UI", emSize, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(color);
        // Typographic: without GDI+'s default side padding, which would push the digits off-center and apart.
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        g.DrawString(text, font, brush, area, format);
    }

    private static readonly Color CreatureColor = Color.FromArgb(255, 217, 119, 87);

    // The status as the number's color where the background is the creature's orange, on which
    // the usual green / orange / red would be anywhere from hard to read to invisible.
    private static Color NumberColorOnCreature(UsageStatusLevel status) => status switch
    {
        UsageStatusLevel.Moderate => Color.FromArgb(255, 255, 236, 110),
        UsageStatusLevel.Critical => Color.FromArgb(255, 110, 0, 0),
        _ => Color.White
    };

    private static Color ColorForStatus(UsageStatusLevel status) => status switch
    {
        UsageStatusLevel.Safe => Color.FromArgb(255, 52, 199, 89),
        UsageStatusLevel.Moderate => Color.FromArgb(255, 255, 149, 0),
        UsageStatusLevel.Critical => Color.FromArgb(255, 255, 59, 48),
        _ => Color.Gray
    };
}
