using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

namespace PiClock.Audio;

/// <summary>Spotify settings from songs.json ("spotify" section). No secrets live here: the
/// Soloist API key is in Soloist's own service, and its login is stored in its data folder.</summary>
public sealed class SpotifySettings
{
    /// <summary>Spotify Soloist's local WebSocket API (its --ws address).</summary>
    public string Soloist { get; set; } = "ws://127.0.0.1:9090";
    /// <summary>Volume to set before each play, 1–100 %. 0 leaves it alone.</summary>
    public int Volume { get; set; }
}

/// <summary>
/// Plays Spotify songs through Spotify Soloist, Spotify's headless Connect player, running on
/// the Pi with its WebSocket API on localhost. Soloist streams the audio into PipeWire (and so
/// to the Echo over Bluetooth); this class only says what to play, where to start and when to
/// pause. Everything is best-effort and runs off the UI thread in order; failures only write
/// a line to stderr and the scene plays without music.
/// </summary>
public sealed class SpotifyPlayer : IDisposable
{
    private readonly SpotifySettings _cfg;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private Task _chain = Task.CompletedTask;

    private ClientWebSocket? _ws;
    private bool _playing;

    /// <summary>The song just asked for: its start is applied and its length reported once
    /// Soloist says that track is loaded.</summary>
    private sealed class Pending(string uri, double start, Action<double>? onLength)
    {
        public string Uri { get; } = uri;
        public double Start { get; } = start;
        public bool Seeked { get; set; } = start <= 0;
        public Action<double>? OnLength { get; set; } = onLength;
    }
    private volatile Pending? _pending;

    public SpotifyPlayer(SpotifySettings cfg) => _cfg = cfg;

    /// <summary>Starts the song. When it has no "end", its length (seconds from "start") is
    /// handed to <paramref name="onLength"/> once known, off the UI thread.</summary>
    public void Play(Song song, Action<double>? onLength = null) => Enqueue(async () =>
    {
        var ws = await ConnectAsync();
        _pending = new Pending($"spotify:track:{song.SpotifyTrack}", song.Start, song.End <= 0 ? onLength : null);

        if (_cfg.Volume > 0)
            await SendAsync(ws, new JsonObject { ["command"] = "set_volume", ["volume"] = Math.Clamp(_cfg.Volume, 1, 100) });
        await SendAsync(ws, new JsonObject { ["command"] = "play", ["uri"] = _pending.Uri });
        _playing = true;
    });

    /// <summary>Pauses if a song was started (the scene ended or was sent away).</summary>
    public void Stop() => Enqueue(async () =>
    {
        _pending = null;
        if (!_playing) return;
        _playing = false;
        await SendAsync(await ConnectAsync(), new JsonObject { ["command"] = "pause" });
    });

    /// <summary>Pauses synchronously (bounded wait), for shutdown mid-song.</summary>
    public void StopNow(TimeSpan timeout)
    {
        Stop();
        Task chain;
        lock (_gate) chain = _chain;
        try { chain.Wait(timeout); } catch { }
    }

    public void Dispose()
    {
        _ws?.Abort();
        _ws?.Dispose();
    }

    private void Enqueue(Func<Task> op)
    {
        lock (_gate)
        {
            _chain = _chain.ContinueWith(async _ =>
            {
                try { await op(); }
                catch (Exception ex) { Console.Error.WriteLine($"Spotify: {ex.Message}"); }
            }, TaskScheduler.Default).Unwrap();
        }
    }

    /// <summary>The open connection, or a new one (Soloist may have restarted since).</summary>
    private async Task<ClientWebSocket> ConnectAsync()
    {
        if (_ws is { State: WebSocketState.Open } open) return open;
        _ws?.Dispose();

        var ws = new ClientWebSocket();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try { await ws.ConnectAsync(new Uri(_cfg.Soloist), timeout.Token); }
        catch (Exception ex)
        {
            ws.Dispose();
            _ws = null;
            throw new InvalidOperationException($"cannot reach Spotify Soloist at {_cfg.Soloist} ({ex.Message}); is it running?");
        }
        _ws = ws;
        _ = Task.Run(() => ReceiveLoopAsync(ws));
        return ws;
    }

    private async Task SendAsync(ClientWebSocket ws, JsonObject command)
    {
        command["type"] = "command";
        var bytes = Encoding.UTF8.GetBytes(command.ToJsonString());
        await _sendLock.WaitAsync();
        try { await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None); }
        finally { _sendLock.Release(); }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket ws)
    {
        var buffer = new byte[16 * 1024];
        var message = new MemoryStream();
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                var r = await ws.ReceiveAsync(buffer, CancellationToken.None);
                if (r.MessageType == WebSocketMessageType.Close) break;
                message.Write(buffer, 0, r.Count);
                if (!r.EndOfMessage) continue;

                var text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                message.SetLength(0);
                await HandleEventAsync(ws, JsonNode.Parse(text) as JsonObject);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException) { /* reconnects on the next play */ }
        catch (Exception ex) { Console.Error.WriteLine($"Spotify: {ex.Message}"); }
    }

    private async Task HandleEventAsync(ClientWebSocket ws, JsonObject? ev)
    {
        switch (ev?["type"]?.GetValue<string>())
        {
            case "auth_state" when ev["logged_in"]?.GetValue<bool>() == false:
                Console.Error.WriteLine($"Spotify: Soloist \"{ev["device_name"]}\" is not logged in; pick it once "
                                        + "in the Spotify app's device list to pair it");
                break;

            case "error":
                Console.Error.WriteLine($"Spotify: Soloist: {ev["message"]}");
                break;

            // The asked-for track is loaded: jump to its start and report its length
            case "track_changed" or "playback_state":
                if (_pending is not { } p || ev["item"]?["uri"]?.GetValue<string>() != p.Uri) break;

                if (!p.Seeked)
                {
                    p.Seeked = true;
                    await SendAsync(ws, new JsonObject { ["command"] = "seek", ["position_ms"] = (int)Math.Round(p.Start * 1000) });
                }
                if (p.OnLength is { } report
                    && ev["item"]?["decorations"]?["playback"]?["duration_ms"]?.GetValue<double>() is double ms and > 0)
                {
                    p.OnLength = null;
                    report(Math.Max(5, ms / 1000 - p.Start));
                }
                break;
        }
    }
}
