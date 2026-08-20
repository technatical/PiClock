using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PiClock.Controls;

/// <summary>
/// Renders an analog clock face with tapered hour/minute hands
/// and a gold second hand — Apple StandBy inspired.
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

        var white   = Brushes.White;
        var dimWhite = new SolidColorBrush(Color.FromArgb(64, 255, 255, 255));
        var gold    = new SolidColorBrush(Color.Parse("#FFB800"));

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

        // ── Hour hand (short, wide, tapered) ──
        double hourAngle = ToRadians((Hours % 12) * 30 + Minutes * 0.5 + Seconds * (0.5 / 60) - 90);
        DrawHand(context, center, hourAngle,
                 length: radius * 0.50,
                 halfWidth: radius * 0.048,
                 tailLength: radius * 0.12,
                 white);

        // ── Minute hand (long, tapered) ──
        double minuteAngle = ToRadians(Minutes * 6 + Seconds * 0.1 - 90);
        DrawHand(context, center, minuteAngle,
                 length: radius * 0.72,
                 halfWidth: radius * 0.032,
                 tailLength: radius * 0.12,
                 white);

        // ── Second hand (thin line, gold, with counterweight) ──
        double secondAngle = ToRadians(Seconds * 6 - 90);
        var secTip  = OnCircle(center, radius * 0.82, secondAngle);
        var secTail = OnCircle(center, radius * 0.20, secondAngle + Math.PI);
        context.DrawLine(new Pen(gold, Math.Max(1.5, radius * 0.01)), secTail, secTip);

        // ── Center hub (white ring + gold dot) ──
        double hubR = radius * 0.03;
        context.DrawEllipse(white, null, center, hubR, hubR);
        double goldR = radius * 0.018;
        context.DrawEllipse(gold, null, center, goldR, goldR);
    }

    /// <summary>
    /// Draws a tapered hand: pointed tip → shoulder → uniform body → narrower tail.
    /// </summary>
    private static void DrawHand(DrawingContext context, Point center, double angle,
        double length, double halfWidth, double tailLength, IBrush brush)
    {
        double perp = angle + Math.PI / 2;

        var tip    = OnCircle(center, length, angle);
        var tail   = OnCircle(center, tailLength, angle + Math.PI);

        // Shoulder at 65% — where taper to the tip begins
        var shoulder = OnCircle(center, length * 0.65, angle);
        var shoulderL = Offset(shoulder, perp, halfWidth);
        var shoulderR = Offset(shoulder, perp, -halfWidth);

        // Base at center — full width
        var baseL = Offset(center, perp, halfWidth);
        var baseR = Offset(center, perp, -halfWidth);

        // Tail — slightly narrower
        double tailHalf = halfWidth * 0.7;
        var tailL = Offset(tail, perp, tailHalf);
        var tailR = Offset(tail, perp, -tailHalf);

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(tip, true);
            ctx.LineTo(shoulderL);
            ctx.LineTo(baseL);
            ctx.LineTo(tailL);
            ctx.LineTo(tailR);
            ctx.LineTo(baseR);
            ctx.LineTo(shoulderR);
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
