using Avalonia;
using Avalonia.Media;

namespace PiClock.Controls;

/// <summary>
/// The "Head, Shoulders, Knees and Toes" scene: a cartoon kid on the sunny backdrop touches
/// each body part as it is sung (squatting down for knees and toes), the word showing at the
/// top. Like the song, it gets faster and faster. It waves hello at the start and throws its
/// arms up for the end.
/// </summary>
public class HeadShouldersKneesToes : SongScene
{
    private enum Pose { Head, Shoulders, Knees, Toes, Eyes, Ears, Mouth, Nose, Wave, Yay }

    // One verse: head, shoulders, knees and toes, knees and toes (twice), and eyes and ears
    // and mouth and nose, head, shoulders, knees and toes, knees and toes
    private static readonly Pose[] Verse =
    [
        Pose.Head, Pose.Shoulders, Pose.Knees, Pose.Toes, Pose.Knees, Pose.Toes,
        Pose.Head, Pose.Shoulders, Pose.Knees, Pose.Toes, Pose.Knees, Pose.Toes,
        Pose.Eyes, Pose.Ears, Pose.Mouth, Pose.Nose,
        Pose.Head, Pose.Shoulders, Pose.Knees, Pose.Toes, Pose.Knees, Pose.Toes,
    ];

    private static readonly Pose[] IconLoop = [Pose.Head, Pose.Shoulders, Pose.Knees, Pose.Toes];

    private static readonly string[] Words =
        ["Head!", "Shoulders!", "Knees!", "Toes!", "Eyes!", "Ears!", "Mouth!", "Nose!", "Hello!", "Yay!"];

    private static readonly IBrush[] WordFill =
        new[] { "#E53935", "#FB8C00", "#1E88E5", "#8E24AA", "#00897B", "#F4511E", "#D81B60", "#3949AB", "#43A047", "#E53935" }
            .Select(c => (IBrush)new SolidColorBrush(Color.Parse(c))).ToArray();

    private static readonly IBrush SkinFill   = new SolidColorBrush(Color.Parse("#F5C6A0"));
    private static readonly IBrush SkinShade  = new SolidColorBrush(Color.Parse("#E3A87F"));
    private static readonly IBrush HairFill   = new SolidColorBrush(Color.Parse("#6D4C2F"));
    private static readonly IBrush ShirtFill  = new SolidColorBrush(Color.Parse("#E53935"));
    private static readonly IBrush ShortsFill = new SolidColorBrush(Color.Parse("#1E88E5"));
    private static readonly IBrush ShoeFill   = new SolidColorBrush(Color.Parse("#FDD835"));
    private static readonly IBrush FaceFill   = new SolidColorBrush(Color.Parse("#3B2A20"));
    private static readonly IBrush MouthFill  = new SolidColorBrush(Color.Parse("#B23A48"));
    private static readonly IBrush CheekFill  = new SolidColorBrush(Color.FromArgb(120, 255, 110, 140));
    private static readonly IBrush StarFill   = new SolidColorBrush(Color.FromArgb(240, 255, 255, 255));

    public override IReadOnlyList<string>? LightColors { get; } =
        ["#E53935", "#FDD835", "#1E88E5", "#8E24AA"];

    // ── Timing: touches per second, rising through the song as it speeds up ──
    private const double StartRate = 1.1, EndRate = 2.4;
    private const double IntroSeconds = 1.5, OutroSeconds = 2.5;

    public override void Render(DrawingContext context)
    {
        if (!IsPlaying) return;

        var sz = Bounds.Size;
        if (sz.Width <= 0 || sz.Height <= 0) return;

        double w = sz.Width, h = sz.Height;
        double s = Math.Min(w / 1280.0, h / 800.0);    // uniform scale

        double grassTop = h * 0.80;
        DrawBackdrop(context, w, h, grassTop, s);

        double t = Tick * TickMs / 1000.0;
        var (from, to, move, hold) = PoseAt(t, SongDuration);

        // A little hop on every touch
        double hop = hold * Math.Sin(Math.Min(1, hold * 2) * Math.PI) * 8 * s;
        DrawKid(context, w / 2, h * 0.92 - hop, s * 1.2, from, to, move, hold, t);

        DrawLabel(context, w, h * 0.04, s, to);
    }

