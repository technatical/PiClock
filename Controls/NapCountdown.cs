using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace PiClock.Controls;

/// <summary>
/// Shown along the bottom of the clock while the bus is "napping" after too many plays.
/// A little sleeping bus, floating Zs, a bar that shrinks as the nap runs out, and small
/// digits in the corner for the grown-ups.
/// </summary>
public class NapCountdown : Control
{
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<NapCountdown, bool>(nameof(IsActive));

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    /// <summary>Remaining share of the nap: 1.0 at the start, 0.0 when it is over.</summary>
    public static readonly StyledProperty<double> FractionProperty =
        AvaloniaProperty.Register<NapCountdown, double>(nameof(Fraction), 1.0);

    public double Fraction
    {
        get => GetValue(FractionProperty);
        set => SetValue(FractionProperty, value);
    }

    /// <summary>"m:ss" left, drawn small at the right end.</summary>
    public static readonly StyledProperty<string> RemainingTextProperty =
        AvaloniaProperty.Register<NapCountdown, string>(nameof(RemainingText), "");

    public string RemainingText
    {
        get => GetValue(RemainingTextProperty);
        set => SetValue(RemainingTextProperty, value);
    }

    private readonly DispatcherTimer _timer;
    private int _tick;

    private static readonly IBrush PanelFill   = new SolidColorBrush(Color.FromArgb(200, 20, 20, 30));
    private static readonly IBrush BusYellow   = new SolidColorBrush(Color.Parse("#FFD700"));
    private static readonly IBrush BusTrim     = new SolidColorBrush(Color.Parse("#DAA520"));
    private static readonly IBrush SleepWindow = new SolidColorBrush(Color.Parse("#2B3A67"));
    private static readonly IBrush TireFill    = new SolidColorBrush(Color.Parse("#333333"));
    private static readonly IBrush HubFill     = new SolidColorBrush(Color.Parse("#999999"));
    private static readonly IBrush TrackFill   = new SolidColorBrush(Color.FromArgb(255, 60, 60, 75));
    private static readonly IBrush TextFill    = new SolidColorBrush(Color.FromArgb(255, 170, 170, 185));
    private static readonly IBrush ZFill       = new SolidColorBrush(Color.FromArgb(230, 200, 220, 255));

    static NapCountdown()
    {
        AffectsRender<NapCountdown>(IsActiveProperty, FractionProperty, RemainingTextProperty);
        IsActiveProperty.Changed.AddClassHandler<NapCountdown>((ctrl, args) =>
        {
            if ((bool)args.NewValue!) { ctrl._tick = 0; ctrl._timer.Start(); }
            else ctrl._timer.Stop();
        });
    }

    public NapCountdown()
    {
        IsHitTestVisible = false;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _timer.Tick += (_, _) => { _tick++; InvalidateVisual(); };
    }

    public override void Render(DrawingContext ctx)
    {
        if (!IsActive) return;

        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        double s = h / 150.0;                 // designed for a 150px strip
        double pad = 24 * s;

        // Panel across the bottom
        ctx.DrawRectangle(PanelFill, null,
            new RoundedRect(new Rect(pad, pad * 0.5, w - pad * 2, h - pad), 22 * s));

        // Sleeping bus on the left
        double bw = 190 * s, bh = 78 * s;
        double bx = pad * 2, by = h / 2 - bh / 2 + 6 * s;
        DrawSleepingBus(ctx, bx, by, bw, bh, s);
        DrawZs(ctx, bx + bw * 0.78, by - 4 * s, s);

        // Remaining time, small, on the right
        var typeface = new Typeface("Inter", FontStyle.Normal, FontWeight.SemiBold);
        var txt = new FormattedText(RemainingText,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, typeface, 30 * s, TextFill);
        double tx = w - pad * 2 - txt.Width;
        ctx.DrawText(txt, new Point(tx, h / 2 - txt.Height / 2));

        // Shrinking bar between the bus and the digits
        double barX = bx + bw + 40 * s;
        double barW = tx - 30 * s - barX;
        double barH = 34 * s;
        double barY = h / 2 - barH / 2;
        if (barW > 10)
        {
            ctx.DrawRectangle(TrackFill, null,
                new RoundedRect(new Rect(barX, barY, barW, barH), barH / 2));
            double fillW = Math.Max(barH, barW * Math.Clamp(Fraction, 0, 1));
            ctx.DrawRectangle(BusYellow, null,
                new RoundedRect(new Rect(barX, barY, fillW, barH), barH / 2));
        }
    }

    private static void DrawSleepingBus(DrawingContext ctx, double x, double y,
                                        double bw, double bh, double s)
    {
        var trim = new Pen(BusTrim, 2 * s);

        ctx.DrawRectangle(BusYellow, trim, new RoundedRect(new Rect(x, y, bw, bh), 10 * s));

        double stripH = 10 * s;
        ctx.DrawRectangle(BusTrim, null,
            new RoundedRect(new Rect(x - 4 * s, y + bh - stripH, bw + 8 * s, stripH), 3 * s));

        // Dark windows: everyone is asleep
        double winW = 24 * s, winH = 26 * s, gap = 8 * s;
        double wx0 = x + 16 * s, wy = y + 10 * s;
        for (int i = 0; i < 4; i++)
            ctx.DrawRectangle(SleepWindow, trim,
                new RoundedRect(new Rect(wx0 + i * (winW + gap), wy, winW, winH), 3 * s));

        // Windshield, also dark
        ctx.DrawRectangle(SleepWindow, trim,
            new RoundedRect(new Rect(x + bw - 34 * s, wy, 26 * s, winH + 6 * s), 4 * s));

        // Closed-eye headlight (a short line instead of a bright circle)
        ctx.DrawLine(new Pen(BusTrim, 3 * s) { LineCap = PenLineCap.Round },
            new Point(x + bw - 2 * s, y + bh - 22 * s),
            new Point(x + bw + 8 * s, y + bh - 22 * s));

        // Wheels
        double r = 14 * s, wyc = y + bh - 1 * s;
        foreach (double cx in new[] { x + 38 * s, x + bw - 36 * s })
        {
            ctx.DrawEllipse(TireFill, null, new Point(cx, wyc), r, r);
            ctx.DrawEllipse(HubFill, null, new Point(cx, wyc), r * 0.45, r * 0.45);
        }
    }

    private void DrawZs(DrawingContext ctx, double x, double y, double s)
    {
        // Three Zs drifting up and to the right, looping every ~4 seconds
        var typeface = new Typeface("Inter", FontStyle.Normal, FontWeight.ExtraBold);
        for (int i = 0; i < 3; i++)
        {
            double phase = ((_tick * 0.02) + i * 0.33) % 1.0;      // 0 → 1
            double size  = (16 + i * 8) * s;
            double zx = x + phase * 40 * s + i * 14 * s;
            double zy = y - phase * 55 * s - i * 6 * s;
            byte alpha = (byte)(230 * (1 - phase));

            var fmt = new FormattedText("z",
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, size,
                new SolidColorBrush(Color.FromArgb(alpha, 200, 220, 255)));
            ctx.DrawText(fmt, new Point(zx, zy - fmt.Height));
        }
    }
}
