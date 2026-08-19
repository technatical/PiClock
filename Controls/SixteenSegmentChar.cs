using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PiClock.Controls;

/// <summary>
/// Renders a single character using a 16-segment display.
/// Supports uppercase letters A–Z and digits 0–9.
///
/// Segment layout:
///    ─a1─ ─a2─
///   │╲    │   ╱│
///   f  h  i  j  b
///   │   ╲ │ ╱  │
///    ─g1─ ─g2─
///   │   ╱ │ ╲  │
///   e  m  l  k  c
///   │╱    │   ╲│
///    ─d1─ ─d2─
///
/// Segments: a1(0) a2(1) b(2) c(3) d1(4) d2(5) e(6) f(7)
///           g1(8) g2(9) h(10) i(11) j(12) k(13) l(14) m(15)
/// </summary>
public class SixteenSegmentChar : Control
{
    // Character → active segment indices
    private static readonly Dictionary<char, int[]> CharMap = new()
    {
        // ── Letters ──
        ['A'] = [0, 1, 2, 3, 6, 7, 8, 9],
        ['B'] = [0, 1, 2, 3, 4, 5, 9, 11, 14],
        ['C'] = [0, 1, 4, 5, 6, 7],
        ['D'] = [0, 1, 2, 3, 4, 5, 11, 14],
        ['E'] = [0, 1, 4, 5, 6, 7, 8],
        ['F'] = [0, 1, 6, 7, 8],
        ['G'] = [0, 1, 3, 4, 5, 6, 7, 9],
        ['H'] = [2, 3, 6, 7, 8, 9],
        ['I'] = [0, 1, 4, 5, 11, 14],
        ['J'] = [2, 3, 4, 5, 6],
        ['K'] = [6, 7, 8, 12, 13],
        ['L'] = [4, 5, 6, 7],
        ['M'] = [2, 3, 6, 7, 10, 12],
        ['N'] = [2, 3, 6, 7, 10, 13],
        ['O'] = [0, 1, 2, 3, 4, 5, 6, 7],
        ['P'] = [0, 1, 2, 6, 7, 8, 9],
        ['Q'] = [0, 1, 2, 3, 4, 5, 6, 7, 13],
        ['R'] = [0, 1, 2, 6, 7, 8, 9, 13],
        ['S'] = [0, 1, 3, 4, 5, 7, 8, 9],
        ['T'] = [0, 1, 11, 14],
        ['U'] = [2, 3, 4, 5, 6, 7],
        ['V'] = [6, 7, 12, 15],
        ['W'] = [2, 3, 6, 7, 13, 15],
        ['X'] = [10, 12, 13, 15],
        ['Y'] = [10, 12, 14],
        ['Z'] = [0, 1, 4, 5, 12, 15],

        // ── Digits ──
        ['0'] = [0, 1, 2, 3, 4, 5, 6, 7],
        ['1'] = [2, 3],
        ['2'] = [0, 1, 2, 4, 5, 6, 8, 9],
        ['3'] = [0, 1, 2, 3, 4, 5, 8, 9],
        ['4'] = [2, 3, 7, 8, 9],
        ['5'] = [0, 1, 3, 4, 5, 7, 8, 9],
        ['6'] = [0, 1, 3, 4, 5, 6, 7, 8, 9],
        ['7'] = [0, 1, 2, 3],
        ['8'] = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9],
        ['9'] = [0, 1, 2, 3, 4, 5, 7, 8, 9],
    };

    // Segment polygons in a 100 × 180 reference space (thickness = 12, gap = 0.5).
    // Orthogonal segments are hexagons; diagonals are quadrilaterals.
    private static readonly Point[][] SegmentPolygons =
    [
        // 0: a1 – top-left horizontal
        [new(12.5, 6), new(18.5, 0), new(43.5, 0), new(49.5, 6), new(43.5, 12), new(18.5, 12)],
        // 1: a2 – top-right horizontal
        [new(50.5, 6), new(56.5, 0), new(81.5, 0), new(87.5, 6), new(81.5, 12), new(56.5, 12)],
        // 2: b – top-right vertical
        [new(94, 12.5), new(100, 18.5), new(100, 77.5), new(94, 83.5), new(88, 77.5), new(88, 18.5)],
        // 3: c – bottom-right vertical
        [new(94, 96.5), new(100, 102.5), new(100, 161.5), new(94, 167.5), new(88, 161.5), new(88, 102.5)],
        // 4: d1 – bottom-left horizontal
        [new(12.5, 174), new(18.5, 168), new(43.5, 168), new(49.5, 174), new(43.5, 180), new(18.5, 180)],
        // 5: d2 – bottom-right horizontal
        [new(50.5, 174), new(56.5, 168), new(81.5, 168), new(87.5, 174), new(81.5, 180), new(56.5, 180)],
        // 6: e – bottom-left vertical
        [new(6, 96.5), new(12, 102.5), new(12, 161.5), new(6, 167.5), new(0, 161.5), new(0, 102.5)],
        // 7: f – top-left vertical
        [new(6, 12.5), new(12, 18.5), new(12, 77.5), new(6, 83.5), new(0, 77.5), new(0, 18.5)],
        // 8: g1 – middle-left horizontal
        [new(12.5, 90), new(18.5, 84), new(43.5, 84), new(49.5, 90), new(43.5, 96), new(18.5, 96)],
        // 9: g2 – middle-right horizontal
        [new(50.5, 90), new(56.5, 84), new(81.5, 84), new(87.5, 90), new(81.5, 96), new(56.5, 96)],
        // 10: h – diagonal, top-left → center
        [new(20, 14), new(43, 79), new(36, 82), new(13, 17)],
        // 11: i – top-center vertical
        [new(50, 12.5), new(56, 18.5), new(56, 77.5), new(50, 83.5), new(44, 77.5), new(44, 18.5)],
        // 12: j – diagonal, top-right → center
        [new(80, 14), new(87, 17), new(64, 82), new(57, 79)],
        // 13: k – diagonal, center → bottom-right
        [new(57, 101), new(64, 98), new(87, 163), new(80, 166)],
        // 14: l – bottom-center vertical
        [new(50, 96.5), new(56, 102.5), new(56, 161.5), new(50, 167.5), new(44, 161.5), new(44, 102.5)],
        // 15: m – diagonal, center → bottom-left
        [new(36, 98), new(43, 101), new(20, 166), new(13, 163)],
    ];

    // Diagonal segment indices — off-state is hidden to avoid visual clutter
    private static readonly HashSet<int> DiagonalIndices = [10, 12, 13, 15];

    public static readonly StyledProperty<char> CharProperty =
        AvaloniaProperty.Register<SixteenSegmentChar, char>(nameof(Char), ' ');

    public static readonly StyledProperty<Color> OnColorProperty =
        AvaloniaProperty.Register<SixteenSegmentChar, Color>(nameof(OnColor), Colors.White);

    public char Char
    {
        get => GetValue(CharProperty);
        set => SetValue(CharProperty, value);
    }

    public Color OnColor
    {
        get => GetValue(OnColorProperty);
        set => SetValue(OnColorProperty, value);
    }

    private SolidColorBrush _onBrush = new(Colors.White);
    private SolidColorBrush _offBrush = new(Color.FromArgb(15, 255, 255, 255));
    private StreamGeometry[]? _geometries;
    private Size _lastSize;

    static SixteenSegmentChar()
    {
        AffectsRender<SixteenSegmentChar>(CharProperty, OnColorProperty);
        OnColorProperty.Changed.AddClassHandler<SixteenSegmentChar>((ctrl, _) => ctrl.UpdateBrushes());
    }

    private void UpdateBrushes()
    {
        var c = OnColor;
        _onBrush = new SolidColorBrush(c);
        _offBrush = new SolidColorBrush(Color.FromArgb(15, c.R, c.G, c.B));
    }

    public override void Render(DrawingContext context)
    {
        var ch = char.ToUpper(Char);

        if (!CharMap.TryGetValue(ch, out var activeSegments))
            return; // Unknown character — draw nothing

        RebuildGeometriesIfNeeded();

        var activeSet = new HashSet<int>(activeSegments);

        for (int i = 0; i < 16; i++)
        {
            bool isOn = activeSet.Contains(i);

            // Skip off-state diagonals — the criss-cross pattern is noisy
            if (!isOn && DiagonalIndices.Contains(i))
                continue;

            context.DrawGeometry(isOn ? _onBrush : _offBrush, null, _geometries![i]);
        }
    }

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
            scale = size.Height / RefH;
            offsetX = (size.Width - size.Height * AspectRatio) / 2;
            offsetY = 0;
        }
        else
        {
            scale = size.Width / RefW;
            offsetX = 0;
            offsetY = (size.Height - size.Width / AspectRatio) / 2;
        }

        _geometries = new StreamGeometry[16];

        for (int i = 0; i < 16; i++)
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
