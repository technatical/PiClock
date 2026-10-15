using Avalonia;
using Avalonia.Media;

namespace PiClock.Controls;

/// <summary>
/// The spider scene: a house with a water spout down its corner and a friendly spider that
/// acts out each verse. It climbs up the spout, down comes the rain and washes it out onto
/// the grass, out comes the sun (with a smile) and dries up the rain, and up the spout it
/// climbs again. The song is split into whole verses so the last one ends with the music.
/// </summary>
public class ItsyBitsySpider : SongScene
{
    private static readonly Color SkyClear = Color.Parse("#4A90D9");
    private static readonly Color SkyStorm = Color.Parse("#5D6B7E");

    private static readonly IBrush WallFill    = new SolidColorBrush(Color.Parse("#F2D3A8"));
    private static readonly IBrush WallLine    = new SolidColorBrush(Color.FromArgb(70, 160, 110, 60));
    private static readonly IBrush RoofFill    = new SolidColorBrush(Color.Parse("#C0392B"));
    private static readonly IBrush WindowFill  = new SolidColorBrush(Color.Parse("#BFE3FF"));
    private static readonly IBrush FrameFill   = Brushes.White;
    private static readonly IBrush PipeFill    = new SolidColorBrush(Color.Parse("#9AA5B1"));
    private static readonly IBrush PipeShade   = new SolidColorBrush(Color.Parse("#76828F"));
    private static readonly IBrush PipeShine   = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255));
    private static readonly IBrush StormCloud  = new SolidColorBrush(Color.FromArgb(235, 120, 128, 140));
    private static readonly IBrush RainFill    = new SolidColorBrush(Color.FromArgb(210, 200, 225, 255));
    private static readonly IBrush WaterFill   = new SolidColorBrush(Color.FromArgb(200, 90, 160, 235));
    private static readonly IBrush PuddleFill  = new SolidColorBrush(Color.FromArgb(170, 80, 150, 225));
    private static readonly IBrush FaceFill    = new SolidColorBrush(Color.Parse("#7A4B00"));
    private static readonly IBrush LabelFill   = new SolidColorBrush(Color.Parse("#2D2438"));
    private static readonly IBrush SparkleFill = new SolidColorBrush(Color.FromArgb(240, 255, 255, 255));

    // The spider itself (also drawn on the picker button)
    private static readonly IBrush SpiderFill  = new SolidColorBrush(Color.Parse("#2D2438"));
    private static readonly IBrush SpiderShine = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255));
    private static readonly IBrush CheekFill   = new SolidColorBrush(Color.FromArgb(140, 255, 120, 150));
    private static readonly IBrush PupilFill   = new SolidColorBrush(Color.Parse("#111111"));

    private static readonly string[] Lines =
        ["Up the water spout!", "Down came the rain!", "Out came the sun!", "Up again!"];

    public override IReadOnlyList<string>? LightColors { get; } =
        ["#4A90D9", "#7FC8F8", "#FFD34D", "#FFB347"];

    // ── Timing computed from song duration: whole verses of about 22 s ──
    private const double VerseSeconds = 22;
    private int Verses       => Math.Max(1, (int)Math.Round(SongDuration / VerseSeconds));
    private double VerseTicks => (double)TotalTicks / Verses;

    // Fixed raindrop positions: x as a share of the width, a phase, a speed factor
    private static readonly (double X, double Phase, double Speed)[] Drops = MakeDrops();

    private static (double, double, double)[] MakeDrops()
    {
        var rng = new Random(11);
        return Enumerable.Range(0, 90)
            .Select(_ => (rng.NextDouble(), rng.NextDouble(), 0.8 + rng.NextDouble() * 0.5))
            .ToArray();
    }

    public override void Render(DrawingContext context)
    {
        if (!IsPlaying) return;

        var sz = Bounds.Size;
        if (sz.Width <= 0 || sz.Height <= 0) return;

        double w = sz.Width, h = sz.Height;
        double s = Math.Min(w / 1280.0, h / 800.0);    // uniform scale

        // Where we are in the song: verse number and the 0..1 position inside it
        int verse = Math.Min(Verses - 1, (int)(Tick / VerseTicks));
        double f = Math.Clamp((Tick - verse * VerseTicks) / VerseTicks, 0, 1);
        int part = Math.Min(3, (int)(f * 4));          // climb, rain, sun, climb again
        double p = f * 4 - part;                       // 0..1 inside the part

        // How stormy it is: clouds roll in early in the rain part, clear early in the sun part
        double storm = part switch
        {
            1 => EaseOut(p / 0.25),
            2 => 1 - EaseOut(p / 0.35),
            _ => 0
        };
        double rain = part == 1 ? Math.Clamp((p - 0.1) / 0.15, 0, 1) : part == 2 ? Math.Clamp(1 - p / 0.2, 0, 1) : 0;
        double sunny = part == 2 ? EaseOut((p - 0.15) / 0.3) : part == 3 ? 1 - EaseOut(p / 0.4) : 0;

        double grassTop = h * 0.80;

        // ── House and spout geometry ──
        double wallL = w * 0.06, wallR = w * 0.34, wallTop = h * 0.30;
        double pipeX = wallR + 26 * s, pipeW = 36 * s;
        double gutterY = wallTop - 6 * s;
        double mouthX = pipeX + 70 * s, mouthY = grassTop - 26 * s;
        double spiderR = 30 * s;
        double topY = gutterY + 70 * s, bottomY = grassTop - 110 * s;
        double landX = mouthX + 240 * s, landY = grassTop + 40 * s;

        // 1) Sky (greys over in the storm), sun, clouds, grass
        context.DrawRectangle(new SolidColorBrush(Lerp(SkyClear, SkyStorm, storm)), null, new Rect(0, 0, w, grassTop));
        double sunGrow = 1 + 0.25 * sunny + 0.04 * sunny * Math.Sin(Tick * 0.15);
        DrawSun(context, w * 0.84, h * 0.15, 55 * s * sunGrow, s);
        if (sunny > 0.05) DrawSunFace(context, w * 0.84, h * 0.15, 55 * s * sunGrow, sunny);
        DrawCloud(context, Wrap(w * 0.55 + Tick * 0.25, w + 200, -150), h * 0.08, s * 0.8);
        DrawCloud(context, Wrap(w * 0.85 + Tick * 0.3, w + 200, -150), h * 0.26, s * 0.65);
        if (storm > 0.01) DrawStorm(context, w, h, s, storm);
        context.DrawRectangle(GrassFill, null, new Rect(0, grassTop, w, h - grassTop));

        // 2) Puddle under the spout: fills while it rains, dries up in the sun
        double puddle = part == 1 ? EaseOut((p - 0.3) / 0.5) : part == 2 ? 1 - EaseOut((p - 0.2) / 0.6) : 0;
        if (puddle > 0.01)
            context.DrawEllipse(PuddleFill, null, new Point(mouthX + 120 * s, grassTop + 32 * s), 170 * s * puddle, 22 * s * puddle);

        // 3) The house, then the spout down its corner
        DrawHouse(context, wallL, wallR, wallTop, grassTop, s);
        DrawSpout(context, wallR, pipeX, pipeW, gutterY, mouthX, mouthY, s);

        // 4) Water rushing out of the spout while it rains
        if (part == 1 && p > 0.25 || part == 2 && p < 0.15)
            DrawGush(context, mouthX, mouthY, s, part == 2 ? 1 - p / 0.15 : Math.Clamp((p - 0.25) / 0.1, 0, 1));

        // 5) Rain in front of everything but the spider's sun
        if (rain > 0.01) DrawRain(context, w, grassTop, s, rain);

        // 6) The spider
        var (sx, sy, spin, legSpeed, wet) = SpiderPose(verse, part, p, pipeX, topY, bottomY, mouthX, mouthY, landX, landY, s);
        DrawSpider(context, sx, sy, spiderR, Tick * legSpeed, spin, wet);

        // Drying off in the sun: sparkles round the spider
        if (part == 2 && p > 0.45)
            for (int i = 0; i < 6; i++)
            {
                double tw = Math.Sin(Tick * 0.15 + i * 1.7);
                if (tw < 0) continue;
                double a = i * Math.PI / 3 + Tick * 0.01;
                DrawSparkle(context, sx + Math.Cos(a) * spiderR * 2.4, sy - spiderR * 0.4 + Math.Sin(a) * spiderR * 1.8, (4 + 8 * tw) * s);
            }

        // 7) The line being sung, at the top
        DrawLabel(context, w, h * 0.04, s, part);
    }

    /// <summary>Spider position, spin, leg speed and how wet it is for this part of the verse.
    /// The first verse starts at the bottom of the spout; later ones start at the top, where
    /// the last "up again" left it.</summary>
    private (double X, double Y, double Spin, double LegSpeed, double Wet) SpiderPose(
        int verse, int part, double p, double pipeX, double topY, double bottomY,
        double mouthX, double mouthY, double landX, double landY, double s)
    {
        switch (part)
        {
            case 0:     // climbing up (or, after the first verse, a happy jig at the top)
                if (verse == 0)
                    return (pipeX, bottomY + (topY - bottomY) * EaseInOut(p) + Bob(s), 0, 0.25, 0);
                return (pipeX + Math.Sin(Tick * 0.12) * 8 * s, topY + Math.Abs(Math.Sin(Tick * 0.12)) * -10 * s, Math.Sin(Tick * 0.12) * 0.25, 0.3, 0);

            case 1:     // waits at the top, the rain pushes it down the spout and out the bottom
            {
                if (p < 0.35)
                    return (pipeX + Math.Sin(Tick * 0.6) * 2 * s * p, topY, 0, 0.1, 0);   // shivering
                if (p < 0.55)
                {
                    double t = EaseIn((p - 0.35) / 0.2);
                    return (pipeX, topY + (mouthY - 20 * s - topY) * Math.Min(1, t), 0, 0.6, 0.5);
                }
                // Out of the spout mouth in an arc, tumbling, onto the grass
                double u = Math.Clamp((p - 0.55) / 0.3, 0, 1);
                double x = mouthX + (landX - mouthX) * u;
                double y = mouthY + (landY - mouthY) * u - Math.Sin(u * Math.PI) * 90 * s;
                return (x, y, u * Math.PI * 4, 0.6, 1);
            }

            case 2:     // on the grass while the sun dries it: shakes off, then smiles
            {
                double shake = p < 0.45 ? Math.Sin(Tick * 0.9) * 0.15 : 0;
                return (landX, landY + Bob(s) * 0.5, shake, p < 0.45 ? 0.05 : 0.15, Math.Clamp(1 - p / 0.7, 0, 1));
            }

            default:    // walks back to the spout and climbs up again
            {
                if (p < 0.3)
                {
                    double t = EaseInOut(p / 0.3);
                    double x = landX + (pipeX - landX) * t;
                    double y = landY + (bottomY - landY) * t + Math.Abs(Math.Sin(Tick * 0.2)) * -8 * s;
                    return (x, y, 0, 0.35, 0);
                }
                double c = EaseInOut((p - 0.3) / 0.65);
                return (pipeX, bottomY + (topY - bottomY) * c + Bob(s), 0, 0.25, 0);
            }
        }
    }

    private double Bob(double s) => Math.Sin(Tick * 0.25) * 3 * s;

    private static double EaseInOut(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static Color Lerp(Color a, Color b, double t) =>
        Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    // ══════════════════════════════════════════════════════════════
    //  The spider: round body, eight wiggling legs, big eyes, a smile
    // ══════════════════════════════════════════════════════════════

    /// <summary>Draws the spider centred on (x, y) with body radius r. legPhase drives the
    /// leg wiggle, spin rotates it (radians), wet 0..1 adds drips.</summary>
    internal static void DrawSpider(DrawingContext ctx, double x, double y, double r,
                                    double legPhase, double spin = 0, double wet = 0)
    {
        var rot = Matrix.CreateTranslation(-x, -y) * Matrix.CreateRotation(spin) * Matrix.CreateTranslation(x, y);
        using (ctx.PushTransform(rot))
        {
            var legPen = new Pen(SpiderFill, r * 0.17) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            double[] angles = [-0.75, -0.25, 0.25, 0.75];   // up to down, from the side
            for (int side = -1; side <= 1; side += 2)
                for (int k = 0; k < 4; k++)
                {
                    double wiggle = Math.Sin(legPhase + k * Math.PI / 2 + (side > 0 ? Math.PI : 0)) * r * 0.22;
                    double a = angles[k];
                    var hip  = new Point(x + side * r * 0.55, y + (k - 1.5) * r * 0.28);
                    var knee = new Point(hip.X + side * Math.Cos(a) * r * 0.95, hip.Y + Math.Sin(a) * r * 0.95 - r * 0.45 + wiggle);
                    var foot = new Point(knee.X + side * r * 0.45, knee.Y + r * 0.85 + wiggle * 0.5);
                    var geo = new StreamGeometry();
                    using (var g = geo.Open())
                    {
                        g.BeginFigure(hip, false);
                        g.LineTo(knee);
                        g.LineTo(foot);
                        g.EndFigure(false);
                    }
                    ctx.DrawGeometry(null, legPen, geo);
                }

            // Body and a little shine
            ctx.DrawEllipse(SpiderFill, null, new Point(x, y), r, r * 1.05);
            ctx.DrawEllipse(SpiderShine, null, new Point(x - r * 0.4, y - r * 0.55), r * 0.25, r * 0.15);

            // Eyes that blink now and then
            double ex = r * 0.36, ey = y - r * 0.18, er = r * 0.3;
            bool blink = (int)(legPhase * 7) % 160 < 5 && wet < 0.5;
            if (blink)
            {
                var lid = new Pen(Brushes.White, r * 0.08) { LineCap = PenLineCap.Round };
                ctx.DrawLine(lid, new Point(x - ex - er, ey), new Point(x - ex + er, ey));
                ctx.DrawLine(lid, new Point(x + ex - er, ey), new Point(x + ex + er, ey));
            }
            else
            {
                ctx.DrawEllipse(Brushes.White, null, new Point(x - ex, ey), er, er * 1.1);
                ctx.DrawEllipse(Brushes.White, null, new Point(x + ex, ey), er, er * 1.1);
                ctx.DrawEllipse(PupilFill, null, new Point(x - ex + er * 0.2, ey + er * 0.15), er * 0.5, er * 0.55);
                ctx.DrawEllipse(PupilFill, null, new Point(x + ex + er * 0.2, ey + er * 0.15), er * 0.5, er * 0.55);
                ctx.DrawEllipse(Brushes.White, null, new Point(x - ex + er * 0.35, ey - er * 0.05), er * 0.15, er * 0.15);
                ctx.DrawEllipse(Brushes.White, null, new Point(x + ex + er * 0.35, ey - er * 0.05), er * 0.15, er * 0.15);
            }

            // Rosy cheeks and a smile (an "o" while soaking wet)
            ctx.DrawEllipse(CheekFill, null, new Point(x - r * 0.62, y + r * 0.3), r * 0.18, r * 0.11);
            ctx.DrawEllipse(CheekFill, null, new Point(x + r * 0.62, y + r * 0.3), r * 0.18, r * 0.11);
            var mouthPen = new Pen(Brushes.White, r * 0.09) { LineCap = PenLineCap.Round };
            if (wet > 0.5)
                ctx.DrawEllipse(null, mouthPen, new Point(x, y + r * 0.42), r * 0.12, r * 0.15);
            else
            {
                var smile = new StreamGeometry();
                using (var g = smile.Open())
                {
                    g.BeginFigure(new Point(x - r * 0.28, y + r * 0.32), false);
                    g.ArcTo(new Point(x + r * 0.28, y + r * 0.32), new Size(r * 0.32, r * 0.3), 0, false, SweepDirection.CounterClockwise);
                    g.EndFigure(false);
                }
                ctx.DrawGeometry(null, mouthPen, smile);
            }
        }

        // Drips falling off a wet spider (not rotated, they fall straight down)
        if (wet > 0.05)
            for (int i = 0; i < 3; i++)
            {
                double dy = (legPhase * 6 + i * 13) % 30 / 30;
                ctx.DrawEllipse(WaterFill, null, new Point(x + (i - 1) * r * 0.7, y + r * (1.1 + dy * 1.2)), r * 0.08 * wet, r * 0.13 * wet);
            }
    }

    // ══════════════════════════════════════════════════════════════
    //  The house, the spout, the weather
    // ══════════════════════════════════════════════════════════════

    private static void DrawHouse(DrawingContext ctx, double l, double r, double top, double ground, double s)
    {
        // Wall with a few board lines
        ctx.DrawRectangle(WallFill, null, new Rect(l, top, r - l, ground - top + 4 * s));
        var line = new Pen(WallLine, 2 * s);
        for (double y = top + 40 * s; y < ground; y += 40 * s)
            ctx.DrawLine(line, new Point(l, y), new Point(r, y));

        // Roof
        var roof = new StreamGeometry();
        using (var g = roof.Open())
        {
            g.BeginFigure(new Point(l - 30 * s, top + 4 * s), true);
            g.LineTo(new Point((l + r) / 2, top - 150 * s));
            g.LineTo(new Point(r + 14 * s, top + 4 * s));
            g.EndFigure(true);
        }
        ctx.DrawGeometry(RoofFill, null, roof);

        // A window with a cross frame
        double wx = l + (r - l) * 0.22, wy = top + 70 * s, ww = (r - l) * 0.5, wh = 110 * s;
        ctx.DrawRectangle(WindowFill, new Pen(FrameFill, 8 * s), new Rect(wx, wy, ww, wh));
        ctx.DrawLine(new Pen(FrameFill, 6 * s), new Point(wx + ww / 2, wy), new Point(wx + ww / 2, wy + wh));
        ctx.DrawLine(new Pen(FrameFill, 6 * s), new Point(wx, wy + wh / 2), new Point(wx + ww, wy + wh / 2));
    }

    private static void DrawSpout(DrawingContext ctx, double wallR, double pipeX, double pipeW,
                                  double gutterY, double mouthX, double mouthY, double s)
    {
        // Gutter along the roof edge, round into the top of the pipe
        ctx.DrawRectangle(PipeShade, null, new RoundedRect(new Rect(wallR - 120 * s, gutterY - 14 * s, pipeX - wallR + 120 * s + pipeW / 2, 22 * s), 6 * s));

        // Down pipe, then an elbow out to the mouth
        double bend = mouthY - 30 * s;
        ctx.DrawRectangle(PipeFill, null, new Rect(pipeX - pipeW / 2, gutterY, pipeW, bend - gutterY));
        ctx.DrawRectangle(PipeShine, null, new Rect(pipeX - pipeW * 0.3, gutterY, pipeW * 0.15, bend - gutterY));
        var elbow = new Pen(PipeFill, pipeW) { LineCap = PenLineCap.Flat, LineJoin = PenLineJoin.Round };
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(new Point(pipeX, bend - 1), false);
            g.QuadraticBezierTo(new Point(pipeX, mouthY), new Point(mouthX, mouthY));
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, elbow, geo);
        ctx.DrawEllipse(PipeShade, null, new Point(mouthX, mouthY), pipeW * 0.22, pipeW / 2);

        // Brackets holding it to the wall
        for (int i = 1; i <= 3; i++)
        {
            double y = gutterY + (bend - gutterY) * i / 4;
            ctx.DrawRectangle(PipeShade, null, new Rect(wallR, y - 4 * s, pipeX - wallR + pipeW / 2, 8 * s));
        }
    }

    /// <summary>Water pouring out of the spout's mouth in an arc of drops.</summary>
    private void DrawGush(DrawingContext ctx, double x, double y, double s, double amount)
    {
        for (int i = 0; i < 14; i++)
        {
            double u = (Tick * 0.05 + i / 14.0) % 1;
            double dx = u * 160 * s;
            double dy = u * u * 70 * s;
            double r = (9 - u * 4) * s * amount;
            ctx.DrawEllipse(WaterFill, null, new Point(x + dx, y + dy), r * 1.3, r);
        }
    }

    private void DrawStorm(DrawingContext ctx, double w, double h, double s, double amount)
    {
        // Big grey clouds slide in from both sides
        double slide = (1 - amount) * w * 0.6;
        DrawGreyCloud(ctx, w * 0.18 - slide, h * 0.10, s * 2.6);
        DrawGreyCloud(ctx, w * 0.50 - slide * 0.5, h * 0.06, s * 2.2);
        DrawGreyCloud(ctx, w * 0.82 + slide, h * 0.12, s * 2.8);
    }

    private static void DrawGreyCloud(DrawingContext ctx, double x, double y, double s)
    {
        double r = 30 * s;
        ctx.DrawEllipse(StormCloud, null, new Point(x, y), r, r * 0.7);
        ctx.DrawEllipse(StormCloud, null, new Point(x - r * 0.75, y + r * 0.25), r * 0.7, r * 0.55);
        ctx.DrawEllipse(StormCloud, null, new Point(x + r * 0.75, y + r * 0.15), r * 0.8, r * 0.55);
    }

    private void DrawRain(DrawingContext ctx, double w, double ground, double s, double amount)
    {
        var pen = new Pen(RainFill, 3 * s) { LineCap = PenLineCap.Round };
        int count = (int)(Drops.Length * amount);
        for (int i = 0; i < count; i++)
        {
            var (fx, phase, speed) = Drops[i];
            double y = Wrap(phase * ground + Tick * 9 * s * speed, ground, 0);
            double x = fx * w - y * 0.12;
            ctx.DrawLine(pen, new Point(x, y), new Point(x - 4 * s, y + 22 * s));
        }
    }

    private void DrawSunFace(DrawingContext ctx, double cx, double cy, double r, double amount)
    {
        var pen = new Pen(FaceFill, r * 0.08 * amount) { LineCap = PenLineCap.Round };
        bool blink = Tick % 200 < 8;
        double ex = r * 0.33, ey = cy - r * 0.15;
        if (blink)
        {
            ctx.DrawLine(pen, new Point(cx - ex - r * 0.1, ey), new Point(cx - ex + r * 0.1, ey));
            ctx.DrawLine(pen, new Point(cx + ex - r * 0.1, ey), new Point(cx + ex + r * 0.1, ey));
        }
        else
        {
            ctx.DrawEllipse(FaceFill, null, new Point(cx - ex, ey), r * 0.08, r * 0.1);
            ctx.DrawEllipse(FaceFill, null, new Point(cx + ex, ey), r * 0.08, r * 0.1);
        }
        var smile = new StreamGeometry();
        using (var g = smile.Open())
        {
            g.BeginFigure(new Point(cx - r * 0.4, cy + r * 0.15), false);
            g.ArcTo(new Point(cx + r * 0.4, cy + r * 0.15), new Size(r * 0.45, r * 0.4), 0, false, SweepDirection.CounterClockwise);
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, pen, smile);
    }

    private static void DrawSparkle(DrawingContext ctx, double x, double y, double r)
    {
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

    private void DrawLabel(DrawingContext ctx, double w, double y, double s, int part)
    {
        string text = $"♪  {Lines[part]}  ♪";
        double bounce = Math.Sin(Tick * 0.06) * 4 * s;
        double fontSize = Math.Max(28, 40 * s);
        var typeface = new Typeface("Inter", FontStyle.Normal, FontWeight.ExtraBold);

        var fmt = new FormattedText(text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, typeface, fontSize, Brushes.White);

        double tx = (w - fmt.Width) / 2;
        double ty = y + bounce;

        double pad = 14 * s;
        ctx.DrawRectangle(LabelFill, new Pen(Brushes.White, 3 * s),
            new RoundedRect(
                new Rect(tx - pad, ty - pad * 0.4, fmt.Width + pad * 2, fmt.Height + pad * 0.8),
                pad));

        ctx.DrawText(fmt, new Point(tx, ty));
    }
}
