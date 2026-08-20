using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PiClock.Controls;

/// <summary>
/// Renders an analog clock face with paddle-shaped hands:
/// thin neck from hub, smooth widening body, rounded tip.
/// Gold second hand with counterweight.
/// </summary>
public class AnalogClock : Control
{
    public static readonly StyledProperty<int> HoursProperty =
        AvaloniaProperty.Register<AnalogClock, int>(nameof(Hours));

    public static readonly StyledProperty<int> MinutesProperty =
        AvaloniaProperty.Register<AnalogClock, int>(nameof(Minutes));

    public static readonly StyledProperty<int> SecondsProperty =
        AvaloniaProperty.Register<AnalogClock, int>(nameof(Seconds));

    public int Hours
    {
        get => GetValue(HoursProperty);
        set => SetValue(HoursProperty, value);
    }

    public int Minutes
    {
        get => GetValue(MinutesProperty);
        set => SetValue(MinutesProperty, value);
    }

    public int Seconds
    {
        get => GetValue(SecondsProperty);
        set => SetValue(SecondsProperty, value);
    }

    static AnalogClock()
    {
        AffectsRender<AnalogClock>(HoursProperty, MinutesProperty, SecondsProperty);
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0) return;

        var center = new Point(size.Width / 2, size.Height / 2);
        var radius = Math.Min(size.Width, size.Height) / 2 * 0.97;

        var white    = Brushes.White;
        var dimWhite = new SolidColorBrush(Color.FromArgb(64, 255, 255, 255));
        var gold     = new SolidColorBrush(Color.Parse("#FFB800"));

        // ── Outer circle (dimmed) ──
        var circlePen = new Pen(dimWhite, Math.Max(2, radius * 0.012));
        context.DrawEllipse(null, circlePen, center, radius, radius);

        // ── Minute tick marks (dimmed, skip hour positions) ──
        for (int i = 0; i < 60; i++)
        {
            if (i % 5 == 0) continue;
            double angle = ToRadians(i * 6 - 90);
            var outer = OnCircle(center, radius * 0.95, angle);
            var inner = OnCircle(center, radius * 0.91, angle);
            context.DrawLine(new Pen(dimWhite, Math.Max(0.5, radius * 0.004)), inner, outer);
        }

        // ── Hour markers and numbers (dimmed) ──
        for (int i = 1; i <= 12; i++)
        {
            double angle = ToRadians(i * 30 - 90);

            var outer = OnCircle(center, radius * 0.95, angle);
            var inner = OnCircle(center, radius * 0.85, angle);
            context.DrawLine(new Pen(dimWhite, Math.Max(2, radius * 0.01)), inner, outer);

            var numPos = OnCircle(center, radius * 0.74, angle);
            var text = new FormattedText(
                i.ToString(),
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Inter", FontStyle.Normal, FontWeight.Bold),
                radius * 0.14,
                dimWhite);

            context.DrawText(text, new Point(
                numPos.X - text.Width / 2,
                numPos.Y - text.Height / 2));
        }

        // ── Hour hand (short, wider paddle) ──
        double hourAngle = ToRadians((Hours % 12) * 30 + Minutes * 0.5 + Seconds * (0.5 / 60) - 90);
        DrawHand(context, center, hourAngle,
                 length: radius * 0.55,
                 halfWidth: radius * 0.042,
                 tailLength: radius * 0.10,
                 white);

        // ── Minute hand (long paddle, nearly touching the dial) ──
        double minuteAngle = ToRadians(Minutes * 6 + Seconds * 0.1 - 90);
        DrawHand(context, center, minuteAngle,
                 length: radius * 0.93,
                 halfWidth: radius * 0.030,
                 tailLength: radius * 0.10,
                 white);

        // ── Second hand (thin line, gold, with counterweight) ──
        double secondAngle = ToRadians(Seconds * 6 - 90);
        var secTip  = OnCircle(center, radius * 0.88, secondAngle);
        var secTail = OnCircle(center, radius * 0.22, secondAngle + Math.PI);
        context.DrawLine(new Pen(gold, Math.Max(1.5, radius * 0.008)), secTail, secTip);

        // ── Center hub (white disc covers hand bases, gold pivot on top) ──
        double hubR = radius * 0.035;
        context.DrawEllipse(white, null, center, hubR, hubR);
        double goldR = radius * 0.02;
        context.DrawEllipse(gold, null, center, goldR, goldR);
    }

    /// <summary>
    /// Draws a paddle-shaped hand: thin neck from hub → smooth bezier
    /// widening → constant-width blade → rounded semicircular tip.
    /// The hub circle covers the base, so only the neck outward is visible.
    /// </summary>
    private static void DrawHand(DrawingContext context, Point center, double angle,
        double length, double halfWidth, double tailLength, IBrush brush)
    {
        double perp = angle + Math.PI / 2;
        double neckHalf = halfWidth * 0.20;  // thin neck

        // ── Key points along the hand axis ──
        var neckPt     = OnCircle(center, length * 0.08, angle);   // start of visible neck
        var shoulderPt = OnCircle(center, length * 0.72, angle);   // where it reaches full width
        var tipPt      = OnCircle(center, length, angle);           // end of hand
        var tailPt     = OnCircle(center, tailLength, angle + Math.PI);

        // ── Perpendicular offsets at each station ──
        var neckR     = Offset(neckPt, perp, neckHalf);
        var neckL     = Offset(neckPt, perp, -neckHalf);
        var shoulderR = Offset(shoulderPt, perp, halfWidth);
        var shoulderL = Offset(shoulderPt, perp, -halfWidth);
        var tipR      = Offset(tipPt, perp, halfWidth);
        var tipL      = Offset(tipPt, perp, -halfWidth);
        var tailR     = Offset(tailPt, perp, neckHalf);
        var tailL     = Offset(tailPt, perp, -neckHalf);

        // ── Bezier control points for smooth widening (at ~40% length, still thin) ──
        var ctrlPt = OnCircle(center, length * 0.40, angle);
        var ctrlR  = Offset(ctrlPt, perp, halfWidth * 0.30);
        var ctrlL  = Offset(ctrlPt, perp, -halfWidth * 0.30);

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(tailR, true);
            ctx.LineTo(neckR);                                          // thin tail to neck
            ctx.QuadraticBezierTo(ctrlR, shoulderR);                   // smooth widening
            ctx.LineTo(tipR);                                           // constant width to tip edge
            ctx.ArcTo(tipL, new Size(halfWidth, halfWidth),            // rounded cap
                      0, false, SweepDirection.Clockwise);
            ctx.LineTo(shoulderL);                                      // tip edge back to shoulder
            ctx.QuadraticBezierTo(ctrlL, neckL);                       // smooth narrowing
            ctx.LineTo(tailL);                                          // neck to tail
            ctx.EndFigure(true);
        }

        context.DrawGeometry(brush, null, geo);
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;

    private static Point OnCircle(Point center, double radius, double angleRadians) =>
        new(center.X + radius * Math.Cos(angleRadians),
            center.Y + radius * Math.Sin(angleRadians));

    private static Point Offset(Point p, double perpAngle, double distance) =>
        new(p.X + distance * Math.Cos(perpAngle),
            p.Y + distance * Math.Sin(perpAngle));
}
