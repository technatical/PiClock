using System.Text.Json;
using System.Text.Json.Serialization;

namespace PiClock.Audio;

/// <summary>One playable song: absolute path plus per-song trim and fade settings (seconds).
/// End = 0 means play to the end of the file.</summary>
public sealed record Song(string Path, double Start, double FadeIn, double Gain = 0,
                          double End = 0, double FadeOut = 0);

/// <summary>Play-limit settings from songs.json ("limit" section).</summary>
public sealed class LimitConfig
{
    /// <summary>How many plays trigger the nap.</summary>
    public int Plays { get; set; } = 3;
    /// <summary>The plays must all start within this many minutes.</summary>
    public double SpanMinutes { get; set; } = 5;
    /// <summary>How long the bus naps once triggered.</summary>
    public double CooldownMinutes { get; set; } = 10;
    /// <summary>Clip played when the bus is tapped during a nap (file name in assets).</summary>
    public string? ComeBackLater { get; set; } = "come-back-soon.mp3";
    /// <summary>Gain in dB applied to that clip (positive = louder).</summary>
    public double ComeBackLaterGain { get; set; }
    /// <summary>Minimum seconds between plays of that clip, so tap-spamming doesn't stutter it.</summary>
    public double ClipEverySeconds { get; set; } = 20;
    /// <summary>A tap only counts after this many quiet seconds since the previous tap,
    /// so a flurry of taps starts the bus once instead of starting and dismissing it.</summary>
    public double TapDebounceSeconds { get; set; } = 2;
}

/// <summary>
/// Loads assets/songs.json and hands out a random song per play (never the same one twice
/// in a row). If the file is missing, every MP3 in the assets folder is a song with no offset.
/// </summary>
public sealed class SongLibrary
{
    private sealed class SongEntry
    {
        public string File { get; set; } = "";
        public double Start { get; set; }
        public double FadeIn { get; set; }
        /// <summary>Gain in dB (positive = louder). Peaks are not limited, so keep it modest.</summary>
        public double Gain { get; set; }
        /// <summary>Seconds (from the start of the file) to stop at; 0 = end of file.
        /// Use it to trim a silent or fading tail so the bus and the music end together.</summary>
        public double End { get; set; }
        /// <summary>Seconds of fade-out ending at End.</summary>
        public double FadeOut { get; set; }
    }

    private sealed class LibraryFile
    {
        public List<SongEntry> Songs { get; set; } = [];
        public LimitConfig? Limit { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly List<Song> _songs = [];
    private readonly Random _rng = new();
    private Song? _last;

    public LimitConfig Limit { get; } = new();
    public string? ComeBackLaterPath { get; }
    public int Count => _songs.Count;

    public SongLibrary(string assetsDir)
    {
        var jsonPath = Path.Combine(assetsDir, "songs.json");
        LibraryFile? lib = null;
        try
        {
            if (File.Exists(jsonPath))
                lib = JsonSerializer.Deserialize<LibraryFile>(File.ReadAllText(jsonPath), JsonOptions);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"songs.json: {ex.Message}");
        }

        if (lib?.Limit != null) Limit = lib.Limit;

        if (!string.IsNullOrWhiteSpace(Limit.ComeBackLater))
        {
            var clip = Path.Combine(assetsDir, Limit.ComeBackLater);
            if (File.Exists(clip)) ComeBackLaterPath = clip;
            else Console.Error.WriteLine($"songs.json: come-back clip not found: {clip}");
        }

        foreach (var e in lib?.Songs ?? [])
        {
            if (string.IsNullOrWhiteSpace(e.File)) continue;
            var path = Path.Combine(assetsDir, e.File);
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"songs.json: song not found, skipping: {path}");
                continue;
            }
            double end = e.End > e.Start ? e.End : 0;
            _songs.Add(new Song(path, Math.Max(0, e.Start), Math.Max(0, e.FadeIn), e.Gain,
                                end, end > 0 ? Math.Max(0, e.FadeOut) : 0));
        }

        // No list (or nothing usable in it): every MP3 in the folder is a song
        if (_songs.Count == 0 && Directory.Exists(assetsDir))
        {
            foreach (var mp3 in Directory.EnumerateFiles(assetsDir, "*.mp3").OrderBy(p => p))
            {
                if (string.Equals(mp3, ComeBackLaterPath, StringComparison.OrdinalIgnoreCase)) continue;
                _songs.Add(new Song(mp3, 0, 0));
            }
        }
    }

    /// <summary>Random song, avoiding an immediate repeat when there is a choice. Null if none.</summary>
    public Song? Pick()
    {
        if (_songs.Count == 0) return null;
        if (_songs.Count == 1) return _last = _songs[0];

        Song pick;
        do pick = _songs[_rng.Next(_songs.Count)];
        while (pick == _last);
        return _last = pick;
    }
}
