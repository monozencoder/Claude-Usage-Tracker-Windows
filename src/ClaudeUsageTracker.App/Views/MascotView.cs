using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ClaudeUsageTracker.App.Settings;
using ClaudeUsageTracker.App.ViewModels;

namespace ClaudeUsageTracker.App.Views;

/// <summary>
/// The pixel-art creature that sits beside the taskbar bars and acts out how much usage is left:
/// hopping after a reset, blinking while there's room, sweating as it runs short, flailing near
/// the limit, slumped once it's used up.
/// <para>
/// Drawn in code, block by block, on a grid of <see cref="Columns"/> x <see cref="Rows"/> blocks
/// of <see cref="Block"/> units each. The body starts <see cref="BodyTop"/> rows down, leaving
/// headroom for raised arms and flying sweat, and there's a spare column either side for shaking.
/// </para>
/// <para>
/// Animation is frame by frame, a few frames a second, in a loop of <see cref="LoopFrames"/>.
/// How much it moves is the user's choice (<see cref="MascotAnimation"/>). Subtle, the default,
/// isn't continuous — something moving all the time on the taskbar pulls the eye: a mood plays
/// its loop twice when it sets in and then once a minute, resting in a still pose in between,
/// and the calm mood only blinks. Lively never rests: every mood loops, a little faster, and the
/// calm one gets a longer loop of its own in which it also glances about and hops.
/// </para>
/// </summary>
public sealed class MascotView : FrameworkElement
{
    private const double Block = 2;
    private const int Columns = 26;
    private const int Rows = 20;
    private const int BodyTop = 4;

    private const int LoopFrames = 16;
    private const int BurstFrames = LoopFrames * 2;
    private const int CycleFrames = 240;   // a burst a minute
    private const int BlinkEvery = 24;     // the calm mood's blink, every six seconds
    private const int BlinkFrame = 11;
    private const int CalmLoopFrames = 48; // the calm mood's own loop when lively: blinks, glances, a couple of hops

    private static readonly Brush Body = Frozen(Color.FromRgb(217, 119, 87));
    private static readonly Brush Dark = Frozen(Color.FromRgb(30, 20, 18));
    private static readonly Brush Sweat = Frozen(Color.FromRgb(110, 190, 255));

    public static readonly DependencyProperty MoodProperty = DependencyProperty.Register(
        nameof(Mood), typeof(MascotMood), typeof(MascotView),
        new FrameworkPropertyMetadata(MascotMood.Calm, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((MascotView)d).Restart()));

    public static readonly DependencyProperty AnimationProperty = DependencyProperty.Register(
        nameof(Animation), typeof(MascotAnimation), typeof(MascotView),
        new FrameworkPropertyMetadata(MascotAnimation.Subtle, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((MascotView)d).Restart()));

    private static readonly TimeSpan SubtleFrameTime = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan LivelyFrameTime = TimeSpan.FromMilliseconds(180);

    private readonly DispatcherTimer _timer = new();
    private int _tick;
    private int? _drawnFrame;

    public MascotView()
    {
        SnapsToDevicePixels = true;
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);

