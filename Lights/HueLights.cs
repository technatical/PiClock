using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace PiClock.Lights;

/// <summary>Settings from assets/hue.json. The file lives on the Pi only (it holds the
/// bridge's app key) and is written by <c>PiClock --hue-pair</c>.</summary>
public sealed class HueConfig
{
    /// <summary>Bridge IP address or host name.</summary>
    public string Bridge { get; set; } = "";
    /// <summary>Application key the bridge issued at pairing time.</summary>
    public string AppKey { get; set; } = "";
    /// <summary>Name of the Hue room or zone whose lights follow the bus.</summary>
    public string Room { get; set; } = "";
    /// <summary>Colour while the bus is on screen, as #RRGGBB.</summary>
    public string Color { get; set; } = "#FFC20E";
    /// <summary>Brightness while the bus is on screen, 1–100 %.</summary>
    public double Brightness { get; set; } = 80;
    /// <summary>Fade time for both the change and the restore.</summary>
    public double TransitionSeconds { get; set; } = 1.5;

    /// <summary>Shades the lights dance between during the song, as #RRGGBB.</summary>
    public List<string> DanceColors { get; set; } =
        ["#FFC20E", "#FFD700", "#FFB300", "#FFE45C", "#FFA000", "#FFF06A"];
    /// <summary>Seconds per dance step (each step fades over most of it). 0 = hold one colour.</summary>
    public double DanceSeconds { get; set; } = 2;
    /// <summary>Dance brightness is picked between this and <see cref="Brightness"/>.</summary>
    public double DanceBrightnessMin { get; set; } = 45;
}

