using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ClaudeUsageTracker.App.Settings;
using ClaudeUsageTracker.App.ViewModels;

namespace ClaudeUsageTracker.App.Views;

/// <summary>
/// The pixel-art creature that sits beside the taskbar bars and acts out how much usage is left:
/// hopping among sparkles after a reset, blinking while there's room, sweating as it runs short, flailing near
/// the limit, asleep once it's used up.
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
    private const int Eyes = BodyTop + 2; // the row the eyes are on

    private const int LoopFrames = 16;
    private const int BurstFrames = LoopFrames * 2;
    private const int CycleFrames = 240;   // a burst a minute
    private const int BlinkEvery = 24;     // the calm mood's blink, every six seconds
    private const int BlinkFrame = 11;
    private const int CalmLoopFrames = 48; // the calm mood's own loop when lively: blinks, glances, a couple of hops

    private static readonly Brush Body = Frozen(Color.FromRgb(217, 119, 87));
    private static readonly Brush Dark = Frozen(Color.FromRgb(30, 20, 18));
    private static readonly Brush Sweat = Frozen(Color.FromRgb(110, 190, 255));
    private static readonly Brush Sparkle = Frozen(Color.FromRgb(255, 200, 60));
    private static readonly Brush Sleep = Frozen(Color.FromRgb(150, 150, 160));

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
        var f = frame ?? RestFrame(mood);
        var painter = new Painter(drawingContext, Shift(mood, f, resting: frame is null), Lift(mood, f));

        DrawBody(painter, mood, f);
        switch (mood)
        {
            case MascotMood.Happy:
                DrawHappy(painter, f);
                break;
            case MascotMood.Worried:
                DrawWorried(painter, f);
                break;
            case MascotMood.Panic:
                DrawPanic(painter, f);
                break;
            case MascotMood.Tired:
                DrawTired(painter, f);
                break;
            default:
                DrawCalm(painter, f);
                break;
        }
    }

    /// <summary>
    /// Draws blocks for one frame: <see cref="Draw"/> for the creature, which moves as a whole by
    /// (<paramref name="Shift"/>, <paramref name="Lift"/>) blocks, and <see cref="DrawStill"/>
    /// for what's around it rather than part of it, which stays put while it hops.
    /// </summary>
    private readonly record struct Painter(DrawingContext Context, int Shift, int Lift)
    {
        public void Draw(Brush brush, int x, int y, int width, int height) => Fill(brush, x + Shift, y + Lift, width, height);

        public void DrawStill(Brush brush, int x, int y, int width, int height) => Fill(brush, x, y, width, height);

        // Column 0 of the drawing is one block in: the spare column on the left.
        private void Fill(Brush brush, int x, int y, int width, int height) =>
            Context.DrawRectangle(brush, null, new Rect((x + 1) * Block, y * Block, width * Block, height * Block));
    }

    // The pose a mood rests in is one of its frames, picked to show the mood without the motion.
    private static int RestFrame(MascotMood mood) => mood switch
    {
        MascotMood.Happy => 2,
        MascotMood.Worried => 9,
        _ => 0
    };

    // Sideways movement of the whole body: the panic's shake (not while resting).
    private static int Shift(MascotMood mood, int f, bool resting) =>
        mood == MascotMood.Panic && !resting ? (f % 4) switch { 0 => -1, 2 => 1, _ => 0 } : 0;

    // Vertical movement of the whole body: up a block for a hop, down one for a sag.
    private static int Lift(MascotMood mood, int f) => mood switch
    {
        MascotMood.Happy => f % 4 < 2 ? -1 : 0,
        MascotMood.Calm => IsCalmHop(f) ? -1 : 0,
        MascotMood.Tired => f is >= 6 and < 14 ? 1 : 0,
        _ => 0
    };

    private static void DrawBody(Painter painter, MascotMood mood, int f)
    {
        painter.Draw(Body, 3, BodyTop, 18, 12);
        foreach (var x in (int[])[3, 8, 14, 19])
            painter.Draw(Body, x, BodyTop + 12, 2, 4 - Math.Max(painter.Lift, 0)); // legs: a pair under each side; they give when it sags

        // Arms: out to the sides, thrown up, or hanging.
        var (leftArm, rightArm, armLength) = mood switch
        {
            MascotMood.Panic => f % 2 == 0 ? (BodyTop - 2, BodyTop + 2, 6) : (BodyTop + 2, BodyTop - 2, 6),
            MascotMood.Tired => (BodyTop + 6, BodyTop + 6, 5),
            MascotMood.Happy when f % 4 < 2 => (BodyTop + 1, BodyTop + 1, 5),
            MascotMood.Calm when IsCalmHop(f) => (BodyTop + 1, BodyTop + 1, 5),
            _ => (BodyTop + 3, BodyTop + 3, 5)
        };
        painter.Draw(Body, 0, leftArm, 3, armLength);
        painter.Draw(Body, 21, rightArm, 3, armLength);
    }

    // Its own plain face — the hop says it — with sparkles twinkling either side.
    private static void DrawHappy(Painter painter, int f)
    {
        painter.Draw(Dark, 6, Eyes, 2, 2);
        painter.Draw(Dark, 16, Eyes, 2, 2);
        if (f % 4 < 2)
        {
            painter.DrawStill(Sparkle, 0, 2, 1, 3);
            painter.DrawStill(Sparkle, -1, 3, 3, 1);
            painter.DrawStill(Sparkle, 23, 0, 1, 3);
            painter.DrawStill(Sparkle, 22, 1, 3, 1);
        }
        else
        {
            painter.DrawStill(Sparkle, 0, 0, 1, 1);
            painter.DrawStill(Sparkle, 22, 2, 1, 1);
        }
    }

    // Eyes darting under knitted brows, a bead of sweat running down.
    private static void DrawWorried(Painter painter, int f)
    {
        var look = (f / 4) switch { 1 => -1, 3 => 1, _ => 0 };
        painter.Draw(Dark, 6 + look, Eyes + 1, 2, 2);
        painter.Draw(Dark, 16 + look, Eyes + 1, 2, 2);
        painter.Draw(Dark, 5, Eyes - 1, 3, 1);
        painter.Draw(Dark, 16, Eyes - 1, 3, 1);
        var drop = f % 8;
        if (drop < 6)
        {
            painter.Draw(Sweat, 22, BodyTop - 3 + drop, 1, 1);
            painter.Draw(Sweat, 21, BodyTop - 2 + drop, 2, 2);
        }
    }

    // Eyes wide, mouth open, sweat flying.
    private static void DrawPanic(Painter painter, int f)
    {
        foreach (var x in (int[])[5, 15])
        {
            painter.Draw(Brushes.White, x, Eyes - 1, 4, 4);
            painter.Draw(Dark, x + 1 + f % 2, Eyes, 2, 2);
        }
        painter.Draw(Dark, 10, Eyes + 5, 4, 2 + f % 2);
        if (f % 2 == 1)
        {
            painter.Draw(Sweat, 5, BodyTop - 4, 1, 2);
            painter.Draw(Sweat, 18, BodyTop - 4, 1, 2);
        }
        else
        {
            painter.Draw(Sweat, 2, BodyTop - 3, 1, 2);
            painter.Draw(Sweat, 11, BodyTop - 4, 2, 1);
            painter.Draw(Sweat, 21, BodyTop - 3, 1, 2);
        }
    }

    // Asleep: eyes shut, and a z rising off it — small, then large, then gone.
    private static void DrawTired(Painter painter, int f)
    {
        painter.Draw(Dark, 6, Eyes + 1, 2, 1);
        painter.Draw(Dark, 16, Eyes + 1, 2, 1);
        switch (f / 4)
        {
            case 0:
                painter.DrawStill(Sleep, 16, 1, 3, 1);
                painter.DrawStill(Sleep, 17, 2, 1, 1);
                painter.DrawStill(Sleep, 16, 3, 3, 1);
                break;
            case 1 or 2:
                painter.DrawStill(Sleep, 19, 0, 4, 1);
                painter.DrawStill(Sleep, 21, 1, 1, 1);
                painter.DrawStill(Sleep, 20, 2, 1, 1);
                painter.DrawStill(Sleep, 19, 3, 4, 1);
                break;
        }
    }

    // Square eyes, shut for the one frame of a blink, glancing left then right.
    private static void DrawCalm(Painter painter, int f)
    {
        var blink = f is BlinkFrame or 35;
        var glance = f is >= 16 and < 20 ? -1 : f is >= 20 and < 24 ? 1 : 0;
        painter.Draw(Dark, 6 + glance, blink ? Eyes + 1 : Eyes, 2, blink ? 1 : 2);
        painter.Draw(Dark, 16 + glance, blink ? Eyes + 1 : Eyes, 2, blink ? 1 : 2);
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
