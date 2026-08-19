using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PiClock.Controls;

/// <summary>
/// Renders a simple analog clock face with hour and minute hands.
/// White on black, no second hand — clean and classic.
/// </summary>
public class AnalogClock : Control
{
    public static readonly StyledProperty<int> HoursProperty =
        AvaloniaProperty.Register<AnalogClock, int>(nameof(Hours));

    public static readonly StyledProperty<int> MinutesProperty =
        AvaloniaProperty.Register<AnalogClock, int>(nameof(Minutes));

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

    static AnalogClock()
    {
        AffectsRender<AnalogClock>(HoursProperty, MinutesProperty);
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0) return;

        var center = new Point(size.Width / 2, size.Height / 2);
        var radius = Math.Min(size.Width, size.Height) / 2 * 0.97;

        var white = Brushes.White;
        var dimWhite = new SolidColorBrush(Color.FromArgb(64, 255, 255, 255)); // 25% opacity

        // ── Outer circle (dimmed) ──
        var circlePen = new Pen(dimWhite, Math.Max(2, radius * 0.01));
        context.DrawEllipse(null, circlePen, center, radius, radius);

        // ── Hour markers and numbers (dimmed) ──
        for (int i = 1; i <= 12; i++)
        {
            double angle = ToRadians(i * 30 - 90);

            // Tick mark
            var outer = OnCircle(center, radius * 0.95, angle);
            var inner = OnCircle(center, radius * 0.87, angle);
            context.DrawLine(new Pen(dimWhite, Math.Max(1.5, radius * 0.008)), inner, outer);

            // Number
            var numPos = OnCircle(center, radius * 0.75, angle);
            var text = new FormattedText(
                i.ToString(),
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Inter", FontStyle.Normal, FontWeight.Normal),
                radius * 0.14,
                dimWhite);

            context.DrawText(text, new Point(
                numPos.X - text.Width / 2,
                numPos.Y - text.Height / 2));
        }

        // ── Hour hand (short, wide) ──
        double hourAngle = ToRadians((Hours % 12) * 30 + Minutes * 0.5 - 90);
        DrawHand(context, center, hourAngle,
                 length: radius * 0.50,
                 baseHalfWidth: radius * 0.055,
                 tailLength: radius * 0.10,
                 white);

        // ── Minute hand (long, thin) ──
        double minuteAngle = ToRadians(Minutes * 6 - 90);
        DrawHand(context, center, minuteAngle,
                 length: radius * 0.72,
                 baseHalfWidth: radius * 0.035,
                 tailLength: radius * 0.10,
                 white);

        // ── Center dot ──
        double dotR = radius * 0.03;
        context.DrawEllipse(white, null, center, dotR, dotR);
    }

    /// <summary>
    /// Draws a diamond/kite-shaped hand: pointed tip → wide base → short tail.
    /// </summary>
    private static void DrawHand(DrawingContext context, Point center, double angle,
        double length, double baseHalfWidth, double tailLength, IBrush brush)
    {
        double perp = angle + Math.PI / 2;

        var tip   = OnCircle(center, length, angle);
        var tail  = OnCircle(center, tailLength, angle + Math.PI);
        var left  = OnCircle(center, baseHalfWidth, perp);
        var right = OnCircle(center, baseHalfWidth, perp + Math.PI);

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(tip, true);
            ctx.LineTo(left);
            ctx.LineTo(tail);
            ctx.LineTo(right);
            ctx.EndFigure(true);
        }

        context.DrawGeometry(brush, null, geo);
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;

    private static Point OnCircle(Point center, double radius, double angleRadians) =>
        new(center.X + radius * Math.Cos(angleRadians),
            center.Y + radius * Math.Sin(angleRadians));
}
