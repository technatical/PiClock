namespace PiClock.Audio;

/// <summary>
/// "Three plays in five minutes, then a ten minute nap."
/// Play starts are recorded; when a play ends and the last <see cref="Plays"/> starts all fall
/// within <see cref="Span"/>, the bus is locked for <see cref="Cooldown"/>.
/// </summary>
public sealed class PlayLimiter
{
    private readonly List<DateTime> _starts = [];

    public int Plays { get; }
    public TimeSpan Span { get; }
    public TimeSpan Cooldown { get; }

    /// <summary>When the current nap ends, or null when not napping.</summary>
    public DateTime? LockEnd { get; private set; }

    public PlayLimiter(LimitConfig cfg)
    {
        Plays    = Math.Max(1, cfg.Plays);
        Span     = TimeSpan.FromMinutes(Math.Max(0.1, cfg.SpanMinutes));
        Cooldown = TimeSpan.FromMinutes(Math.Max(0.1, cfg.CooldownMinutes));
    }

    public bool IsLocked(DateTime now) => LockEnd is { } end && now < end;

    public TimeSpan Remaining(DateTime now) =>
        LockEnd is { } end && now < end ? end - now : TimeSpan.Zero;

    /// <summary>1.0 when the nap starts, 0.0 when it is over.</summary>
    public double RemainingFraction(DateTime now) =>
        Math.Clamp(Remaining(now).TotalSeconds / Cooldown.TotalSeconds, 0, 1);

    public void RecordPlayStart(DateTime now)
    {
        _starts.Add(now);
        while (_starts.Count > Plays) _starts.RemoveAt(0);
    }

    /// <summary>Call when a play ends. Returns true if a nap just began.</summary>
    public bool CheckAfterPlay(DateTime now)
    {
        if (IsLocked(now) || _starts.Count < Plays) return false;

        var spread = _starts[^1] - _starts[0];
        if (spread > Span) return false;

        LockEnd = now + Cooldown;
        _starts.Clear();
        return true;
    }

    public void ClearLock()
    {
        LockEnd = null;
        _starts.Clear();
    }
}
