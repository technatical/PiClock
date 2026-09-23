using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PiClock.Lights;

/// <summary>
/// <c>PiClock --hue-pair &lt;bridge-ip&gt; ["Room name"]</c>: waits for the bridge's link
/// button, then writes assets/hue.json with the new app key. Without a room name it lists
/// the rooms and zones so one can be filled in.
/// </summary>
public static class HuePairing
{
    public static int Run(string[] args)
    {
        int i = Array.IndexOf(args, "--hue-pair");
        string? bridge = i + 1 < args.Length ? args[i + 1] : null;
        string room = i + 2 < args.Length ? args[i + 2] : "";
        if (string.IsNullOrWhiteSpace(bridge) || bridge.StartsWith("--"))
        {
            Console.Error.WriteLine("usage: PiClock --hue-pair <bridge-ip> [\"Room name\"]");
            Console.Error.WriteLine("The bridge IP is in the Hue app: Settings > Bridges > (i).");
            return 2;
        }

        try { return RunAsync(bridge, room).GetAwaiter().GetResult(); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Pairing failed: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> RunAsync(string bridge, string room)
    {
        using var http = HueLights.CreateClient(bridge);

        Console.WriteLine($"Press the round link button on the Hue bridge at {bridge} (waiting 60 s)...");
        string? key = null;
        var until = DateTime.UtcNow.AddSeconds(60);
        while (key is null && DateTime.UtcNow < until)
        {
            using var resp = await http.PostAsJsonAsync("api", new { devicetype = "piclock#" + Environment.MachineName });
            var reply = await resp.Content.ReadFromJsonAsync<JsonArray>();
            var first = reply?.FirstOrDefault();
            key = first?["success"]?["username"]?.GetValue<string>();

            // 101 = link button not pressed yet; anything else is a real error
            if (key is null && first?["error"]?["type"]?.GetValue<int>() is int err and not 101)
            {
                Console.Error.WriteLine($"Bridge refused: {first["error"]?["description"]}");
                return 1;
            }
            if (key is null) await Task.Delay(2000);
        }
        if (key is null)
        {
            Console.Error.WriteLine("Timed out; run it again and press the button while it waits.");
            return 1;
        }
        Console.WriteLine("Paired.");

        // Show what can go in "room"
        http.DefaultRequestHeaders.Add("hue-application-key", key);
        var names = new List<string>();
        foreach (var kind in new[] { "room", "zone" })
        {
            var doc = await http.GetFromJsonAsync<JsonObject>($"clip/v2/resource/{kind}");
            foreach (var g in doc?["data"]?.AsArray() ?? [])
                if (g?["metadata"]?["name"]?.GetValue<string>() is { } n)
                    names.Add($"{n} ({kind})");
        }
        Console.WriteLine("Rooms and zones on this bridge:");
        foreach (var n in names.OrderBy(n => n)) Console.WriteLine($"  {n}");

        var cfg = new HueConfig { Bridge = bridge, AppKey = key, Room = room };
        var assets = Path.Combine(AppContext.BaseDirectory, "assets");
        Directory.CreateDirectory(assets);
        var path = Path.Combine(assets, "hue.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(cfg, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));

        Console.WriteLine($"Wrote {path}");
        if (string.IsNullOrWhiteSpace(room))
            Console.WriteLine("Set \"room\" in that file to one of the names above, then restart PiClock.");
        return 0;
    }
}
