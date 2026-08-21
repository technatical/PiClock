using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PiClock.Controls;

/// <summary>
/// Renders an analog clock face with outlined capsule-shaped hands:
/// white outline, light blue fill, rounded ends. Gold second hand.
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
        var blueFill = new SolidColorBrush(Color.Parse("#3366AA"));

        double border = Math.Max(2, radius * 0.009);  // white border thickness

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

        // ── Hour hand (short, wider capsule) ──
        double hourAngle = ToRadians((Hours % 12) * 30 + Minutes * 0.5 + Seconds * (0.5 / 60) - 90);
        DrawCapsuleHand(context, center, hourAngle,
                        startDist: radius * 0.06,
                        endDist: radius * 0.52,
                        width: radius * 0.068,
                        border, white, blueFill);

        // ── Minute hand (long, narrower capsule, nearly touching dial) ──
        double minuteAngle = ToRadians(Minutes * 6 + Seconds * 0.1 - 90);
        DrawCapsuleHand(context, center, minuteAngle,
                        startDist: radius * 0.06,
                        endDist: radius * 0.93,
                        width: radius * 0.052,
                        border, white, blueFill);

        // ── Second hand (thin line, gold, with counterweight) ──
        double secondAngle = ToRadians(Seconds * 6 - 90);
        var secTip  = OnCircle(center, radius * 0.88, secondAngle);
        var secTail = OnCircle(center, radius * 0.22, secondAngle + Math.PI);
        context.DrawLine(new Pen(gold, Math.Max(1.5, radius * 0.008)), secTail, secTip);

        // ── Center hub (covers hand bases) ──
        double hubR = radius * 0.045;
        context.DrawEllipse(white, null, center, hubR, hubR);
        double innerR = radius * 0.032;
        context.DrawEllipse(new SolidColorBrush(Color.Parse("#222222")), null, center, innerR, innerR);
        double goldR = radius * 0.015;
        context.DrawEllipse(gold, null, center, goldR, goldR);
    }

    /// <summary>
    /// Draws a capsule-shaped (stadium) hand using thick lines with rounded caps.
    /// White outline drawn first, then narrower blue fill on top.
    /// </summary>
    private static void DrawCapsuleHand(DrawingContext context, Point center, double angle,
        double startDist, double endDist, double width, double border, IBrush outline, IBrush fill)
    {
        var start = OnCircle(center, startDist, angle);
        var end   = OnCircle(center, endDist, angle);

        // White outline (full width, rounded ends)
        context.DrawLine(
            new Pen(outline, width) { LineCap = PenLineCap.Round },
            start, end);

        // Blue fill (narrower, exposing white border on all sides)
        double fillWidth = width - border * 2;
        if (fillWidth > 0)
        {
            context.DrawLine(
                new Pen(fill, fillWidth) { LineCap = PenLineCap.Round },
                start, end);
        }
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;

    private static Point OnCircle(Point center, double radius, double angleRadians) =>
        new(center.X + radius * Math.Cos(angleRadians),
            center.Y + radius * Math.Sin(angleRadians));
}
