using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using ClaudeUsageTracker.App.Localization;
using ClaudeUsageTracker.Core.History;

namespace ClaudeUsageTracker.App.Views;

/// <summary>
/// The flyout's history chart: the session and weekly percentages over the last <see cref="Span"/>,
/// as two lines on one 0–100% axis. The line above the plot is the legend, and while the cursor is
/// over the plot, the values at that moment instead. The two series differ in color and in stroke
/// (the weekly line is dashed), so they can be told apart without relying on color alone; all text
/// is drawn in the text colors, never in a series color.
/// </summary>
public sealed class UsageHistoryChart : FrameworkElement
{
    private const double HeaderHeight = 18;
    private const double AxisHeight = 15;
    private const double LeftGutter = 32;
    private const double RightPadding = 6;
    private const double FontSize = 11;
    private const double SwatchWidth = 12;

    public static readonly DependencyProperty SamplesProperty = Register<IReadOnlyList<UsageSample>?>(nameof(Samples), null);
    public static readonly DependencyProperty SpanProperty = Register(nameof(Span), TimeSpan.FromDays(1));
    public static readonly DependencyProperty SessionBrushProperty = Register<Brush?>(nameof(SessionBrush), null);
    public static readonly DependencyProperty WeeklyBrushProperty = Register<Brush?>(nameof(WeeklyBrush), null);
    public static readonly DependencyProperty GridBrushProperty = Register<Brush?>(nameof(GridBrush), null);
    public static readonly DependencyProperty TextBrushProperty = Register<Brush?>(nameof(TextBrush), null);
    public static readonly DependencyProperty MutedTextBrushProperty = Register<Brush?>(nameof(MutedTextBrush), null);
    public static readonly DependencyProperty SurfaceBrushProperty = Register<Brush?>(nameof(SurfaceBrush), null);
    public static readonly DependencyProperty SessionLabelProperty = Register(nameof(SessionLabel), string.Empty);
    public static readonly DependencyProperty WeeklyLabelProperty = Register(nameof(WeeklyLabel), string.Empty);
    public static readonly DependencyProperty EmptyTextProperty = Register(nameof(EmptyText), string.Empty);

    // Where the cursor is over the chart, or null when it isn't.
    private Point? _hover;

