using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PiClock.Controls;

/// <summary>
/// Renders the blinking colon ( : ) between the hour and minute digits.
/// Two rounded-square dots at 1/3 and 2/3 of the control height.
/// </summary>
public class ColonDisplay : Control
{
    public static readonly StyledProperty<bool> IsOnProperty =
        AvaloniaProperty.Register<ColonDisplay, bool>(nameof(IsOn), true);

    public static readonly StyledProperty<Color> DotColorProperty =
        AvaloniaProperty.Register<ColonDisplay, Color>(nameof(DotColor), Colors.White);

    public bool IsOn
    {
        get => GetValue(IsOnProperty);
        set => SetValue(IsOnProperty, value);
    }

    public Color DotColor
    {
        get => GetValue(DotColorProperty);
        set => SetValue(DotColorProperty, value);
    }

    private SolidColorBrush _onBrush = new(Colors.White);
    private SolidColorBrush _offBrush = new(Color.FromArgb(15, 255, 255, 255));

    static ColonDisplay()
    {
        AffectsRender<ColonDisplay>(IsOnProperty, DotColorProperty);
        DotColorProperty.Changed.AddClassHandler<ColonDisplay>((ctrl, _) => ctrl.UpdateBrushes());
    }

    private void UpdateBrushes()
    {
        var c = DotColor;
        _onBrush = new SolidColorBrush(c);
        _offBrush = new SolidColorBrush(Color.FromArgb(15, c.R, c.G, c.B));
    }

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        var brush = IsOn ? _onBrush : _offBrush;

        // Dot size scales with the control, matching seven-segment thickness
        var dotSize = Math.Min(w * 0.45, h * 0.065);
        var cx = w / 2;
        var cornerRadius = dotSize * 0.2;

        // Upper dot at 1/3 height, lower dot at 2/3 height
        var upper = new Rect(cx - dotSize / 2, h / 3 - dotSize / 2, dotSize, dotSize);
        var lower = new Rect(cx - dotSize / 2, h * 2 / 3 - dotSize / 2, dotSize, dotSize);

        context.DrawRectangle(brush, null, upper, cornerRadius, cornerRadius);
        context.DrawRectangle(brush, null, lower, cornerRadius, cornerRadius);
    }
}