    /// <summary>The pose being left, the pose being reached, how far the move is (0..1) and
    /// how long the touch has been held (0..1 of the rest of the beat).</summary>
    private static (Pose From, Pose To, double Move, double Hold) PoseAt(double t, double length)
    {
        if (t < IntroSeconds)
            return (Pose.Wave, Pose.Wave, 1, 0);

        double singing = Math.Max(1, length - IntroSeconds - OutroSeconds);
        double u = t - IntroSeconds;
        if (u > singing)
            return (Pose.Toes, Pose.Yay, Math.Clamp((u - singing) / 0.5, 0, 1), 0);

        // Touch count is the integral of a rate rising linearly from StartRate to EndRate
        double beats = StartRate * u + (EndRate - StartRate) * u * u / (2 * singing);
        int n = (int)beats;
        double f = beats - n;
        var from = n == 0 ? Pose.Wave : Verse[(n - 1) % Verse.Length];
        var to = Verse[n % Verse.Length];
        return (from, to, Math.Clamp(f / 0.4, 0, 1), Math.Clamp((f - 0.4) / 0.6, 0, 1));
    }

    // ══════════════════════════════════════════════════════════════
    //  The kid: legs and arms are two-bone chains bent toward their targets
    // ══════════════════════════════════════════════════════════════

    private const double Thigh = 100, Shin = 100, UpperArm = 100, Forearm = 100;

    private static double Squat(Pose p) => p switch { Pose.Knees => 0.55, Pose.Toes => 1.0, _ => 0 };

    /// <summary>Body joints for a squat amount, kid centred on cx with feet on feetY.
    /// u is the scale (1 = 1280×800 sizing).</summary>
    private readonly record struct Body(double Cx, double HipY, double ShoulderY, double HeadY, double FeetY, double U)
    {
        public Point Shoulder(int side) => new(Cx + side * 70 * U, ShoulderY + 14 * U);
        public Point Hip(int side)      => new(Cx + side * 32 * U, HipY);
        public Point Foot(int side)     => new(Cx + side * 70 * U, FeetY - 12 * U);
        public Point Knee(int side)     => Bend(Hip(side), Foot(side), Thigh * U, Shin * U, Cx, side);
    }

    private static Body MakeBody(double cx, double feetY, double squat, double u)
    {
        double hipY = feetY - 195 * u + squat * 140 * u;
        double shoulderY = hipY - 125 * u;
        return new Body(cx, hipY, shoulderY, shoulderY - 82 * u, feetY, u);
    }

    /// <summary>Where a hand wants to be for a pose, on the body as it is now.</summary>
    private static Point HandTarget(Pose p, int side, Body b, double t)
    {
        double u = b.U, cx = b.Cx;
        Point Rest() => new(cx + side * 105 * u, b.HipY + 5 * u);
        return p switch
        {
            Pose.Head      => new(cx + side * 40 * u, b.HeadY - 58 * u),
            Pose.Shoulders => new(cx + side * 74 * u, b.ShoulderY + 4 * u),
            Pose.Knees     => b.Knee(side) + new Point(0, -6 * u),
            Pose.Toes      => b.Foot(side) + new Point(side * 24 * u, -8 * u),
            Pose.Eyes      => new(cx + side * 26 * u, b.HeadY - 8 * u),
            Pose.Ears      => new(cx + side * 74 * u, b.HeadY + 2 * u),
            Pose.Mouth     => new(cx + side * 15 * u, b.HeadY + 38 * u),
            Pose.Nose      => side > 0 ? new(cx + 4 * u, b.HeadY + 16 * u) : Rest(),
            Pose.Wave      => side > 0 ? new(cx + 150 * u + Math.Sin(t * 9) * 22 * u, b.HeadY - 55 * u) : Rest(),
            Pose.Yay       => new(cx + side * 150 * u, b.HeadY - 120 * u),
            _              => Rest()
        };
    }

    /// <summary>Middle joint of a two-bone chain from a to target, bent away from the body
    /// centre on that side. The chain reaches as far toward the target as it can.</summary>
    private static Point Bend(Point a, Point target, double l1, double l2, double cx, int side)
    {
        var v = target - a;
        double d = Math.Clamp(Math.Sqrt(v.X * v.X + v.Y * v.Y), Math.Abs(l1 - l2) + 15, l1 + l2 - 0.5);
        double baseAngle = Math.Atan2(v.Y, v.X);
        double spread = Math.Acos(Math.Clamp((l1 * l1 + d * d - l2 * l2) / (2 * l1 * d), -1, 1));

        var j1 = new Point(a.X + Math.Cos(baseAngle + spread) * l1, a.Y + Math.Sin(baseAngle + spread) * l1);
        var j2 = new Point(a.X + Math.Cos(baseAngle - spread) * l1, a.Y + Math.Sin(baseAngle - spread) * l1);
        return side * (j1.X - cx) >= side * (j2.X - cx) ? j1 : j2;
    }

