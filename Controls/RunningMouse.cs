using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace PiClock.Controls;

/// <summary>
/// A cute little mouse that appears every 2–5 minutes and does one of:
///   • scurry all the way across
///   • pause mid-path to loiter, then continue
///   • wander partway, then turn back
/// Purely decorative — set IsHitTestVisible="False".
/// </summary>
public class RunningMouse : Control
{
    // ── State machine ──
    private enum Phase { Idle, Running, Loitering, Hesitating, Returning }

    private enum Behavior { Cross, CrossWithLoiter, TurnBack }

    private readonly DispatcherTimer _animTimer;
    private readonly Random _rng = new();

    private Phase _phase = Phase.Idle;
    private Behavior _behavior;
    private bool _facingRight = true;
    private double _posX;
    private double _speed;
    private int _frame;
    private double _countdown;        // seconds until next state change
    private double _actionPointX;     // where to loiter or turn back

    // Mouse palette
    private static readonly SolidColorBrush BodyBrush    = new(Color.FromRgb(170, 170, 170));
    private static readonly SolidColorBrush EarPinkBrush = new(Color.FromRgb(255, 150, 165));
    private static readonly SolidColorBrush NoseBrush    = new(Color.FromRgb(255, 130, 145));
    private static readonly SolidColorBrush EyeBrush     = new(Colors.White);
    private static readonly SolidColorBrush WhiskerBrush = new(Color.FromRgb(200, 200, 200));

    private const double TickMs = 20;
    private const double TickSec = TickMs / 1000.0;