    /// <summary>All recorded samples, oldest first; the chart shows the ones within <see cref="Span"/> of now.</summary>
    public IReadOnlyList<UsageSample>? Samples
    {
        get => (IReadOnlyList<UsageSample>?)GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    public TimeSpan Span
    {
        get => (TimeSpan)GetValue(SpanProperty);
        set => SetValue(SpanProperty, value);
    }

    public Brush? SessionBrush
    {
        get => (Brush?)GetValue(SessionBrushProperty);
        set => SetValue(SessionBrushProperty, value);
    }

    public Brush? WeeklyBrush
    {
        get => (Brush?)GetValue(WeeklyBrushProperty);
        set => SetValue(WeeklyBrushProperty, value);
    }

    public Brush? GridBrush
    {
        get => (Brush?)GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    public Brush? TextBrush
    {
        get => (Brush?)GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }

    public Brush? MutedTextBrush
    {
        get => (Brush?)GetValue(MutedTextBrushProperty);
        set => SetValue(MutedTextBrushProperty, value);
    }

    /// <summary>The color behind the chart, ringed around the hover markers so they stand off the lines.</summary>
    public Brush? SurfaceBrush
    {
        get => (Brush?)GetValue(SurfaceBrushProperty);
        set => SetValue(SurfaceBrushProperty, value);
    }

    public string SessionLabel
    {
        get => (string)GetValue(SessionLabelProperty);
        set => SetValue(SessionLabelProperty, value);
    }

    public string WeeklyLabel
    {
        get => (string)GetValue(WeeklyLabelProperty);
        set => SetValue(WeeklyLabelProperty, value);
    }

    /// <summary>Shown in place of the lines until there are two samples to draw one between.</summary>
    public string EmptyText
    {
        get => (string)GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    private static DependencyProperty Register<T>(string name, T defaultValue) => DependencyProperty.Register(
        name, typeof(T), typeof(UsageHistoryChart),
        new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender));

    protected override void OnMouseMove(MouseEventArgs e)
    {
        _hover = e.GetPosition(this);
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        _hover = null;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var plot = new Rect(LeftGutter, HeaderHeight,
            Math.Max(0, ActualWidth - LeftGutter - RightPadding), Math.Max(0, ActualHeight - HeaderHeight - AxisHeight));
        if (plot.Width <= 0 || plot.Height <= 0)
            return;

        // Without a fill the gaps between the lines wouldn't see the mouse.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

        var to = DateTimeOffset.Now;
        var from = to - Span;
        double X(DateTimeOffset time) => plot.Left + (time - from) / Span * plot.Width;
        double Y(double percentage) => plot.Bottom - Math.Clamp(percentage, 0, 100) / 100 * plot.Height;

        DrawAxes(dc, plot, from, to);

        // The sample just before the range is kept, so the line enters from the left edge
        // instead of starting at the first sample inside.
        var samples = Samples ?? [];
        var first = 0;
        while (first < samples.Count && samples[first].Time < from)
            first++;
        var start = Math.Max(0, first - 1);

        if (samples.Count - first < 2)
        {
            var empty = Text(EmptyText, MutedTextBrush);
            dc.DrawText(empty, new Point(plot.Left + (plot.Width - empty.Width) / 2, plot.Top + (plot.Height - empty.Height) / 2));
            DrawHeader(dc, null);
            return;
        }

        dc.PushClip(new RectangleGeometry(plot));
        DrawSeries(dc, samples, start, sample => sample.Weekly, WeeklyPen(), X, Y);
        DrawSeries(dc, samples, start, sample => sample.Session, SessionPen(), X, Y);
        dc.Pop();

        UsageSample? hovered = null;
        if (_hover is { } cursor && cursor.X >= plot.Left - 4 && cursor.X <= plot.Right + 4)
        {
            // The sample nearest the cursor in time, among those inside the range.
            var nearest = samples[first];
            for (var i = first + 1; i < samples.Count; i++)
            {
                if (Math.Abs(X(samples[i].Time) - cursor.X) < Math.Abs(X(nearest.Time) - cursor.X))
                    nearest = samples[i];
            }

            hovered = nearest;
            var x = X(nearest.Time);
            dc.DrawLine(new Pen(MutedTextBrush, 1), new Point(x, plot.Top), new Point(x, plot.Bottom));
            DrawMarker(dc, new Point(x, Y(nearest.Weekly)), WeeklyBrush);
            DrawMarker(dc, new Point(x, Y(nearest.Session)), SessionBrush);
        }

        DrawHeader(dc, hovered);
    }

    // Recessive: hairlines at 0 / 50 / 100% with their values, and the times of the two ends and the middle.
    private void DrawAxes(DrawingContext dc, Rect plot, DateTimeOffset from, DateTimeOffset to)
    {
        var gridPen = new Pen(GridBrush, 1);
        foreach (var percentage in (int[])[0, 50, 100])
        {
            var y = Math.Round(plot.Bottom - percentage / 100.0 * plot.Height) + 0.5;
            dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            var label = Text($"{percentage}%", MutedTextBrush);
            dc.DrawText(label, new Point(plot.Left - 6 - label.Width, y - label.Height / 2));
        }

        var format = Span > TimeSpan.FromDays(1) ? "M/d" : "H:mm";
        var middle = from + (to - from) / 2;
        var texts = ((DateTimeOffset[])[from, middle, to]).Select(time => Text(time.ToLocalTime().ToString(format, Loc.Culture), MutedTextBrush)).ToArray();
        var top = plot.Bottom + 2;
        dc.DrawText(texts[0], new Point(plot.Left, top));
        dc.DrawText(texts[1], new Point(plot.Left + (plot.Width - texts[1].Width) / 2, top));
        dc.DrawText(texts[2], new Point(plot.Right - texts[2].Width, top));
    }

    // One line per run of samples: across a gap nothing was measured, so nothing is drawn.
    private static void DrawSeries(
        DrawingContext dc, IReadOnlyList<UsageSample> samples, int start, Func<UsageSample, double> value, Pen pen,
        Func<DateTimeOffset, double> x, Func<double, double> y)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            for (var i = start; i < samples.Count; i++)
            {
                var point = new Point(x(samples[i].Time), y(value(samples[i])));
                if (i == start || samples[i].Time - samples[i - 1].Time > UsageHistory.GapThreshold)
                    context.BeginFigure(point, isFilled: false, isClosed: false);
                else
                    context.LineTo(point, isStroked: true, isSmoothJoin: true);
            }
        }

        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    private void DrawMarker(DrawingContext dc, Point center, Brush? brush)
    {
        dc.DrawEllipse(SurfaceBrush, null, center, 6, 6);
        dc.DrawEllipse(brush, null, center, 4, 4);
    }

    // The legend; with a hovered sample, its time and each series' value at it.
    private void DrawHeader(DrawingContext dc, UsageSample? hovered)
    {
        var x = LeftGutter;
        if (hovered is { } sample)
        {
            var format = Span > TimeSpan.FromDays(1) ? "M/d H:mm" : "H:mm";
            var time = Text(sample.Time.ToLocalTime().ToString(format, Loc.Culture), MutedTextBrush);
            dc.DrawText(time, new Point(x, 0));
            x += time.Width + 10;
        }

        x = DrawLegendEntry(dc, x, SessionPen(), SessionLabel, hovered?.Session);
        DrawLegendEntry(dc, x, WeeklyPen(), WeeklyLabel, hovered?.Weekly);
    }

    private double DrawLegendEntry(DrawingContext dc, double x, Pen pen, string label, double? value)
    {
        var text = Text(value is { } percentage ? $"{label} {percentage:0.#}%" : label, value is null ? MutedTextBrush : TextBrush);
        var middle = Math.Round(text.Height / 2) + 1;
        dc.DrawLine(pen, new Point(x, middle), new Point(x + SwatchWidth, middle));
        dc.DrawText(text, new Point(x + SwatchWidth + 4, 0));
        return x + SwatchWidth + 4 + text.Width + 12;
    }

    private Pen SessionPen() => new(SessionBrush, 2) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

    private Pen WeeklyPen() => new(WeeklyBrush, 2) { LineJoin = PenLineJoin.Round, DashStyle = new DashStyle([2.5, 1.5], 0) };

    private FormattedText Text(string text, Brush? brush) => new(
        text, Loc.Culture, FlowDirection.LeftToRight,
        new Typeface(TextElement.GetFontFamily(this), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
        FontSize, brush ?? Brushes.Gray, VisualTreeHelper.GetDpi(this).PixelsPerDip);
}
