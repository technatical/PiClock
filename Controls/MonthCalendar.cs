using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PiClock.Controls;

/// <summary>
/// Renders a clean monthly calendar grid.
/// Current day highlighted with a gold circle — Apple StandBy style.
/// </summary>
public class MonthCalendar : Control
{
    public static readonly StyledProperty<int> YearProperty =
        AvaloniaProperty.Register<MonthCalendar, int>(nameof(Year));

    public static readonly StyledProperty<int> MonthProperty =
        AvaloniaProperty.Register<MonthCalendar, int>(nameof(Month), 1);

    public static readonly StyledProperty<int> HighlightDayProperty =
        AvaloniaProperty.Register<MonthCalendar, int>(nameof(HighlightDay));

    public int Year         { get => GetValue(YearProperty);         set => SetValue(YearProperty, value); }
    public int Month        { get => GetValue(MonthProperty);        set => SetValue(MonthProperty, value); }
    public int HighlightDay { get => GetValue(HighlightDayProperty); set => SetValue(HighlightDayProperty, value); }

    static MonthCalendar()
    {
        AffectsRender<MonthCalendar>(YearProperty, MonthProperty, HighlightDayProperty);
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0) return;

        var gold     = new SolidColorBrush(Color.Parse("#FFB800"));
        var dimWhite = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255));
        var white    = Brushes.White;

        var typeface     = new Typeface("Inter", FontStyle.Normal, FontWeight.SemiBold);
        var boldTypeface = new Typeface("Inter", FontStyle.Normal, FontWeight.ExtraBold);

        // ── Layout proportions ──
        double cellW      = size.Width / 7;
        double headerH    = size.Height * 0.22;   // "Aug 20" header
        double dayHeaderH = size.Height * 0.08;   // S M T W T F S
        double gridH      = size.Height - headerH - dayHeaderH;
        double cellH      = gridH / 6;
        double fontSize   = Math.Min(cellW, cellH) * 0.42;

        // ── Month + Day header (gold, centered, abbreviated) ──
        var dt = new DateTime(Year, Month, 1);
        string headerLabel = $"{dt.ToString("MMM")} {HighlightDay}";
        double headerFontSize = fontSize * 2.8;
        var headerText = new FormattedText(
            headerLabel,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            boldTypeface,
            headerFontSize,
            gold);
        context.DrawText(headerText, new Point(
            (size.Width - headerText.Width) / 2,
            (headerH - headerText.Height) / 2));

        // ── Day-of-week headers ──
        string[] headers = ["S", "M", "T", "W", "T", "F", "S"];
        for (int i = 0; i < 7; i++)
        {
            var hdr = new FormattedText(
                headers[i],
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize * 0.85,
                dimWhite);
            double x = i * cellW + (cellW - hdr.Width) / 2;
            context.DrawText(hdr, new Point(x, headerH + (dayHeaderH - hdr.Height) / 2));
        }

        // ── Day numbers ──
        int daysInMonth    = DateTime.DaysInMonth(Year, Month);
        int firstDayOfWeek = (int)new DateTime(Year, Month, 1).DayOfWeek; // 0 = Sunday
        double gridTop     = headerH + dayHeaderH;

        for (int day = 1; day <= daysInMonth; day++)
        {
            int pos = firstDayOfWeek + day - 1;
            int row = pos / 7;
            int col = pos % 7;

            double cx = col * cellW + cellW / 2;
            double cy = gridTop + row * cellH + cellH / 2;

            bool isToday = day == HighlightDay;

            // Gold circle behind today
            if (isToday)
            {
                double r = Math.Min(cellW, cellH) * 0.38;
                context.DrawEllipse(gold, null, new Point(cx, cy), r, r);
            }

            var numText = new FormattedText(
                day.ToString(),
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                isToday ? boldTypeface : typeface,
                fontSize,
                isToday ? Brushes.Black : white);

            context.DrawText(numText, new Point(
                cx - numText.Width / 2,
                cy - numText.Height / 2));
        }
    }
}
