using Avalonia;
using Avalonia.Media;

namespace PiClock.Controls;

/// <summary>
/// The rainbow scene: the bus's sunny backdrop (no road), with a rainbow that paints itself
/// in band by band, ripples and shimmers for the song, and is wiped away again so the last
/// band disappears as the music stops. Smiling clouds sit on its feet, flowers sway in the
/// grass, and the colour names take turns at the top while that band pulses.
/// </summary>
public class ILoveARainbow : SongScene
{
    private static readonly Color[] Bands =
    [
        Color.Parse("#FF3B30"),   // red (outside)
        Color.Parse("#FF9500"),   // orange
        Color.Parse("#FFD60A"),   // yellow
        Color.Parse("#34C759"),   // green
        Color.Parse("#0A84FF"),   // blue
        Color.Parse("#5856D6"),   // indigo
        Color.Parse("#AF52DE"),   // violet (inside)
    ];

    private static readonly string[] BandNames =
        ["Red!", "Orange!", "Yellow!", "Green!", "Blue!", "Indigo!", "Violet!"];

    private static readonly IBrush[] BandFill = Bands.Select(c => (IBrush)new SolidColorBrush(c)).ToArray();

    // Dark text where white would vanish into the band colour
    private static readonly IBrush[] LabelText = Bands.Select(c =>
        (IBrush)(c.R * 0.299 + c.G * 0.587 + c.B * 0.114 > 150 ? new SolidColorBrush(Color.Parse("#333333")) : Brushes.White)).ToArray();

