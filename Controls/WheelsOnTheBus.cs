using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PiClock.Controls;

/// <summary>
/// A colorful "Wheels on the Bus" animation — triggered by a tap.
/// A bright school bus drives across with spinning wheels, swishing wipers,
/// an opening door, and bouncing passengers. Song lyrics scroll at the top.
/// Runs for the length of the song, then the clock resumes.  Tap to dismiss early.
/// </summary>
public class WheelsOnTheBus : SongScene
{
    // ── Timing computed from song duration ──
    private int EnterTicks    => Math.Min(100, TotalTicks / 10);            // ~2s or 10%
    private int ExitTicks     => Math.Min(600, TotalTicks / 3);             // last ~12s
    private int ExitStartTick => Math.Max(EnterTicks + 50, TotalTicks - ExitTicks);
    private int TicksPerVerse => Math.Max(1, TotalTicks / 4);               // 4 verses

    /// <summary>How much of the bus is still on screen when the song ends (back bumper only).</summary>
    private const double VisibleAtEnd = 0.10;

    // ── Verse lyrics (each ~2.5 s = 125 ticks) ──
    private static readonly string[] Lyrics =
    [
        "The wheels on the bus go round and round!",
        "The wipers on the bus go swish, swish, swish!",
        "The door on the bus goes open and shut!",
        "The people on the bus go up and down!"
    ];

    private int VerseIndex => Math.Clamp(Tick / TicksPerVerse, 0, 3);

    // ── Static brushes (allocated once) ──
    private static readonly IBrush RoadFill     = new SolidColorBrush(Color.Parse("#555555"));
    private static readonly IBrush DashFill     = new SolidColorBrush(Color.Parse("#EEEE44"));
    private static readonly IBrush BusYellow    = new SolidColorBrush(Color.Parse("#FFD700"));
    private static readonly IBrush BusTrimFill  = new SolidColorBrush(Color.Parse("#DAA520"));
    private static readonly IBrush WindowFill   = new SolidColorBrush(Color.Parse("#87CEEB"));
    private static readonly IBrush TireFill     = new SolidColorBrush(Color.Parse("#333333"));
    private static readonly IBrush HubFill      = new SolidColorBrush(Color.Parse("#999999"));
    private static readonly IBrush DoorFill     = new SolidColorBrush(Color.Parse("#CC4444"));
    private static readonly IBrush SkinFill     = new SolidColorBrush(Color.Parse("#FFDBAC"));
    private static readonly IBrush StopRedFill  = new SolidColorBrush(Color.Parse("#CC0000"));
    private static readonly IBrush TailLightFill = new SolidColorBrush(Color.Parse("#FF4444"));
    private static readonly IBrush PillFill     = new SolidColorBrush(Color.FromArgb(100, 0, 0, 0));
    private static readonly IBrush ShadowFill   = new SolidColorBrush(Color.FromArgb(100, 0, 0, 0));

    private static readonly IBrush[] ShirtColors =
    [
        new SolidColorBrush(Color.Parse("#FF6B6B")),
        new SolidColorBrush(Color.Parse("#4ECDC4")),
        new SolidColorBrush(Color.Parse("#FFE66D")),
        new SolidColorBrush(Color.Parse("#A8E6CF")),
    ];

    // ══════════════════════════════════════════════════════════════
    //  Main render
    // ══════════════════════════════════════════════════════════════

    public override void Render(DrawingContext context)
    {
        if (!IsPlaying) return;

        var sz = Bounds.Size;
        if (sz.Width <= 0 || sz.Height <= 0) return;

        double w = sz.Width, h = sz.Height;
        double s = Math.Min(w / 1280.0, h / 800.0);    // uniform scale

        // Bus dimensions and travel
        double bw = 480 * s;
        double margin = 100 * s;
        double centerX = (w - bw) / 2;

        // 3-phase travel: enter → park in center → drive off
        double busX;
        if (Tick <= EnterTicks)
        {
            // Drive from off-screen left to center (ease-out: decelerate to stop)
            double t = EaseOut((double)Tick / EnterTicks);
            busX = (-bw - margin) + t * (centerX + bw + margin);
        }
        else if (Tick <= ExitStartTick)
        {
            // Parked in center — the show!
            busX = centerX;
        }
        else
        {
            // Drive off to the right (ease-in: accelerate away). Timed so that when the song
            // ends only the back bumper is still visible; past the end it keeps going.
            double t = EaseIn((double)(Tick - ExitStartTick) / (TotalTicks - ExitStartTick));
            double endX = w - bw * VisibleAtEnd;
            busX = centerX + t * (endX - centerX);
        }

        // Scene regions
        double roadTop  = h * 0.62;
        double roadH    = h * 0.14;
        double grassTop = roadTop + roadH;
        double busH     = 210 * s;
        double busY     = roadTop + roadH * 0.35 - busH;

        // 1) Sky, sun with rotating rays, drifting clouds, grass
        DrawBackdrop(context, w, h, grassTop, s);

        // 2) Road with dashed center line
        context.DrawRectangle(RoadFill, null, new Rect(0, roadTop, w, roadH));
        var dashPen = new Pen(DashFill, 4 * s)
        {
            DashStyle = new DashStyle([18, 12], Tick * 0.8 % 30)
        };
        context.DrawLine(dashPen,
            new Point(0, roadTop + roadH / 2),
            new Point(w, roadTop + roadH / 2));

        // 3) Exhaust puffs (drawn before bus so bus body occludes near ones)
        DrawExhaust(context, busX - 10 * s, busY + busH - 40 * s, s);

        // 4) The bus!
        DrawBus(context, busX, busY, bw, busH, s);

        // 5) Song lyrics at the top
        DrawLyrics(context, w, h * 0.04, s);
    }

