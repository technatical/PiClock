using System.Text.Json;
using System.Text.Json.Serialization;

namespace PiClock.Audio;

/// <summary>One playable song: absolute path plus per-song trim and fade settings (seconds).
/// End = 0 means play to the end of the file. A Spotify song has no path but a track id
/// (fade and gain do not apply to it). Animation names the scene that plays with it.</summary>
public sealed record Song(string Path, double Start, double FadeIn, double Gain = 0,
                          double End = 0, double FadeOut = 0,
                          string? SpotifyTrack = null, string Animation = "bus");

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
        /// <summary>Spotify song link (Share > Copy Song Link) or spotify:track: URI, in place of File.</summary>
        public string Spotify { get; set; } = "";
        /// <summary>Scene to show: "bus" (default) or "rainbow".</summary>
        public string Animation { get; set; } = "bus";
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
        public SpotifySettings? Spotify { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly List<Song> _songs = [];
    private readonly List<Song> _spotifySongs = [];
    private readonly Random _rng = new();
    private Song? _last, _lastSpotify;

    public LimitConfig Limit { get; } = new();
    public SpotifySettings Spotify { get; } = new();
    public string? ComeBackLaterPath { get; }
    public int Count => _songs.Count;
    public int SpotifyCount => _spotifySongs.Count;

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
        if (lib?.Spotify != null) Spotify = lib.Spotify;

        if (!string.IsNullOrWhiteSpace(Limit.ComeBackLater))
        {
            var clip = Path.Combine(assetsDir, Limit.ComeBackLater);
            if (File.Exists(clip)) ComeBackLaterPath = clip;
            else Console.Error.WriteLine($"songs.json: come-back clip not found: {clip}");
        }

        foreach (var e in lib?.Songs ?? [])
        {
            string animation = string.IsNullOrWhiteSpace(e.Animation) ? "bus" : e.Animation.Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(e.Spotify))
            {
                if (SpotifyTrackId(e.Spotify) is not { } track)
                {
                    Console.Error.WriteLine($"songs.json: not a Spotify song link, skipping: {e.Spotify}");
                    continue;
                }
                double spEnd = e.End > e.Start ? e.End : 0;
                _spotifySongs.Add(new Song("", Math.Max(0, e.Start), 0, 0, spEnd, 0, track, animation));
                continue;
            }
            if (string.IsNullOrWhiteSpace(e.File)) continue;
            var path = Path.Combine(assetsDir, e.File);
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"songs.json: song not found, skipping: {path}");
                continue;
            }
            double end = e.End > e.Start ? e.End : 0;
            _songs.Add(new Song(path, Math.Max(0, e.Start), Math.Max(0, e.FadeIn), e.Gain,
                                end, end > 0 ? Math.Max(0, e.FadeOut) : 0, null, animation));
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

    /// <summary>Random local song, avoiding an immediate repeat when there is a choice. Null if none.</summary>
    public Song? Pick() => PickFrom(_songs, ref _last);

    /// <summary>Random Spotify song, the same way. Null if the list has none.</summary>
    public Song? PickSpotify() => PickFrom(_spotifySongs, ref _lastSpotify);

    private Song? PickFrom(List<Song> songs, ref Song? last)
    {
        if (songs.Count == 0) return null;
        if (songs.Count == 1) return last = songs[0];

        Song pick;
        do pick = songs[_rng.Next(songs.Count)];
        while (pick == last);
        return last = pick;
    }

    /// <summary>The 22-character track id from https://open.spotify.com/track/ID?si=…
    /// (also with an /intl-xx/ segment) or spotify:track:ID. Null if it is neither.</summary>
    internal static string? SpotifyTrackId(string link)
    {
        link = link.Trim();
        string? id = null;
        if (link.StartsWith("spotify:track:", StringComparison.OrdinalIgnoreCase))
            id = link["spotify:track:".Length..];
        else if (Uri.TryCreate(link, UriKind.Absolute, out var uri)
                 && uri.Host.EndsWith("spotify.com", StringComparison.OrdinalIgnoreCase))
        {
            var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            int t = Array.IndexOf(parts, "track");
            if (t >= 0 && t + 1 < parts.Length) id = parts[t + 1];
        }
        return id is { Length: 22 } && id.All(char.IsAsciiLetterOrDigit) ? id : null;
    }
}
