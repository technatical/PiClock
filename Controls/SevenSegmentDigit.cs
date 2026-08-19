using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PiClock.Controls;

/// <summary>
/// Renders a single seven-segment digit (0–9).
/// Set Digit to -1 for a blank display (leading-zero suppression).
///
/// Segment layout:
///    _a_
///   |   |
///   f   b
///   |_g_|
///   |   |
///   e   c
///   |_d_|
/// </summary>
public class SevenSegmentDigit : Control
{
    // Which segments light up for each digit [a, b, c, d, e, f, g]
    private static readonly bool[][] SegmentMap =
    [
        [true,  true,  true,  true,  true,  true,  false], // 0
        [false, true,  true,  false, false, false, false], // 1
        [true,  true,  false, true,  true,  false, true],  // 2
        [true,  true,  true,  true,  false, false, true],  // 3
        [false, true,  true,  false, false, true,  true],  // 4
        [true,  false, true,  true,  false, true,  true],  // 5
        [true,  false, true,  true,  true,  true,  true],  // 6
        [true,  true,  true,  false, false, false, false], // 7
        [true,  true,  true,  true,  true,  true,  true],  // 8
        [true,  true,  true,  true,  false, true,  true],  // 9
    ];

    // Hexagonal segment polygons in a 100 × 180 reference coordinate space.
    // Each segment is a six-point hexagon with tapered ends for that authentic LED look.
    // Gap between segments: 0.5 units (very tight for a solid, clean look at large sizes).
    private static readonly Point[][] SegmentPolygons =
    [
        // a – top horizontal
        [new(16.5, 8), new(24.5, 0), new(75.5, 0), new(83.5, 8), new(75.5, 16), new(24.5, 16)],
        // b – top-right vertical
        [new(92, 16.5), new(100, 24.5), new(100, 73.5), new(92, 81.5), new(84, 73.5), new(84, 24.5)],
        // c – bottom-right vertical
        [new(92, 98.5), new(100, 106.5), new(100, 155.5), new(92, 163.5), new(84, 155.5), new(84, 106.5)],
        // d – bottom horizontal
        [new(16.5, 172), new(24.5, 164), new(75.5, 164), new(83.5, 172), new(75.5, 180), new(24.5, 180)],
        // e – bottom-left vertical
        [new(8, 98.5), new(16, 106.5), new(16, 155.5), new(8, 163.5), new(0, 155.5), new(0, 106.5)],
        // f – top-left vertical
        [new(8, 16.5), new(16, 24.5), new(16, 73.5), new(8, 81.5), new(0, 73.5), new(0, 24.5)],
        // g – middle horizontal
        [new(16.5, 90), new(24.5, 82), new(75.5, 82), new(83.5, 90), new(75.5, 98), new(24.5, 98)],
    ];

    public static readonly StyledProperty<int> DigitProperty =
        AvaloniaProperty.Register<SevenSegmentDigit, int>(nameof(Digit), 0);

    public static readonly StyledProperty<Color> OnColorProperty =
        AvaloniaProperty.Register<SevenSegmentDigit, Color>(nameof(OnColor), Colors.White);

    public int Digit
    {
        get => GetValue(DigitProperty);
        set => SetValue(DigitProperty, value);
    }

    public Color OnColor
    {
        get => GetValue(OnColorProperty);
        set => SetValue(OnColorProperty, value);
    }

    // Cached brushes and geometries for efficient rendering
    private SolidColorBrush _onBrush = new(Colors.White);
    private SolidColorBrush _offBrush = new(Color.FromArgb(15, 255, 255, 255));
    private StreamGeometry[]? _geometries;
    private Size _lastSize;

    static SevenSegmentDigit()
    {
        AffectsRender<SevenSegmentDigit>(DigitProperty, OnColorProperty);
        OnColorProperty.Changed.AddClassHandler<SevenSegmentDigit>((ctrl, _) => ctrl.UpdateBrushes());
    }

    private void UpdateBrushes()
    {
        var c = OnColor;
        _onBrush = new SolidColorBrush(c);
        _offBrush = new SolidColorBrush(Color.FromArgb(15, c.R, c.G, c.B));
    }

    public override void Render(DrawingContext context)
    {
        var digit = Digit;

        // -1 or out-of-range = blank digit (draw nothing)
        if (digit < 0 || digit > 9)
            return;

        var segments = SegmentMap[digit];
        RebuildGeometriesIfNeeded();

        for (int i = 0; i < 7; i++)
        {
            context.DrawGeometry(segments[i] ? _onBrush : _offBrush, null, _geometries![i]);
        }
    }

    /// <summary>
    /// Rebuilds the scaled segment geometries when the control size changes.
    /// The 100×180 reference coordinates are mapped into the control bounds
    /// while preserving the 5∶9 aspect ratio and centering the result.
    /// </summary>
    private void RebuildGeometriesIfNeeded()
    {
        var size = Bounds.Size;
        if (_geometries is not null && _lastSize == size)
            return;

        _lastSize = size;

        const double RefW = 100.0;
        const double RefH = 180.0;
        const double AspectRatio = RefW / RefH;

        double scale;
        double offsetX, offsetY;

        if (size.Width / size.Height > AspectRatio)
        {
            // Control is wider than 5∶9 → constrain by height
            scale = size.Height / RefH;
            offsetX = (size.Width - size.Height * AspectRatio) / 2;
            offsetY = 0;
        }
        else
        {
            // Control is taller than 5∶9 → constrain by width
            scale = size.Width / RefW;
            offsetX = 0;
            offsetY = (size.Height - size.Width / AspectRatio) / 2;
        }

        _geometries = new StreamGeometry[7];

        for (int i = 0; i < 7; i++)
        {
            var pts = SegmentPolygons[i];
            var geo = new StreamGeometry();

            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(Scale(pts[0]), true);
                for (int j = 1; j < pts.Length; j++)
                    ctx.LineTo(Scale(pts[j]));
                ctx.EndFigure(true);
            }

            _geometries[i] = geo;
        }

        Point Scale(Point p) => new(p.X * scale + offsetX, p.Y * scale + offsetY);
    }
}