    public RunningMouse()
    {
        IsHitTestVisible = false;
        _countdown = 5; // first appearance after 5 s (then 2–5 min)

        _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TickMs) };
        _animTimer.Tick += (_, _) => Tick();
        _animTimer.Start();
    }

    // ══════════════════════════════════════════════════════════════
    //  State machine
    // ══════════════════════════════════════════════════════════════

    private void Tick()
    {
        switch (_phase)
        {
            case Phase.Idle:
                _countdown -= TickSec;
                if (_countdown <= 0) StartRun();
                return; // don't invalidate while idle

            case Phase.Running:
                TickRunning();
                break;

            case Phase.Loitering:
                TickLoitering();
                break;

            case Phase.Hesitating:
                TickHesitating();
                break;

            case Phase.Returning:
                TickReturning();
                break;
        }

        InvalidateVisual();
    }

    private void StartRun()
    {
        _facingRight = _rng.Next(2) == 0;

        double w = Bounds.Width;
        _speed = 4 + _rng.NextDouble() * 3; // 4–7 px/frame
        _posX = _facingRight ? -120 : w + 120;
        _frame = 0;

        // Pick a behavior
        int roll = _rng.Next(100);
        if (roll < 45)
        {
            _behavior = Behavior.Cross;
        }
        else if (roll < 75)
        {
            _behavior = Behavior.CrossWithLoiter;
            // Loiter at 30–70% of the way across
            double frac = 0.3 + _rng.NextDouble() * 0.4;
            _actionPointX = _facingRight ? w * frac : w * (1 - frac);
        }
        else
        {
            _behavior = Behavior.TurnBack;
            // Turn back at 20–50% of the way across
            double frac = 0.2 + _rng.NextDouble() * 0.3;
            _actionPointX = _facingRight ? w * frac : w * (1 - frac);
        }

        _phase = Phase.Running;
    }

    private void TickRunning()
    {
        double prev = _posX;
        _posX += _facingRight ? _speed : -_speed;
        _frame++;

        double w = Bounds.Width;

        // Check for action point (loiter or turn-back)
        if (_behavior == Behavior.CrossWithLoiter &&
            CrossedPoint(prev, _posX, _actionPointX))
        {
            _phase = Phase.Loitering;
            _countdown = 1.0 + _rng.NextDouble() * 2.5; // loiter 1–3.5 s
            _behavior = Behavior.Cross; // after loitering, just finish crossing
            return;
        }

        if (_behavior == Behavior.TurnBack &&
            CrossedPoint(prev, _posX, _actionPointX))
        {
            _phase = Phase.Hesitating;
            _countdown = 0.4 + _rng.NextDouble() * 0.6; // brief 0.4–1 s pause
            return;
        }

        // Off screen → done
        if ((_facingRight && _posX > w + 120) || (!_facingRight && _posX < -120))
            GoIdle();
    }

    private void TickLoitering()
    {
        _countdown -= TickSec;
        // Keep rendering (no leg movement — frame is frozen)
        if (_countdown <= 0)
            _phase = Phase.Running;
    }

    private void TickHesitating()
    {
        _countdown -= TickSec;
        // Frozen in place, then flip and run back
        if (_countdown <= 0)
        {
            _facingRight = !_facingRight;
            _phase = Phase.Returning;
        }
    }

    private void TickReturning()
    {
        _posX += _facingRight ? _speed : -_speed;
        _frame++;

        double w = Bounds.Width;
        if ((_facingRight && _posX > w + 120) || (!_facingRight && _posX < -120))
            GoIdle();
    }

    private void GoIdle()
    {
        _phase = Phase.Idle;
        _countdown = 120 + _rng.NextDouble() * 180; // 2–5 minutes
    }

    /// <summary>Did we cross (or reach) targetX between prev and current?</summary>
    private static bool CrossedPoint(double prev, double current, double target) =>
        (prev <= target && current >= target) || (prev >= target && current <= target);

    // ══════════════════════════════════════════════════════════════
    //  Rendering
    // ══════════════════════════════════════════════════════════════

    public override void Render(DrawingContext context)
    {
        if (_phase == Phase.Idle) return;

        double h = Bounds.Height;
        double y = h * 0.92;
        double sc = Math.Max(0.6, h / 800.0) * 1.75; // 75% bigger than original
        int dir = _facingRight ? 1 : -1;

        // Leg animation — freeze when loitering or hesitating
        bool legsMoving = _phase != Phase.Loitering && _phase != Phase.Hesitating;
        double leg1 = legsMoving ? Math.Sin(_frame * 0.6) : 0;
        double leg2 = legsMoving ? Math.Sin(_frame * 0.6 + Math.PI) : 0;

        // Subtle body bob (only when moving)
        double bob = legsMoving ? Math.Abs(Math.Sin(_frame * 0.6)) * 1.5 * sc : 0;

        // Helper: place a point relative to mouse origin
        Point At(double dx, double dy) =>
            new(_posX + dx * sc * dir, y + dy * sc - bob);

        double S(double v) => v * sc;

        // ── Tail ──
        var tailPen = new Pen(BodyBrush, S(2.5));
        var tailGeo = new StreamGeometry();
        using (var tc = tailGeo.Open())
        {
            tc.BeginFigure(At(-15, -6), false);
            tc.CubicBezierTo(At(-28, -18), At(-34, -28), At(-24, -32));
            tc.EndFigure(false);
        }
        context.DrawGeometry(null, tailPen, tailGeo);

        // ── Legs ──
        var legPen = new Pen(BodyBrush, S(2.5));
        context.DrawLine(legPen, At(-8, 3), At(-8 + leg1 * 5, 13));
        context.DrawLine(legPen, At(-3, 3), At(-3 + leg2 * 5, 13));
        context.DrawLine(legPen, At(14, 3), At(14 + leg2 * 5, 13));
        context.DrawLine(legPen, At(19, 3), At(19 + leg1 * 5, 13));

        // ── Body ──
        context.DrawEllipse(BodyBrush, null, At(3, -4), S(18), S(11));

        // ── Head ──
        context.DrawEllipse(BodyBrush, null, At(24, -8), S(11), S(9));

        // ── Ears ──
        context.DrawEllipse(BodyBrush, null, At(18, -20), S(5), S(6));
        context.DrawEllipse(EarPinkBrush, null, At(18, -20), S(3.5), S(4.5));
        context.DrawEllipse(BodyBrush, null, At(28, -20), S(5), S(6));
        context.DrawEllipse(EarPinkBrush, null, At(28, -20), S(3.5), S(4.5));

        // ── Eye ──
        context.DrawEllipse(EyeBrush, null, At(28, -11), S(2), S(2));

        // ── Nose ──
        context.DrawEllipse(NoseBrush, null, At(34, -6), S(2.5), S(2));

        // ── Whiskers ──
        var wPen = new Pen(WhiskerBrush, S(1));
        context.DrawLine(wPen, At(32, -8), At(46, -13));
        context.DrawLine(wPen, At(33, -6), At(47, -6));
        context.DrawLine(wPen, At(32, -4), At(46, 1));
    }
}