    /// <summary>End of the chain: the target, or as near to it as the limb reaches.</summary>
    private static Point Reach(Point a, Point joint, Point target, double l2)
    {
        var v = target - joint;
        double d = Math.Sqrt(v.X * v.X + v.Y * v.Y);
        return d < 0.001 ? target : joint + v * (l2 / d);
    }

    private static void DrawKid(DrawingContext ctx, double cx, double feetY, double u,
                                Pose from, Pose to, double move, double hold, double t)
    {
        double e = move * move * (3 - 2 * move);
        var body = MakeBody(cx, feetY, Squat(from) + (Squat(to) - Squat(from)) * e, u);

        // Legs: shorts on the thigh, skin below, a shoe on each foot
        var thighPen = new Pen(ShortsFill, 34 * u) { LineCap = PenLineCap.Round };
        var shinPen  = new Pen(SkinFill, 26 * u) { LineCap = PenLineCap.Round };
        for (int side = -1; side <= 1; side += 2)
        {
            var hip = body.Hip(side);
            var knee = body.Knee(side);
            var foot = Reach(hip, knee, body.Foot(side), Shin * u);
            ctx.DrawLine(shinPen, knee, foot);
            ctx.DrawLine(thighPen, hip, knee);
            ctx.DrawEllipse(ShoeFill, null, new Point(foot.X + side * 14 * u, body.FeetY - 4 * u), 30 * u, 15 * u);
        }

        // Shorts and shirt
        ctx.DrawRectangle(ShortsFill, null,
            new RoundedRect(new Rect(cx - 62 * u, body.HipY - 30 * u, 124 * u, 50 * u), 14 * u));
        ctx.DrawRectangle(ShirtFill, null,
            new RoundedRect(new Rect(cx - 75 * u, body.ShoulderY, 150 * u, body.HipY - body.ShoulderY), 30 * u, 30 * u, 14 * u, 14 * u));
        ctx.DrawEllipse(StarFill, null, new Point(cx, body.ShoulderY + 55 * u), 16 * u, 16 * u);

        // Neck and head
        ctx.DrawRectangle(SkinShade, null, new Rect(cx - 16 * u, body.HeadY + 50 * u, 32 * u, body.ShoulderY - body.HeadY - 46 * u));
        DrawHead(ctx, cx, body.HeadY, u, to, move >= 1, t);

        // Arms last, so hands sit on top of whatever they touch
        var sleevePen = new Pen(ShirtFill, 30 * u) { LineCap = PenLineCap.Round };
        var armPen    = new Pen(SkinFill, 22 * u) { LineCap = PenLineCap.Round };
        for (int side = -1; side <= 1; side += 2)
        {
            var a = HandTarget(from, side, body, t);
            var b = HandTarget(to, side, body, t);
            var target = new Point(a.X + (b.X - a.X) * e, a.Y + (b.Y - a.Y) * e);

            var sh = body.Shoulder(side);
            var elbow = Bend(sh, target, UpperArm * u, Forearm * u, cx, side);
            var hand = Reach(sh, elbow, target, Forearm * u);
            ctx.DrawLine(armPen, elbow, hand);
            ctx.DrawLine(sleevePen, sh, elbow);
            ctx.DrawEllipse(SkinFill, null, hand, 18 * u, 18 * u);

            // Sparkles where the hand lands
            if (hold is > 0 and < 0.5 && to != Pose.Wave && (to != Pose.Nose || side > 0))
                DrawStars(ctx, hand, u, hold * 2);
        }
    }