    // ══════════════════════════════════════════════════════════════
    //  Scene elements
    // ══════════════════════════════════════════════════════════════

    private void DrawBus(DrawingContext ctx, double x, double y, double bw, double bh, double s)
    {
        var trim = new Pen(BusTrimFill, 3 * s);

        // ── Body (yellow, rounded corners) ──
        double cr = 14 * s;
        ctx.DrawRectangle(BusYellow, trim,
            new RoundedRect(new Rect(x, y, bw, bh), cr));

        // ── Bumper strip at bottom ──
        double stripH = 22 * s;
        ctx.DrawRectangle(BusTrimFill, null,
            new RoundedRect(new Rect(x - 8 * s, y + bh - stripH, bw + 16 * s, stripH), 6 * s));

        // ── Front windshield (right side = front of bus) ──
        double wsW = 60 * s, wsH = 80 * s;
        double wsX = x + bw - 72 * s, wsY = y + 22 * s;
        ctx.DrawRectangle(WindowFill, trim,
            new RoundedRect(new Rect(wsX, wsY, wsW, wsH), 8 * s));

        // ── Windshield wiper ──
        bool wiperVerse = VerseIndex == 1;
        double wSpeed = wiperVerse ? 0.14 : 0.06;
        double wSwing = wiperVerse ? 55 : 35;
        double wDeg   = Math.Sin(Tick * wSpeed) * wSwing;
        double wRad   = (-90 + wDeg) * Math.PI / 180;
        double wpx = wsX + wsW * 0.5, wpy = wsY + wsH - 5 * s;
        double wLen = 50 * s;
        ctx.DrawLine(new Pen(TireFill, 3 * s) { LineCap = PenLineCap.Round },
            new Point(wpx, wpy),
            new Point(wpx + Math.Cos(wRad) * wLen, wpy + Math.Sin(wRad) * wLen));

        // ── Side windows with bouncing passengers ──
        double winW = 52 * s, winH = 62 * s, gap = 14 * s;
        double winX0 = x + 50 * s, winY = y + 22 * s;
        for (int i = 0; i < 4; i++)
        {
            double wx = winX0 + i * (winW + gap);
            ctx.DrawRectangle(WindowFill, trim,
                new RoundedRect(new Rect(wx, winY, winW, winH), 5 * s));
            DrawPassenger(ctx, wx + winW / 2, winY, winH, s, i);
        }

        // ── Door (near front, after last window) ──
        double dw = 38 * s, dh = bh - stripH - 18 * s;
        double dx = winX0 + 4 * (winW + gap) + 10 * s;
        double dy = y + 18 * s;
        bool doorVerse = VerseIndex == 2;
        double dSpeed  = doorVerse ? 0.08 : 0.03;
        double doorOpen = (Math.Sin(Tick * dSpeed) + 1) / 2;   // 0→1
        double offset   = doorOpen * dw * 0.45;

        // Door frame always visible
        ctx.DrawRectangle(null, trim,
            new RoundedRect(new Rect(dx, dy, dw, dh), 4 * s));
        // Sliding door panel
        ctx.DrawRectangle(DoorFill, trim,
            new RoundedRect(new Rect(dx + offset, dy, Math.Max(1, dw - offset), dh), 4 * s));

        // ── Headlights ──
        double lR = 11 * s;
        ctx.DrawEllipse(SunFill, trim,
            new Point(x + bw + 4 * s, y + bh - 48 * s), lR, lR);
        ctx.DrawEllipse(TailLightFill, trim,
            new Point(x - 4 * s, y + bh - 48 * s), lR * 0.8, lR * 0.8);

        // ── Stop sign (folds out periodically) ──
        if (Math.Sin(Tick * 0.025) > 0.3)
        {
            double sw = 32 * s, sh = 26 * s;
            double sx = x - sw - 6 * s, sy = y + 45 * s;
            ctx.DrawRectangle(StopRedFill, new Pen(Brushes.White, 2 * s),
                new RoundedRect(new Rect(sx, sy, sw, sh), 3 * s));
            var stopTxt = new FormattedText("STOP",
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Inter", FontStyle.Normal, FontWeight.Bold),
                11 * s, Brushes.White);
            ctx.DrawText(stopTxt, new Point(
                sx + (sw - stopTxt.Width) / 2,
                sy + (sh - stopTxt.Height) / 2));
        }

        // ── Wheels — round and round! ──
        double wheelR = 34 * s;
        double wy = y + bh - 3 * s;
        DrawWheel(ctx, x + 95 * s, wy, wheelR, s);
        DrawWheel(ctx, x + bw - 85 * s, wy, wheelR, s);
    }