        _timer.Tick += (_, _) =>
        {
            _tick++;
            // Most ticks fall in a rest and change nothing: only a new frame is worth a redraw.
            if (Frame() != _drawnFrame)
                InvalidateVisual();
        };
        // Nothing runs while it can't be seen: hidden, or its window hidden behind a full-screen app.
        IsVisibleChanged += (_, _) => Restart();
    }

    public MascotMood Mood
    {
        get => (MascotMood)GetValue(MoodProperty);
        set => SetValue(MoodProperty, value);
    }

    /// <summary>How much it moves; off, it holds each mood's still pose.</summary>
    public MascotAnimation Animation
    {
        get => (MascotAnimation)GetValue(AnimationProperty);
        set => SetValue(AnimationProperty, value);
    }

    // From the top of a cycle, so a mood plays as soon as it sets in.
    private void Restart()
    {
        _tick = 0;
        _timer.Interval = Animation == MascotAnimation.Lively ? LivelyFrameTime : SubtleFrameTime;
        _timer.IsEnabled = Animation != MascotAnimation.Off && IsVisible;
    }

    /// <summary>The frame of the loop to draw now, or null for the mood's still pose.</summary>
    private int? Frame()
    {
        switch (Animation)
        {
            case MascotAnimation.Off:
                return null;
            case MascotAnimation.Lively:
                return _tick % (Mood == MascotMood.Calm ? CalmLoopFrames : LoopFrames);
        }

        if (Mood == MascotMood.Calm)
            return _tick % BlinkEvery == BlinkEvery - 1 ? BlinkFrame : null;
        return _tick % CycleFrames < BurstFrames ? _tick % LoopFrames : null;
    }

    protected override Size MeasureOverride(Size availableSize) => new(Columns * Block, Rows * Block);

    protected override void OnRender(DrawingContext drawingContext)
    {
        var frame = Frame();
        _drawnFrame = frame;
        var mood = Mood;
        var resting = frame is null;
        // The pose a mood rests in is one of its frames, picked to show the mood without the motion.
        var f = frame ?? mood switch
        {
            MascotMood.Happy => 2,
            MascotMood.Worried => 9,
            _ => 0
        };

        // Whole-body movement: a hop, a shake, a sag.
        var dx = 1;
        var dy = 0;
        switch (mood)
        {
            case MascotMood.Happy:
                dy = f % 4 < 2 ? -1 : 0;
                break;
            case MascotMood.Calm when IsCalmHop(f):
                dy = -1;
                break;
            case MascotMood.Panic when !resting:
                dx += (f % 4) switch { 0 => -1, 2 => 1, _ => 0 };
                break;
            case MascotMood.Tired:
                dy = f is >= 6 and < 14 ? 1 : 0;
                break;
        }

        void Draw(Brush brush, int x, int y, int width, int height) =>
            drawingContext.DrawRectangle(brush, null, new Rect((x + dx) * Block, (y + dy) * Block, width * Block, height * Block));

        Draw(Body, 3, BodyTop, 18, 12);
        foreach (var x in (int[])[3, 8, 14, 19])
            Draw(Body, x, BodyTop + 12, 2, 4 - Math.Max(dy, 0)); // legs: a pair under each side; they give when it sags

        // Arms: out to the sides, thrown up, or hanging.
        var (leftArm, rightArm, armLength) = mood switch
        {
            MascotMood.Panic => f % 2 == 0 ? (BodyTop - 2, BodyTop + 2, 6) : (BodyTop + 2, BodyTop - 2, 6),
            MascotMood.Tired => (BodyTop + 6, BodyTop + 6, 5),
            MascotMood.Happy when f % 4 < 2 => (BodyTop + 1, BodyTop + 1, 5),
            MascotMood.Calm when IsCalmHop(f) => (BodyTop + 1, BodyTop + 1, 5),
            _ => (BodyTop + 3, BodyTop + 3, 5)
        };
        Draw(Body, 0, leftArm, 3, armLength);
        Draw(Body, 21, rightArm, 3, armLength);

        var eyes = BodyTop + 2;
        switch (mood)
        {
            case MascotMood.Happy: // eyes closed in a smile, and a smile
                foreach (var x in (int[])[5, 15])
                {
                    Draw(Dark, x, eyes + 1, 1, 1);
                    Draw(Dark, x + 1, eyes, 2, 1);
                    Draw(Dark, x + 3, eyes + 1, 1, 1);
                }
                Draw(Dark, 10, eyes + 4, 1, 1);
                Draw(Dark, 11, eyes + 5, 2, 1);
                Draw(Dark, 13, eyes + 4, 1, 1);
                break;

            case MascotMood.Worried: // eyes darting under knitted brows, a bead of sweat running down
                var look = (f / 4) switch { 1 => -1, 3 => 1, _ => 0 };
                Draw(Dark, 6 + look, eyes + 1, 2, 2);
                Draw(Dark, 16 + look, eyes + 1, 2, 2);
                Draw(Dark, 5, eyes - 1, 3, 1);
                Draw(Dark, 16, eyes - 1, 3, 1);
                var drop = f % 8;
                if (drop < 6)
                {
                    Draw(Sweat, 22, BodyTop - 3 + drop, 1, 1);
                    Draw(Sweat, 21, BodyTop - 2 + drop, 2, 2);
                }
                break;

            case MascotMood.Panic: // eyes wide, mouth open, sweat flying
                foreach (var x in (int[])[5, 15])
                {
                    Draw(Brushes.White, x, eyes - 1, 4, 4);
                    Draw(Dark, x + 1 + f % 2, eyes, 2, 2);
                }
                Draw(Dark, 10, eyes + 5, 4, 2 + f % 2);
                if (f % 2 == 1)
                {
                    Draw(Sweat, 5, BodyTop - 4, 1, 2);
                    Draw(Sweat, 18, BodyTop - 4, 1, 2);
                }
                else
                {
                    Draw(Sweat, 2, BodyTop - 3, 1, 2);
                    Draw(Sweat, 11, BodyTop - 4, 2, 1);
                    Draw(Sweat, 21, BodyTop - 3, 1, 2);
                }
                break;

            case MascotMood.Tired: // crosses for eyes, a flat mouth
                foreach (var x in (int[])[5, 15])
                {
                    for (var i = 0; i < 3; i++)
                    {
                        Draw(Dark, x + i, eyes + i, 1, 1);
                        Draw(Dark, x + 2 - i, eyes + i, 1, 1);
                    }
                }
                Draw(Dark, 10, eyes + 6, 4, 1);
                break;

            default: // calm: square eyes, shut for the one frame of a blink, glancing left then right
                var blink = f is BlinkFrame or 35;
                var glance = f is >= 16 and < 20 ? -1 : f is >= 20 and < 24 ? 1 : 0;
                Draw(Dark, 6 + glance, blink ? eyes + 1 : eyes, 2, blink ? 1 : 2);
                Draw(Dark, 16 + glance, blink ? eyes + 1 : eyes, 2, blink ? 1 : 2);
                break;
        }
    }

    // Two quick hops near the end of the calm mood's lively loop.
    private static bool IsCalmHop(int frame) => frame is 40 or 41 or 44 or 45;

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