    private static void DrawHead(DrawingContext ctx, double cx, double cy, double u, Pose pose, bool arrived, double t)
    {
        double r = 70 * u;

        // Ears, face, curly hair on top
        ctx.DrawEllipse(SkinShade, null, new Point(cx - r, cy + 4 * u), 15 * u, 20 * u);
        ctx.DrawEllipse(SkinShade, null, new Point(cx + r, cy + 4 * u), 15 * u, 20 * u);
        ctx.DrawEllipse(SkinFill, null, new Point(cx, cy), r, r);
        for (int i = 0; i < 7; i++)
        {
            double a = Math.PI * (1.08 + i * 0.14);
            ctx.DrawEllipse(HairFill, null, new Point(cx + Math.Cos(a) * r * 0.82, cy + Math.Sin(a) * r * 0.82), 22 * u, 20 * u);
        }

        // Eyes: shut tight while the hands cover them, otherwise blinking now and then
        double ex = 25 * u, ey = cy - 8 * u;
        bool shut = pose == Pose.Eyes && arrived || (int)(t * 50) % 230 < 6;
        if (shut)
        {
            var lid = new Pen(FaceFill, 4 * u) { LineCap = PenLineCap.Round };
            ctx.DrawLine(lid, new Point(cx - ex - 9 * u, ey), new Point(cx - ex + 9 * u, ey));
            ctx.DrawLine(lid, new Point(cx + ex - 9 * u, ey), new Point(cx + ex + 9 * u, ey));
        }
        else
        {
            ctx.DrawEllipse(FaceFill, null, new Point(cx - ex, ey), 8 * u, 10 * u);
            ctx.DrawEllipse(FaceFill, null, new Point(cx + ex, ey), 8 * u, 10 * u);
            ctx.DrawEllipse(Brushes.White, null, new Point(cx - ex + 3 * u, ey - 4 * u), 3 * u, 3 * u);
            ctx.DrawEllipse(Brushes.White, null, new Point(cx + ex + 3 * u, ey - 4 * u), 3 * u, 3 * u);
        }

        ctx.DrawEllipse(SkinShade, null, new Point(cx, cy + 14 * u), 8 * u, 6 * u);   // nose
        ctx.DrawEllipse(CheekFill, null, new Point(cx - 44 * u, cy + 18 * u), 12 * u, 7 * u);
        ctx.DrawEllipse(CheekFill, null, new Point(cx + 44 * u, cy + 18 * u), 12 * u, 7 * u);

        // Singing mouth, opening and closing
        double open = 0.35 + 0.65 * Math.Abs(Math.Sin(t * 6));
        ctx.DrawEllipse(MouthFill, null, new Point(cx, cy + 36 * u), 16 * u, 10 * u * open + 2 * u);
    }

    private static void DrawStars(DrawingContext ctx, Point at, double u, double k)
    {
        for (int i = 0; i < 5; i++)
        {
            double a = i * Math.PI * 2 / 5 - Math.PI / 2;
            double d = (24 + 30 * k) * u;
            double r = (9 * (1 - k) + 2) * u;
            var c = new Point(at.X + Math.Cos(a) * d, at.Y + Math.Sin(a) * d);
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                double q = r * 0.25;
                g.BeginFigure(new Point(c.X, c.Y - r), true);
                g.LineTo(new Point(c.X + q, c.Y - q));
                g.LineTo(new Point(c.X + r, c.Y));
                g.LineTo(new Point(c.X + q, c.Y + q));
                g.LineTo(new Point(c.X, c.Y + r));
                g.LineTo(new Point(c.X - q, c.Y + q));
                g.LineTo(new Point(c.X - r, c.Y));
                g.LineTo(new Point(c.X - q, c.Y - q));
                g.EndFigure(true);
            }
            ctx.DrawGeometry(StarFill, null, geo);
        }
    }

    private void DrawLabel(DrawingContext ctx, double w, double y, double s, Pose pose)
    {
        string text = $"♪  {Words[(int)pose]}  ♪";
        double bounce = Math.Sin(Tick * 0.06) * 4 * s;
        double fontSize = Math.Max(28, 40 * s);
        var typeface = new Typeface("Inter", FontStyle.Normal, FontWeight.ExtraBold);

        var fmt = new FormattedText(text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, typeface, fontSize, Brushes.White);

        double tx = (w - fmt.Width) / 2;
        double ty = y + bounce;

        double pad = 14 * s;
        ctx.DrawRectangle(WordFill[(int)pose], new Pen(Brushes.White, 3 * s),
            new RoundedRect(
                new Rect(tx - pad, ty - pad * 0.4, fmt.Width + pad * 2, fmt.Height + pad * 0.8),
                pad));

        ctx.DrawText(fmt, new Point(tx, ty));
    }

    /// <summary>The kid for the picker button: head, shoulders, knees and toes on a loop.</summary>
    internal static void DrawIcon(DrawingContext ctx, Point c, double r, double t)
    {
        double beats = t * 1.6;
        int n = (int)beats;
        double f = beats - n;
        var from = IconLoop[(n + IconLoop.Length - 1) % IconLoop.Length];
        var to = IconLoop[n % IconLoop.Length];
        double u = r * 1.55 / 480;
        DrawKid(ctx, c.X, c.Y + r * 0.78, u, from, to, Math.Clamp(f / 0.4, 0, 1), Math.Clamp((f - 0.4) / 0.6, 0, 1), t);
    }
}