    private void DrawWheel(DrawingContext ctx, double cx, double cy, double r, double s)
    {
        // Tire
        ctx.DrawEllipse(TireFill, null, new Point(cx, cy), r, r);

        // Hub cap
        double hr = r * 0.45;
        ctx.DrawEllipse(HubFill, null, new Point(cx, cy), hr, hr);

        // Center bolt
        ctx.DrawEllipse(TireFill, null, new Point(cx, cy), r * 0.12, r * 0.12);

        // Spokes — spin faster during Wheels verse
        bool active = VerseIndex == 0;
        double rot = Tick * (active ? 0.25 : 0.15);
        var spokePen = new Pen(HubFill, 2.5 * s);
        for (int i = 0; i < 5; i++)
        {
            double a = rot + i * 72 * Math.PI / 180;
            ctx.DrawLine(spokePen, new Point(cx, cy),
                new Point(cx + Math.Cos(a) * r * 0.85, cy + Math.Sin(a) * r * 0.85));
        }
    }

    private void DrawPassenger(DrawingContext ctx, double cx, double winTop, double winH,
                                double s, int i)
    {
        // Bounce harder during People verse
        bool active = VerseIndex == 3;
        double amp = (active ? 10 : 5) * s;
        double bounce = Math.Sin(Tick * 0.08 + i * 1.5) * amp;
        double headR = 7 * s;
        double hy = winTop + winH * 0.32 + bounce;

        // Head
        ctx.DrawEllipse(SkinFill, null, new Point(cx, hy), headR, headR);

        // Eyes
        double er = 1.5 * s;
        ctx.DrawEllipse(TireFill, null, new Point(cx - 3 * s, hy - 1 * s), er, er);
        ctx.DrawEllipse(TireFill, null, new Point(cx + 3 * s, hy - 1 * s), er, er);

        // Shirt / body
        ctx.DrawRectangle(ShirtColors[i % ShirtColors.Length], null,
            new RoundedRect(new Rect(cx - 7 * s, hy + headR, 14 * s, winH * 0.4), 3 * s));
    }

    private void DrawExhaust(DrawingContext ctx, double x, double y, double s)
    {
        for (int i = 0; i < 4; i++)
        {
            double age = (Tick * 0.5 + i * 8) % 40;
            double px = x - age * 2 * s - i * 15 * s;
            double py = y - Math.Sin(age * 0.3) * 8 * s;
            double pr = (4 + age * 0.3) * s;
            byte alpha = (byte)Math.Clamp(180 - age * 4, 20, 180);
            ctx.DrawEllipse(
                new SolidColorBrush(Color.FromArgb(alpha, 180, 180, 180)),
                null, new Point(px, py), pr, pr * 0.8);
        }
    }

    private void DrawLyrics(DrawingContext ctx, double w, double y, double s)
    {
        string text = $"♪  {Lyrics[VerseIndex]}  ♪";
        double bounce = Math.Sin(Tick * 0.06) * 4 * s;
        double fontSize = Math.Max(28, 36 * s);
        var typeface = new Typeface("Inter", FontStyle.Normal, FontWeight.ExtraBold);

        var fmt = new FormattedText(text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, typeface, fontSize, Brushes.White);

        double tx = (w - fmt.Width) / 2;
        double ty = y + bounce;

        // Semi-transparent pill behind text for readability over clouds
        double pad = 14 * s;
        ctx.DrawRectangle(PillFill, null,
            new RoundedRect(
                new Rect(tx - pad, ty - pad * 0.4, fmt.Width + pad * 2, fmt.Height + pad * 0.8),
                pad));

        // Drop shadow
        var shadow = new FormattedText(text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, typeface, fontSize, ShadowFill);
        ctx.DrawText(shadow, new Point(tx + 2 * s, ty + 2 * s));

        // Main text
        ctx.DrawText(fmt, new Point(tx, ty));
    }
}
