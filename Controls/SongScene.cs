using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace PiClock.Controls;

/// <summary>
/// Base for the full-screen scenes that play with a song (the bus, the rainbow, …).
/// Owns the play/stop switch, the song length and a wall-clock tick, plus the sunny
/// outdoor backdrop the scenes share.
/// </summary>
public abstract class SongScene : Control
{
    public static readonly StyledProperty<bool> IsPlayingProperty =
        AvaloniaProperty.Register<SongScene, bool>(nameof(IsPlaying));

    public bool IsPlaying
    {
        get => GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    public static readonly StyledProperty<double> SongDurationProperty =
        AvaloniaProperty.Register<SongScene, double>(nameof(SongDuration), 39);

    /// <summary>Song length in seconds — animation timing adapts automatically.</summary>
    public double SongDuration
    {
        get => GetValue(SongDurationProperty);
        set => SetValue(SongDurationProperty, value);
    }

    /// <summary>Colours the Hue lights dance through while this scene plays, as #RRGGBB.
    /// Null = the colours in hue.json.</summary>
    public virtual IReadOnlyList<string>? LightColors => null;

    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();

    protected const double TickMs = 20;       // animation time unit (~50 fps when the Pi keeps up)

    /// <summary>Ticks since the scene started. Derived from the wall clock, not counted, so
    /// the scene stays in step with the song even when the Pi cannot draw 50 frames a second.</summary>
    protected int Tick { get; private set; }

    protected int TotalTicks => (int)(SongDuration * 1000 / TickMs);

    static SongScene()
    {
        IsPlayingProperty.Changed.AddClassHandler<SongScene>((ctrl, args) =>
        {
            if ((bool)args.NewValue!)
            {
                ctrl.Tick = 0;
                ctrl._clock.Restart();
                ctrl._timer.Start();
            }
            else
            {
                ctrl._timer.Stop();
                ctrl._clock.Stop();
            }

            ctrl.InvalidateVisual();
        });
    }

    protected SongScene()
    {
        IsHitTestVisible = false;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TickMs) };
        _timer.Tick += (_, _) => { Tick = (int)(_clock.ElapsedMilliseconds / TickMs); InvalidateVisual(); };
    }

    // ══════════════════════════════════════════════════════════════
    //  Shared backdrop: sky, sun, drifting clouds, grass
    // ══════════════════════════════════════════════════════════════

    protected static readonly IBrush SkyFill   = new SolidColorBrush(Color.Parse("#4A90D9"));
    protected static readonly IBrush GrassFill = new SolidColorBrush(Color.Parse("#4CAF50"));
    protected static readonly IBrush SunFill   = new SolidColorBrush(Color.Parse("#FFE44D"));
    protected static readonly IBrush CloudFill = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255));

    /// <summary>Sky down to <paramref name="grassTop"/>, the sun, three drifting clouds and the grass.</summary>
    protected void DrawBackdrop(DrawingContext ctx, double w, double h, double grassTop, double s)
    {
        ctx.DrawRectangle(SkyFill, null, new Rect(0, 0, w, grassTop));
        DrawSun(ctx, w * 0.85, h * 0.13, 55 * s, s);
        DrawCloud(ctx, Wrap(w * 0.15 + Tick * 0.4, w + 200, -150), h * 0.14, s);
        DrawCloud(ctx, Wrap(w * 0.55 + Tick * 0.25, w + 200, -150), h * 0.08, s * 0.8);
        DrawCloud(ctx, Wrap(w * 0.80 + Tick * 0.3, w + 200, -150), h * 0.20, s * 0.65);
        ctx.DrawRectangle(GrassFill, null, new Rect(0, grassTop, w, h - grassTop));
    }

    protected void DrawSun(DrawingContext ctx, double cx, double cy, double r, double s)
    {
        // Rays rotate slowly
        var rayPen = new Pen(SunFill, 4 * s);
        for (int i = 0; i < 12; i++)
        {
            double a = i * 30.0 * Math.PI / 180 + Tick * 0.008;
            ctx.DrawLine(rayPen,
                new Point(cx + Math.Cos(a) * r * 1.2, cy + Math.Sin(a) * r * 1.2),
                new Point(cx + Math.Cos(a) * r * 1.65, cy + Math.Sin(a) * r * 1.65));
        }

        ctx.DrawEllipse(SunFill, null, new Point(cx, cy), r, r);
    }

    protected static void DrawCloud(DrawingContext ctx, double x, double y, double s)
    {
        double r = 30 * s;
        ctx.DrawEllipse(CloudFill, null, new Point(x, y), r, r * 0.7);
        ctx.DrawEllipse(CloudFill, null, new Point(x - r * 0.75, y + r * 0.25), r * 0.7, r * 0.55);
        ctx.DrawEllipse(CloudFill, null, new Point(x + r * 0.75, y + r * 0.15), r * 0.8, r * 0.55);
    }

    /// <summary>Ease-out cubic — fast start, smooth deceleration.</summary>
    protected static double EaseOut(double t) =>
        1 - Math.Pow(1 - Math.Clamp(t, 0, 1), 3);

    /// <summary>Ease-in quadratic — gentle start, accelerating away. Not capped at 1 so a
    /// scene keeps moving if it is still up after the song.</summary>
    protected static double EaseIn(double t)
    {
        t = Math.Max(0, t);
        return t * t;
    }

    /// <summary>Wraps val into the range [min, max).</summary>
    protected static double Wrap(double val, double max, double min)
    {
        double range = max - min;
        return ((val - min) % range + range) % range + min;
    }
}
