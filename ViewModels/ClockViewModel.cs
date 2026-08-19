using Avalonia.Threading;

namespace PiClock.ViewModels;

public class ClockViewModel : ViewModelBase
{
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _shiftTimer;
    private readonly Random _shiftRandom = new();

    private bool _isAnalogMode = true;
    private int _currentHour;
    private int _currentMinute;
    private int _currentSecond;
    private int _hourTens;
    private int _hourOnes;
    private int _minuteTens;
    private int _minuteOnes;
    private bool _colonVisible = true;
    private string _amPm = "AM";
    private char _dayChar1 = ' ';
    private char _dayChar2 = ' ';
    private char _dayChar3 = ' ';
    private int _monthTens;
    private int _monthOnes;
    private int _dateTens;
    private int _dateOnes;
    private string _dayText = "";
    private string _monthDateText = "";
    private double _shiftX;
    private double _shiftY;
    private double _displayOpacity = 1.0;

    // ── Mode toggle ──
    public bool IsAnalogMode
    {
        get => _isAnalogMode;
        set => SetProperty(ref _isAnalogMode, value);
    }

    // ── Analog clock properties ──
    public int  CurrentHour   { get => _currentHour;   set => SetProperty(ref _currentHour, value); }
    public int  CurrentMinute { get => _currentMinute;  set => SetProperty(ref _currentMinute, value); }
    public int  CurrentSecond { get => _currentSecond;  set => SetProperty(ref _currentSecond, value); }

    // ── Digital clock properties ──
    public int  HourTens     { get => _hourTens;     set => SetProperty(ref _hourTens, value); }
    public int  HourOnes     { get => _hourOnes;     set => SetProperty(ref _hourOnes, value); }
    public int  MinuteTens   { get => _minuteTens;   set => SetProperty(ref _minuteTens, value); }
    public int  MinuteOnes   { get => _minuteOnes;   set => SetProperty(ref _minuteOnes, value); }
    public bool ColonVisible { get => _colonVisible; set => SetProperty(ref _colonVisible, value); }
    public string AmPm       { get => _amPm;         set => SetProperty(ref _amPm, value); }
    public char DayChar1     { get => _dayChar1;     set => SetProperty(ref _dayChar1, value); }
    public char DayChar2     { get => _dayChar2;     set => SetProperty(ref _dayChar2, value); }
    public char DayChar3     { get => _dayChar3;     set => SetProperty(ref _dayChar3, value); }
    public int  MonthTens    { get => _monthTens;    set => SetProperty(ref _monthTens, value); }
    public int  MonthOnes    { get => _monthOnes;    set => SetProperty(ref _monthOnes, value); }
    public int  DateTens     { get => _dateTens;     set => SetProperty(ref _dateTens, value); }
    public int  DateOnes     { get => _dateOnes;     set => SetProperty(ref _dateOnes, value); }

    // ── Analog-mode text properties (clean font, no segments) ──
    public string DayText       { get => _dayText;       set => SetProperty(ref _dayText, value); }
    public string MonthDateText { get => _monthDateText; set => SetProperty(ref _monthDateText, value); }

    // ── Anti-burn-in pixel shift (±4px every 3–5 min) ──
    public double ShiftX { get => _shiftX; set => SetProperty(ref _shiftX, value); }
    public double ShiftY { get => _shiftY; set => SetProperty(ref _shiftY, value); }

    // ── Night dimming (11 PM – 5 AM) ──
    public double DisplayOpacity { get => _displayOpacity; set => SetProperty(ref _displayOpacity, value); }

    public void ToggleMode() => IsAnalogMode = !IsAnalogMode;

    public ClockViewModel()
    {
        UpdateTime();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => UpdateTime();
        _timer.Start();

        // Subtle pixel shift to prevent LCD image retention
        _shiftTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(3) };
        _shiftTimer.Tick += (_, _) => ApplyPixelShift();
        _shiftTimer.Start();
    }

    private void ApplyPixelShift()
    {
        ShiftX = _shiftRandom.Next(-4, 5);
        ShiftY = _shiftRandom.Next(-4, 5);
        _shiftTimer.Interval = TimeSpan.FromMinutes(3 + _shiftRandom.NextDouble() * 2);
    }

    private void UpdateTime()
    {
        var now = DateTime.Now;

        // Analog clock hands (second precision for smooth sweep)
        CurrentHour   = now.Hour;
        CurrentMinute = now.Minute;
        CurrentSecond = now.Second;

        // Colon blinks on each second boundary
        ColonVisible = now.Millisecond < 500;

        // 12-hour format
        int hour12 = now.Hour % 12;
        if (hour12 == 0) hour12 = 12;

        HourTens   = hour12 >= 10 ? hour12 / 10 : -1; // -1 = blank leading digit
        HourOnes   = hour12 % 10;
        MinuteTens = now.Minute / 10;
        MinuteOnes = now.Minute % 10;

        AmPm = now.Hour >= 12 ? "PM" : "AM";

        // Day of week as individual characters for 16-segment display
        var day = now.ToString("ddd").ToUpper(); // MON, TUE, …
        DayChar1 = day[0];
        DayChar2 = day[1];
        DayChar3 = day[2];

        MonthTens = now.Month / 10;
        MonthOnes = now.Month % 10;
        DateTens  = now.Day / 10;
        DateOnes  = now.Day % 10;

        // Text versions for analog-mode right panel
        DayText       = now.ToString("ddd").ToUpper();
        MonthDateText = $"{now.Month:D2}/{now.Day:D2}";

        // Dim display 11 PM – 5 AM
        DisplayOpacity = (now.Hour >= 23 || now.Hour < 5) ? 0.3 : 1.0;
    }
}
