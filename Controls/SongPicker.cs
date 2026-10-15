using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace PiClock.Controls;

/// <summary>
/// Shown over the clock after a tap: one big round button per song scene (bus, rainbow,
/// spider, the head-shoulders-knees-and-toes kid), each with its own little animation. The
/// buttons bounce in one after another, then bob gently until one is tapped. The window does the hit-testing via
/// <see cref="ButtonAt"/>; this control only draws.
/// </summary>
public class SongPicker : Control
{
    private static readonly IBrush Backdrop   = new SolidColorBrush(Color.FromArgb(185, 10, 10, 20));
    private static readonly IBrush SkyFill    = new SolidColorBrush(Color.Parse("#4A90D9"));
    private static readonly IBrush GrassFill  = new SolidColorBrush(Color.Parse("#4CAF50"));
    private static readonly IBrush ShadowFill = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0));
    private static readonly IBrush BusYellow  = new SolidColorBrush(Color.Parse("#FFD700"));
    private static readonly IBrush BusTrim    = new SolidColorBrush(Color.Parse("#DAA520"));
    private static readonly IBrush BusWindow  = new SolidColorBrush(Color.Parse("#87CEEB"));
    private static readonly IBrush TireFill   = new SolidColorBrush(Color.Parse("#333333"));
    private static readonly IBrush HubFill    = new SolidColorBrush(Color.Parse("#BBBBBB"));
    private static readonly IBrush CloudFill  = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255));
    private static readonly IBrush NoteFill   = Brushes.White;

    private static readonly IBrush[] RainbowFill =
        new[] { "#FF3B30", "#FF9500", "#FFD60A", "#34C759", "#0A84FF", "#5856D6", "#AF52DE" }
            .Select(c => (IBrush)new SolidColorBrush(Color.Parse(c))).ToArray();

    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();
    private IReadOnlyList<string> _scenes = [];

    /// <summary>Seconds a button takes to bounce in, and the gap before the next one starts.</summary>
    private const double PopSeconds = 0.45, StaggerSeconds = 0.12;

    public bool IsOpen { get; private set; }

    /// <summary>Seconds since the picker opened.</summary>
    public double OpenSeconds => _clock.Elapsed.TotalSeconds;

    /// <summary>All buttons have finished bouncing in, so a tap now is a choice rather than
    /// the tail end of the tap that opened the picker.</summary>
    public bool IsReady => IsOpen && OpenSeconds > PopSeconds;

    public SongPicker()
    {
        IsHitTestVisible = false;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        _timer.Tick += (_, _) => InvalidateVisual();
    }

    public void Open(IReadOnlyList<string> scenes)
    {
        _scenes = scenes;
        IsOpen = true;
        _clock.Restart();
        _timer.Start();
        InvalidateVisual();
    }

    public void Close()
    {
        IsOpen = false;
        _timer.Stop();
        _clock.Stop();
        InvalidateVisual();
    }

    /// <summary>The scene whose button is under <paramref name="p"/> (window coordinates;
    /// the picker fills the window), or null. Generous, so small fingers needn't be precise.</summary>
    public string? ButtonAt(Point p)
    {
        if (!IsOpen) return null;
        var (centers, r) = Layout();
        for (int i = 0; i < centers.Length; i++)
        {
            double dx = p.X - centers[i].X, dy = p.Y - centers[i].Y;
            if (dx * dx + dy * dy <= r * r * 1.3 * 1.3) return _scenes[i];
        }
        return null;
    }

    private (Point[] Centers, double Radius) Layout()
    {
        double w = Bounds.Width, h = Bounds.Height;
        int n = Math.Max(1, _scenes.Count);
        double r = Math.Min(h * 0.24, w / (n * 2.7));
        double gap = (w - n * 2 * r) / (n + 1);
        var centers = Enumerable.Range(0, _scenes.Count)
            .Select(i => new Point(gap + r + i * (2 * r + gap), h * 0.5))
            .ToArray();
        return (centers, r);
    }

    public override void Render(DrawingContext ctx)
    {
        if (!IsOpen) return;
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        double t = OpenSeconds;
        using (ctx.PushOpacity(Math.Clamp(t / 0.2, 0, 1)))
            ctx.DrawRectangle(Backdrop, null, new Rect(0, 0, w, h));

        var (centers, r) = Layout();
        for (int i = 0; i < centers.Length; i++)
        {
            double pop = ElasticOut((t - i * StaggerSeconds) / PopSeconds);
            if (pop <= 0.01) continue;

            // Gentle bob, each button a little out of step with the others
            double bob = Math.Sin(t * 2.4 + i * 1.3) * r * 0.04;
            var c = new Point(centers[i].X, centers[i].Y + bob);
            double br = r * pop;

            ctx.DrawEllipse(ShadowFill, null, new Point(c.X, centers[i].Y + r * 0.12), br, br);
            using (ctx.PushGeometryClip(new EllipseGeometry(new Rect(c.X - br, c.Y - br, br * 2, br * 2))))
            {
                ctx.DrawEllipse(SkyFill, null, c, br, br);
                DrawIcon(ctx, _scenes[i], c, br, t + i * 0.7);
            }

            // A white ring that breathes
            double ring = br * (0.075 + 0.015 * Math.Sin(t * 3 + i));
            ctx.DrawEllipse(null, new Pen(Brushes.White, ring), c, br - ring / 2, br - ring / 2);
        }
    }

    private static void DrawIcon(DrawingContext ctx, string scene, Point c, double r, double t)
    {
        switch (scene)
        {
            case "bus":     DrawBusIcon(ctx, c, r, t); break;
            case "rainbow": DrawRainbowIcon(ctx, c, r, t); break;
            case "spider":  DrawSpiderIcon(ctx, c, r, t); break;
            case "head":    HeadShouldersKneesToes.DrawIcon(ctx, c, r, t); break;
            default:        DrawNoteIcon(ctx, c, r, t); break;
        }
    }

    /// <summary>A yellow bus rocking along the grass with turning wheels.</summary>
    private static void DrawBusIcon(DrawingContext ctx, Point c, double r, double t)
    {
        ctx.DrawRectangle(GrassFill, null, new Rect(c.X - r, c.Y + r * 0.42, r * 2, r));

        double bw = r * 1.35, bh = r * 0.7;
        double x = c.X - bw / 2 + Math.Sin(t * 1.5) * r * 0.06;
        double y = c.Y - bh * 0.55 - Math.Abs(Math.Sin(t * 6)) * r * 0.03;

        ctx.DrawRectangle(BusYellow, new Pen(BusTrim, r * 0.03), new RoundedRect(new Rect(x, y, bw, bh), r * 0.1));
        ctx.DrawRectangle(BusTrim, null, new Rect(x, y + bh * 0.62, bw, bh * 0.07));
        for (int k = 0; k < 4; k++)
            ctx.DrawRectangle(BusWindow, null,
                new RoundedRect(new Rect(x + bw * (0.06 + k * 0.215), y + bh * 0.13, bw * 0.17, bh * 0.36), r * 0.03));

        double wr = r * 0.16;
        foreach (double wx in new[] { x + bw * 0.24, x + bw * 0.78 })
        {
            var wc = new Point(wx, y + bh + wr * 0.2);
            ctx.DrawEllipse(TireFill, null, wc, wr, wr);
            ctx.DrawEllipse(HubFill, null, wc, wr * 0.45, wr * 0.45);
            double a = t * 8;
            ctx.DrawLine(new Pen(TireFill, wr * 0.18),
                new Point(wc.X - Math.Cos(a) * wr * 0.45, wc.Y - Math.Sin(a) * wr * 0.45),
                new Point(wc.X + Math.Cos(a) * wr * 0.45, wc.Y + Math.Sin(a) * wr * 0.45));
        }
    }

    /// <summary>Rainbow arcs that ripple, on two little clouds.</summary>
    private static void DrawRainbowIcon(DrawingContext ctx, Point c, double r, double t)
    {
        double cy = c.Y + r * 0.35;
        double outer = r * 0.8, band = outer * 0.1;
        for (int i = 0; i < RainbowFill.Length; i++)
        {
            double rr = outer - band * (i + 0.5) + Math.Sin(t * 4 - i * 0.6) * r * 0.015;
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(new Point(c.X - rr, cy), false);
                g.ArcTo(new Point(c.X + rr, cy), new Size(rr, rr), 0, false, SweepDirection.Clockwise);
                g.EndFigure(false);
            }
            ctx.DrawGeometry(null, new Pen(RainbowFill[i], band * 1.1), geo);
        }

        double feet = outer - band * 3.5;
        double bob = Math.Sin(t * 3) * r * 0.03;
        DrawPuff(ctx, c.X - feet, cy - r * 0.02 + bob, r * 0.22);
        DrawPuff(ctx, c.X + feet, cy - r * 0.02 - bob, r * 0.22);
    }

    private static void DrawPuff(DrawingContext ctx, double x, double y, double r)
    {
        ctx.DrawEllipse(CloudFill, null, new Point(x, y), r, r * 0.7);
        ctx.DrawEllipse(CloudFill, null, new Point(x - r * 0.75, y + r * 0.25), r * 0.7, r * 0.55);
        ctx.DrawEllipse(CloudFill, null, new Point(x + r * 0.75, y + r * 0.15), r * 0.8, r * 0.55);
    }

    /// <summary>The spider bobbing on a silk thread, legs wiggling.</summary>
    private static void DrawSpiderIcon(DrawingContext ctx, Point c, double r, double t)
    {
        double y = c.Y + r * 0.05 + Math.Sin(t * 2.2) * r * 0.15;
        double x = c.X + Math.Sin(t * 1.1) * r * 0.04;
        ctx.DrawLine(new Pen(Brushes.White, r * 0.02), new Point(c.X, c.Y - r), new Point(x, y));
        ItsyBitsySpider.DrawSpider(ctx, x, y, r * 0.3, t * 10, Math.Sin(t * 1.1) * 0.12);
    }

    /// <summary>A bouncing music note for a scene without its own icon.</summary>
    private static void DrawNoteIcon(DrawingContext ctx, Point c, double r, double t)
    {
        double y = c.Y + Math.Sin(t * 3) * r * 0.08;
        ctx.DrawEllipse(NoteFill, null, new Point(c.X - r * 0.12, y + r * 0.3), r * 0.2, r * 0.15);
        ctx.DrawLine(new Pen(NoteFill, r * 0.07), new Point(c.X + r * 0.05, y + r * 0.3), new Point(c.X + r * 0.05, y - r * 0.4));
        ctx.DrawLine(new Pen(NoteFill, r * 0.07), new Point(c.X + r * 0.05, y - r * 0.4), new Point(c.X + r * 0.3, y - r * 0.2));
    }

    /// <summary>0 → 1 with a springy overshoot; 0 before the start.</summary>
    private static double ElasticOut(double t)
    {
        if (t <= 0) return 0;
        if (t >= 1) return 1;
        return Math.Pow(2, -10 * t) * Math.Sin((t * 10 - 0.75) * (2 * Math.PI / 3)) + 1;
    }
}