    private static readonly IBrush FaceFill    = new SolidColorBrush(Color.Parse("#555555"));
    private static readonly IBrush CheekFill   = new SolidColorBrush(Color.FromArgb(110, 255, 120, 150));
    private static readonly IBrush StemFill    = new SolidColorBrush(Color.Parse("#2E7D32"));
    private static readonly IBrush CenterFill  = new SolidColorBrush(Color.Parse("#FFE44D"));
    private static readonly IBrush ShineFill   = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255));
    private static readonly IBrush SparkleFill = new SolidColorBrush(Color.FromArgb(240, 255, 255, 255));

    public override IReadOnlyList<string>? LightColors { get; } =
        ["#FF3B30", "#FF9500", "#FFD60A", "#34C759", "#0A84FF", "#5856D6", "#AF52DE"];

    // ── Timing computed from song duration ──
    private int EnterTicks     => Math.Min(200, TotalTicks / 8);             // ~4 s paint-in
    private int ExitTicks      => Math.Min(300, TotalTicks / 6);             // last ~6 s wipe-away
    private int ExitStartTick  => Math.Max(EnterTicks + 50, TotalTicks - ExitTicks);
    private const int TicksPerColour = 150;                                  // 3 s per colour name

    /// <summary>Fraction of the enter time each band takes; the rest staggers their starts.</summary>
    private const double BandShare = 0.55;

    // Fixed sparkle positions around the arc: angle along it (0 = left foot, π = right foot)
    // and distance out from the centre as a multiple of the outer radius.
    private static readonly (double Angle, double Dist, double Phase)[] Sparkles = MakeSparkles();

    private static (double, double, double)[] MakeSparkles()
    {
        var rng = new Random(7);
        return Enumerable.Range(0, 16)
            .Select(i => (0.15 + i * (Math.PI - 0.3) / 15,
                          i % 2 == 0 ? 1.08 + rng.NextDouble() * 0.1 : 0.42 + rng.NextDouble() * 0.08,
                          rng.NextDouble() * Math.PI * 2))
            .ToArray();
    }

    public override void Render(DrawingContext context)
    {
        if (!IsPlaying) return;

        var sz = Bounds.Size;
        if (sz.Width <= 0 || sz.Height <= 0) return;

        double w = sz.Width, h = sz.Height;
        double s = Math.Min(w / 1280.0, h / 800.0);    // uniform scale

        double grassTop = h * 0.78;
        double cx = w / 2;
        double cy = grassTop + 12 * s;                    // feet just into the grass
        double outer = Math.Min(w * 0.40, cy - h * 0.15);
        double band = outer * 0.075;

        // 1) Sky, sun, drifting clouds, grass
        DrawBackdrop(context, w, h, grassTop, s);

        // 2) The rainbow, outside band first
        for (int i = 0; i < Bands.Length; i++)
        {
            var (a0, a1) = BandSpan(i);
            if (a1 - a0 < 0.002) continue;

            // Each band breathes in and out a little, a ripple running from red to violet
            double r = outer - band * (i + 0.5) + Math.Sin(Tick * 0.05 - i * 0.6) * 3 * s;

            // The band whose name is showing pulses wider
            double pulse = i == FeaturedBand() ? 0.18 + 0.12 * Math.Sin(Tick * 0.2) : 0;
            var pen = new Pen(BandFill[i], band * (1.12 + pulse));
            DrawArc(context, pen, cx, cy, r, a0, a1);

            // A white shimmer glides along each band, a step behind the band outside it
            double shine = Wrap(Tick * 0.022 - i * 0.18, Math.PI + 0.8, -0.8);
            double s0 = Math.Max(a0, shine - 0.12), s1 = Math.Min(a1, shine + 0.12);
            if (s1 > s0)
                DrawArc(context, new Pen(ShineFill, band * 0.45) { LineCap = PenLineCap.Round },
                        cx, cy, r, s0, s1);
        }

        // 3) Twinkling sparkles around the finished part of the arc
        var (redFrom, redTo) = BandSpan(0);
        foreach (var (angle, dist, phase) in Sparkles)
        {
            if (angle < redFrom || angle > redTo) continue;
            double twinkle = Math.Sin(Tick * 0.12 + phase);
            if (twinkle < 0) continue;
            double r = outer * dist;
            DrawSparkle(context, cx - r * Math.Cos(angle), cy - r * Math.Sin(angle), (5 + 9 * twinkle) * s);
        }

        // 4) Smiling clouds on the rainbow's feet
        double feetR = outer - band * 3.5;
        double cloudScale = s * 2.4 * CloudGrow();
        if (cloudScale > 0.05)
        {
            double bob = Math.Sin(Tick * 0.06) * 5 * s;
            DrawFootCloud(context, cx - feetR, cy - 30 * s + bob, cloudScale, false);
            DrawFootCloud(context, cx + feetR, cy - 30 * s - bob, cloudScale, true);
        }

        // 5) Flowers swaying in the grass
        for (int i = 0; i < 9; i++)
        {
            double fx = w * (0.05 + i * 0.1125);
            double fy = grassTop + (h - grassTop) * (i % 2 == 0 ? 0.45 : 0.7);
            DrawFlower(context, fx, fy, s, i);
        }

        // 6) The featured colour's name at the top
        if (Tick > EnterTicks && Tick < ExitStartTick)
            DrawLabel(context, w, h * 0.04, s, FeaturedBand());
    }

    /// <summary>The part of band i that is drawn now, as angles along the arc (0 = left foot,
    /// π = right foot). Bands paint in left to right, red first, and are wiped away the same
    /// way, violet first, so the red band's tail leaves the right foot as the song ends.</summary>
    private (double From, double To) BandSpan(int i)
    {
        double stagger = (1 - BandShare) / (Bands.Length - 1);

        double inT = (double)Tick / Math.Max(1, EnterTicks);
        double grow = EaseOut((inT - i * stagger) / BandShare);

        double outT = (double)(Tick - ExitStartTick) / Math.Max(1, TotalTicks - ExitStartTick);
        int order = Bands.Length - 1 - i;
        double wipe = Tick <= ExitStartTick ? 0 : Math.Clamp(EaseIn((outT - order * stagger) / BandShare), 0, 1);

        return (wipe * Math.PI, grow * Math.PI);
    }

    /// <summary>0 → 1 over the first second, back to 0 over the last second of the wipe.</summary>
    private double CloudGrow()
    {
        double grow = EaseOut(Tick / 50.0);
        double shrink = Math.Clamp((TotalTicks - Tick) / 50.0, 0, 1);
        return Math.Min(grow, shrink);
    }

    private int FeaturedBand() =>
        Math.Max(0, Tick - EnterTicks) / TicksPerColour % Bands.Length;

    /// <summary>Arc of radius r round (cx, cy) over the top, from angle a0 to a1 (0 = left).</summary>
    private static void DrawArc(DrawingContext ctx, Pen pen, double cx, double cy, double r, double a0, double a1)
    {
        Point At(double a) => new(cx - r * Math.Cos(a), cy - r * Math.Sin(a));

        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(At(a0), false);
            g.ArcTo(At(a1), new Size(r, r), 0, false, SweepDirection.Clockwise);
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, pen, geo);
    }

    private static void DrawSparkle(DrawingContext ctx, double x, double y, double r)
    {
        // Four-pointed star: two thin diamonds crossed
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            double t = r * 0.22;
            g.BeginFigure(new Point(x, y - r), true);
            g.LineTo(new Point(x + t, y - t));
            g.LineTo(new Point(x + r, y));
            g.LineTo(new Point(x + t, y + t));
            g.LineTo(new Point(x, y + r));
            g.LineTo(new Point(x - t, y + t));
            g.LineTo(new Point(x - r, y));
            g.LineTo(new Point(x - t, y - t));
            g.EndFigure(true);
        }
        ctx.DrawGeometry(SparkleFill, null, geo);
    }

    private void DrawFootCloud(DrawingContext ctx, double x, double y, double s, bool right)
    {
        DrawCloud(ctx, x, y, s);

        // Face: eyes that blink now and then, rosy cheeks, a smile
        double er = 2.6 * s;
        bool blink = (Tick + (right ? 90 : 0)) % 220 < 8;
        double ex = 7 * s, ey = y - 2 * s;
        if (blink)
        {
            var lid = new Pen(FaceFill, 1.2 * s);
            ctx.DrawLine(lid, new Point(x - ex - er, ey), new Point(x - ex + er, ey));
            ctx.DrawLine(lid, new Point(x + ex - er, ey), new Point(x + ex + er, ey));
        }
        else
        {
            ctx.DrawEllipse(FaceFill, null, new Point(x - ex, ey), er, er);
            ctx.DrawEllipse(FaceFill, null, new Point(x + ex, ey), er, er);
        }
        ctx.DrawEllipse(CheekFill, null, new Point(x - ex * 1.9, y + 4 * s), 4 * s, 2.6 * s);
        ctx.DrawEllipse(CheekFill, null, new Point(x + ex * 1.9, y + 4 * s), 4 * s, 2.6 * s);

        var smile = new StreamGeometry();
        using (var g = smile.Open())
        {
            g.BeginFigure(new Point(x - 6 * s, y + 4 * s), false);
            g.ArcTo(new Point(x + 6 * s, y + 4 * s), new Size(7 * s, 6 * s), 0, false, SweepDirection.CounterClockwise);
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, new Pen(FaceFill, 1.4 * s) { LineCap = PenLineCap.Round }, smile);
    }

    private void DrawFlower(DrawingContext ctx, double x, double y, double s, int i)
    {
        double sway = Math.Sin(Tick * 0.05 + i * 0.9) * 0.18;     // radians
        double stem = 34 * s;
        double hx = x + Math.Sin(sway) * stem, hy = y - Math.Cos(sway) * stem;

        ctx.DrawLine(new Pen(StemFill, 3 * s), new Point(x, y), new Point(hx, hy));

        double pr = 6.5 * s;
        var petal = BandFill[i % BandFill.Length];
        for (int k = 0; k < 5; k++)
        {
            double a = k * 72 * Math.PI / 180 + sway;
            ctx.DrawEllipse(petal, null, new Point(hx + Math.Cos(a) * pr, hy + Math.Sin(a) * pr), pr * 0.8, pr * 0.8);
        }
        ctx.DrawEllipse(CenterFill, null, new Point(hx, hy), pr * 0.6, pr * 0.6);
    }

    private void DrawLabel(DrawingContext ctx, double w, double y, double s, int i)
    {
        string text = $"♪  {BandNames[i]}  ♪";
        double bounce = Math.Sin(Tick * 0.06) * 4 * s;
        double fontSize = Math.Max(28, 40 * s);
        var typeface = new Typeface("Inter", FontStyle.Normal, FontWeight.ExtraBold);

        var fmt = new FormattedText(text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, typeface, fontSize, LabelText[i]);

        double tx = (w - fmt.Width) / 2;
        double ty = y + bounce;

        // Pill in the band's own colour
        double pad = 14 * s;
        ctx.DrawRectangle(BandFill[i], new Pen(Brushes.White, 3 * s),
            new RoundedRect(
                new Rect(tx - pad, ty - pad * 0.4, fmt.Width + pad * 2, fmt.Height + pad * 0.8),
                pad));

        ctx.DrawText(fmt, new Point(tx, ty));
    }
}