/// <summary>
/// Hue Bridge (Pro) integration over the local CLIP v2 API: when the bus arrives the lights
/// in one room/zone turn bus-coloured and dance between shades of yellow, and when it leaves they go back to exactly how they were.
/// Everything is best-effort and runs off the UI thread; a missing or unreachable bridge
/// only writes a line to stderr.
/// </summary>
public sealed class HueLights : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    // The bridge accepts roughly 10 light commands a second; stay under that.
    private const int CommandSpacingMs = 110;

    private readonly HueConfig _cfg;
    private readonly HttpClient _http;
    private readonly object _gate = new();
    private Task _chain = Task.CompletedTask;

    /// <summary>Light id → the state to put back when the bus leaves. Null while no
    /// snapshot is held (the bus is not on screen, or the snapshot failed).</summary>
    private Dictionary<string, JsonObject>? _saved;

    /// <summary>The dance loop while the bus is on screen; cancelled before the restore.</summary>
    private CancellationTokenSource? _danceCts;
    private Task _dance = Task.CompletedTask;
    private readonly Random _rng = new();

    private HueLights(HueConfig cfg)
    {
        _cfg = cfg;
        _http = CreateClient(cfg.Bridge);
        _http.DefaultRequestHeaders.Add("hue-application-key", cfg.AppKey);
    }

    /// <summary>Loads hue.json from the assets folder. Null (lights disabled) if the file is
    /// missing or incomplete.</summary>
    public static HueLights? Load(string assetsDir)
    {
        var path = Path.Combine(assetsDir, "hue.json");
        if (!File.Exists(path)) return null;
        try
        {
            var cfg = JsonSerializer.Deserialize<HueConfig>(File.ReadAllText(path), JsonOptions);
            if (cfg is null || string.IsNullOrWhiteSpace(cfg.Bridge)
                || string.IsNullOrWhiteSpace(cfg.AppKey) || string.IsNullOrWhiteSpace(cfg.Room))
            {
                Console.Error.WriteLine("hue.json: bridge, appKey and room are all required; Hue disabled");
                return null;
            }
            return new HueLights(cfg);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"hue.json: {ex.Message}; Hue disabled");
            return null;
        }
    }

    /// <summary>The bus is arriving: remember the room's lights, then turn them bus-coloured.</summary>
    public void BusArrived() => Enqueue(ArriveAsync);

    /// <summary>The bus is leaving: put the lights back the way they were.</summary>
    public void BusLeft() => Enqueue(RestoreAsync);

    /// <summary>Restores synchronously (bounded wait), for shutdown mid-song.</summary>
    public void RestoreNow(TimeSpan timeout)
    {
        BusLeft();
        Task chain;
        lock (_gate) chain = _chain;
        try { chain.Wait(timeout); } catch { }
    }

    public void Dispose() => _http.Dispose();

    // Operations run strictly in order, so a quick tap-away can't restore before the
    // snapshot has been taken.
    private void Enqueue(Func<Task> op)
    {
        lock (_gate)
        {
            _chain = _chain.ContinueWith(async _ =>
            {
                try { await op(); }
                catch (Exception ex) { Console.Error.WriteLine($"Hue: {ex.Message}"); }
            }, TaskScheduler.Default).Unwrap();
        }
    }

    private async Task ArriveAsync()
    {
        var lights = await RoomLightsAsync();
        if (lights.Count == 0) return;

        // Keep the first snapshot if the bus comes back before a restore finished.
        _saved ??= lights.ToDictionary(l => l.Id, l => RestoreBody(l.State));

        var (x, y) = HexToXy(_cfg.Color);
        foreach (var light in lights)
        {
            var body = new JsonObject
            {
                ["on"] = new JsonObject { ["on"] = true },
                ["dimming"] = new JsonObject { ["brightness"] = Math.Clamp(_cfg.Brightness, 1, 100) },
                ["dynamics"] = Dynamics()
            };
            // White-only bulbs just get the brightness.
            if (light.State["color"] is not null)
                body["color"] = new JsonObject { ["xy"] = new JsonObject { ["x"] = x, ["y"] = y } };

            await PutLightAsync(light.Id, body);
        }

        var colorLights = lights.Where(l => l.State["color"] is not null).Select(l => l.Id).ToList();
        var palette = _cfg.DanceColors.Select(HexToXy).Distinct().ToList();
        if (_cfg.DanceSeconds > 0 && palette.Count > 1 && colorLights.Count > 0 && _danceCts is null)
        {
            _danceCts = new CancellationTokenSource();
            _dance = DanceAsync(colorLights, palette, _danceCts.Token);
        }
    }

    /// <summary>Every step each colour bulb fades to a different shade and brightness than it
    /// had, so the room shimmers instead of flashing in unison. Runs until the bus leaves.</summary>
    private async Task DanceAsync(List<string> ids, List<(double X, double Y)> palette, CancellationToken ct)
    {
        var step = TimeSpan.FromSeconds(Math.Max(1, _cfg.DanceSeconds));
        // Fade over most of the step so one shade flows into the next without a pause.
        int fadeMs = (int)(step.TotalMilliseconds * 0.9);
        double hi = Math.Clamp(_cfg.Brightness, 1, 100);
        double lo = Math.Clamp(Math.Min(_cfg.DanceBrightnessMin, hi), 1, 100);
        var current = new Dictionary<string, int>();

        try
        {
            // Let the arrival fade finish first.
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, _cfg.TransitionSeconds)), ct);
            while (!ct.IsCancellationRequested)
            {
                var started = DateTime.UtcNow;
                foreach (var id in ids)
                {
                    int pick;
                    do pick = _rng.Next(palette.Count);
                    while (current.TryGetValue(id, out int last) && pick == last);
                    current[id] = pick;

                    var (x, y) = palette[pick];
                    await PutLightAsync(id, new JsonObject
                    {
                        ["dimming"] = new JsonObject { ["brightness"] = Math.Round(lo + _rng.NextDouble() * (hi - lo)) },
                        ["color"] = new JsonObject { ["xy"] = new JsonObject { ["x"] = x, ["y"] = y } },
                        ["dynamics"] = new JsonObject { ["duration"] = fadeMs }
                    }, ct);
                }
                var left = step - (DateTime.UtcNow - started);
                if (left > TimeSpan.Zero) await Task.Delay(left, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Console.Error.WriteLine($"Hue dance: {ex.Message}"); }
    }

    private async Task RestoreAsync()
    {
        if (_danceCts is { } cts)
        {
            _danceCts = null;
            cts.Cancel();
            await _dance;
            cts.Dispose();
        }

        if (_saved is not { } saved) return;
        _saved = null;
        foreach (var (id, body) in saved)
        {
            body["dynamics"] = Dynamics();
            await PutLightAsync(id, body);
        }
    }

    private JsonObject Dynamics() =>
        new() { ["duration"] = (int)Math.Round(Math.Max(0, _cfg.TransitionSeconds) * 1000) };

    /// <summary>Builds the PUT body that returns a light to the state in a GET response.</summary>
    private static JsonObject RestoreBody(JsonObject state)
    {
        bool on = state["on"]?["on"]?.GetValue<bool>() ?? false;
        var body = new JsonObject { ["on"] = new JsonObject { ["on"] = on } };
        if (!on) return body;

        if (state["dimming"]?["brightness"] is { } bri)
            body["dimming"] = new JsonObject { ["brightness"] = bri.GetValue<double>() };

        // A light is in either colour-temperature mode (mirek_valid) or xy colour mode.
        var ct = state["color_temperature"];
        if (ct?["mirek_valid"]?.GetValue<bool>() == true && ct["mirek"] is { } mirek)
            body["color_temperature"] = new JsonObject { ["mirek"] = mirek.GetValue<int>() };
        else if (state["color"]?["xy"] is JsonObject xy)
            body["color"] = new JsonObject { ["xy"] = xy.DeepClone() };

        return body;
    }

    private sealed record Light(string Id, JsonObject State);

    /// <summary>The lights in the configured room or zone, with their current state.
    /// Resolved on every play so renamed rooms or new bulbs are picked up.</summary>
    private async Task<List<Light>> RoomLightsAsync()
    {
        var all = await GetDataAsync("light");
        var group = (await GetDataAsync("room")).Concat(await GetDataAsync("zone"))
            .FirstOrDefault(g => string.Equals(g["metadata"]?["name"]?.GetValue<string>(),
                                               _cfg.Room.Trim(), StringComparison.OrdinalIgnoreCase));
        if (group is null)
        {
            Console.Error.WriteLine($"Hue: no room or zone named \"{_cfg.Room}\"");
            return [];
        }

        // Rooms list devices as children, zones list lights directly.
        var ids = new HashSet<string>();
        foreach (var child in group["children"]?.AsArray() ?? [])
        {
            string? rid = child?["rid"]?.GetValue<string>();
            switch (child?["rtype"]?.GetValue<string>())
            {
                case "light":
                    if (rid != null) ids.Add(rid);
                    break;
                case "device":
                    foreach (var l in all.Where(l => l["owner"]?["rid"]?.GetValue<string>() == rid))
                        ids.Add(l["id"]!.GetValue<string>());
                    break;
            }
        }

        return all.Where(l => ids.Contains(l["id"]!.GetValue<string>()))
                  .Select(l => new Light(l["id"]!.GetValue<string>(), l))
                  .ToList();
    }

    private async Task<List<JsonObject>> GetDataAsync(string resource)
    {
        var doc = await _http.GetFromJsonAsync<JsonObject>($"clip/v2/resource/{resource}");
        return doc?["data"]?.AsArray().OfType<JsonObject>().ToList() ?? [];
    }

    private async Task PutLightAsync(string id, JsonObject body, CancellationToken ct = default)
    {
        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var resp = await _http.PutAsync($"clip/v2/resource/light/{id}", content, ct);
        if (!resp.IsSuccessStatusCode)
            Console.Error.WriteLine($"Hue: light {id}: {(int)resp.StatusCode} {await resp.Content.ReadAsStringAsync(ct)}");
        await Task.Delay(CommandSpacingMs, ct);
    }

    /// <summary>The bridge serves HTTPS with a certificate from the Hue root CA that has the
    /// bridge id, not its IP, as its name, so the usual validation would always fail. The
    /// bridge is on the local network and addressed by IP, so any certificate is accepted.</summary>
    internal static HttpClient CreateClient(string bridge) =>
        new(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        })
        {
            BaseAddress = new Uri($"https://{bridge.Trim()}/"),
            Timeout = TimeSpan.FromSeconds(5)
        };

    /// <summary>#RRGGBB → CIE 1931 xy, using the sRGB gamma curve and the wide-gamut matrix
    /// Philips documents for Hue. The bridge clamps the result to each bulb's gamut.</summary>
    internal static (double X, double Y) HexToXy(string hex)
    {
        hex = hex.Trim().TrimStart('#');
        if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
            rgb = 0xFFC20E;

        static double Lin(int c)
        {
            double v = c / 255.0;
            return v > 0.04045 ? Math.Pow((v + 0.055) / 1.055, 2.4) : v / 12.92;
        }

        double r = Lin((rgb >> 16) & 0xFF), g = Lin((rgb >> 8) & 0xFF), b = Lin(rgb & 0xFF);
        double X = r * 0.664511 + g * 0.154324 + b * 0.162028;
        double Y = r * 0.283881 + g * 0.668433 + b * 0.047685;
        double Z = r * 0.000088 + g * 0.072310 + b * 0.986039;
        double sum = X + Y + Z;
        if (sum <= 0) return (0.3227, 0.3290);   // black → neutral white point
        return (Math.Round(X / sum, 4), Math.Round(Y / sum, 4));
    }
}
