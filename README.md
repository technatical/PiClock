# PiClock 🕐🐭

A clock for the **Raspberry Pi 5** with a **WaveShare 8-DSI-TOUCH-A** display (1280×800), built with [Avalonia UI](https://avaloniaui.net/) and .NET 8.

Three display modes (the default is the calendar):

- **Analog** — clean white clock face with capsule hands, dimmed dial, bold blue day/date text
- **Digital** — seven-segment time digits with sixteen-segment day-of-week and seven-segment date
- **Calendar** — month grid with today highlighted, analog clock alongside

**Toddler mode:** tapping the screen drives a bus in from the bottom and plays a song (*Wheels on the Bus*, or a random pick from a library) through a Bluetooth speaker (an Echo Studio). The bus parks for the length of the song and drives off near the end. Tap again to send it away early. Swipe gestures (cycle modes, dismiss the bus) exist in the code but are commented out so small hands can't get lost.

**Spotify songs with their own scenes:** holding a finger on the screen for a moment plays a song from Spotify instead (Ms. Rachel's *I Love a Rainbow*), with a scene made for it: the same sunny backdrop as the bus, but a rainbow paints itself in band by band, ripples and shimmers with smiling clouds on its feet and the colour names taking turns at the top, then wipes away as the song ends. The Hue lights dance through rainbow colours instead of yellow. Spotify plays through [Spotify Soloist](https://developer.spotify.com/documentation/soloist), Spotify's own Connect player for Linux, on the Pi.

**Nap time:** three plays that all start within 5 minutes put the bus to sleep for 90 seconds (both numbers are settings). A sleeping bus with a shrinking bar appears along the bottom (small digits in the corner for grown-ups), and tapping during the nap plays a short "come back soon" clip instead of the song. All of it is set in `assets/songs.json`.

A little gray mouse with pink ears scurries across the bottom of the screen every few minutes. Sometimes it crosses all the way, sometimes it stops to look around, and sometimes it hesitates and runs back the way it came.

## Prerequisites

| What | Install |
|------|---------|
| .NET 8 SDK | [dot.net/download](https://dot.net/download) |
| VS Code | [code.visualstudio.com](https://code.visualstudio.com) |
| C# Dev Kit extension | `ms-dotnettools.csdevkit` in VS Code |
| Avalonia for VS Code | `avaloniateam.vscode-avalonia` in VS Code |

## Development (Windows)

```bash
dotnet run
```

| Key | Action |
|-----|--------|
| `F11` | Toggle fullscreen |
| `Escape` | Exit |
| Click/tap | Toggle the bus animation (and audio, if mpv is present) |
| Long press (0.7 s) / `S` | Play a Spotify song and its scene (the scene plays silently without Soloist) |

## Deploy to Raspberry Pi 5

### One-command deploy

From Windows, with SSH key auth set up:

```powershell
.\deploy.ps1
```

This publishes for `linux-arm64`, stops the PiClock service on the Pi, copies the files, and restarts it. Defaults to `mgibson@192.168.7.113` — override with `.\deploy.ps1 -Pi <ip> -User <name>`.

### First-time Pi setup

#### 1. Install .NET 8

```bash
curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 8.0
echo 'export DOTNET_ROOT=$HOME/.dotnet' >> ~/.bashrc
echo 'export PATH=$PATH:$HOME/.dotnet' >> ~/.bashrc
source ~/.bashrc
```

#### 2. WaveShare DSI display

Add to `/boot/firmware/config.txt`:

```
dtoverlay=vc4-kms-dsi-waveshare-panel-v2,8_0_inch_a
```

#### 3. Landscape rotation (Wayland)

The display is natively portrait (800×1280). Rotate it via labwc autostart:

```bash
mkdir -p ~/.config/labwc
echo 'wlr-randr --output DSI-2 --transform 270' >> ~/.config/labwc/autostart
```

#### 4. Hide the system cursor

```bash
echo 'XCURSOR_SIZE=1' >> ~/.config/labwc/environment
```

#### 5. Auto-start on boot

```bash
sudo tee /etc/systemd/system/piclock.service << 'EOF'
[Unit]
Description=PiClock Display
After=graphical.target

[Service]
Environment=DISPLAY=:0
Environment=XAUTHORITY=/home/mgibson/.Xauthority
ExecStart=/home/mgibson/PiClock/PiClock --kiosk
User=mgibson
Restart=always
RestartSec=5

[Install]
WantedBy=graphical.target
EOF

sudo systemctl enable piclock
sudo systemctl start piclock
```

#### 6. Bluetooth audio (Wheels on the Bus)

Audio is best-effort: if any of this is missing the bus still drives, just silently.

Install the players the app shells out to (`mpv` plays, `ffprobe` reads the song length so the animation matches it):

```bash
sudo apt install mpv ffmpeg
```

Pair and trust the speaker once (the app uses the Echo Studio's MAC, set in `Views/MainWindow.axaml.cs`):

```bash
bluetoothctl
# in the bluetoothctl prompt, with the Echo in pairing mode:
scan on
pair B8:5F:98:E4:FD:B1
trust B8:5F:98:E4:FD:B1
connect B8:5F:98:E4:FD:B1
quit
```

**Tell WirePlumber the Pi only sends audio.** By default the Pi advertises itself as a Bluetooth *speaker* too, and the Echo Studio will happily connect *to* the Pi and stream into it. When that happens PipeWire has no Echo output sink, `bluetoothctl connect` fails with `br-connection-busy`, and mpv plays into a dummy device. Restricting the role prevents it:

```bash
mkdir -p ~/.config/wireplumber/wireplumber.conf.d
cat > ~/.config/wireplumber/wireplumber.conf.d/50-bluez-roles.conf << 'EOF'
monitor.bluez.properties = {
  bluez5.roles = [ a2dp_source ]
}
EOF
systemctl --user restart wireplumber
bluetoothctl disconnect B8:5F:98:E4:FD:B1
bluetoothctl connect B8:5F:98:E4:FD:B1
```

Check it: `wpctl status` should list **Echo Studio** under Sinks (not "Dummy Output"), and `pactl list cards | grep "Active Profile"` should show `a2dp-sink` for the Bluetooth card.

**Keep the stream open between plays.** By default PipeWire releases the Bluetooth audio stream a few seconds after a song ends, and the Echo announces "Playing from PiClock" every time the next song re-opens it. Disabling suspend on the Echo sink keeps the (silent) stream open, so the Echo stays quiet:

```bash
cat > ~/.config/wireplumber/wireplumber.conf.d/51-bluez-no-suspend.conf << 'EOF'
monitor.bluez.rules = [
  {
    matches = [ { node.name = "~bluez_output.*" } ]
    actions = { update-props = { session.suspend-timeout-seconds = 0 } }
  }
]
EOF
systemctl --user restart wireplumber
```

If Alexa ever complains that Bluetooth is in use, delete that file and restart WirePlumber to go back to the default.

Copy the MP3s into the app folder. They are not in the repo (copyrighted) and `deploy.ps1` never touches them, so keep a copy somewhere safe:

```bash
mkdir -p ~/PiClock/assets
# scp your MP3s to ~/PiClock/assets/  (the song(s) plus the "come back soon" clip)
```

`play-audio.sh` (in the repo, deployed next to the binary) wraps mpv because the app runs from a system service with no user environment; without `XDG_RUNTIME_DIR` mpv cannot find the user's PipeWire socket. Test from a shell (Ctrl+C to stop):

```bash
~/PiClock/play-audio.sh ~/PiClock/assets/wheels-on-the-bus.mp3
```

Before each play the app runs `bluetoothctl connect` so an idle-disconnected Echo comes back on its own; nothing needs to be reconnected by hand.

#### 7. Song library and nap settings

`assets/songs.json` (in the repo, deployed with the app) lists the songs and the play limit:

```jsonc
{
  "songs": [
    // start/end = seconds into the file to play from/to (end trims a silent tail)
    // fadeIn/fadeOut = seconds of ramp at each end
    { "file": "wheels-on-the-bus.mp3", "start": 6.5, "fadeIn": 1.5, "end": 86, "fadeOut": 1 },
    { "file": "old-macdonald.mp3", "gain": -3 },  // gain = dB, optional
    // Spotify song: Share > Copy Song Link. Plays on a long press; see step 9.
    { "spotify": "https://open.spotify.com/track/3bblDd2pNGaTSWGOT6lf3y", "animation": "rainbow" }
  ],
  "spotify": { "soloist": "ws://127.0.0.1:9090", "volume": 0 },
  "limit": {
    "plays": 3,               // this many plays...
    "spanMinutes": 5,         // ...all starting within this window...
    "cooldownMinutes": 10,    // ...and the bus naps for this long
    "comeBackLater": "come-back-soon.mp3",
    "comeBackLaterGain": 0,   // dB adjustment for the clip
    "clipEverySeconds": 20,   // don't replay the clip faster than this
    "tapDebounceSeconds": 2   // a tap counts only after this many quiet seconds
  }
}
```

`gain` is applied by mpv with no peak limiting, so it is for small corrections. A clip that is far too quiet (a text-to-speech export, say) is better normalized once with ffmpeg, keeping the original:

```bash
cd ~/PiClock/assets
cp come-back-soon.mp3 come-back-soon.orig.mp3
ffmpeg -y -i come-back-soon.orig.mp3 -af "loudnorm=I=-12:TP=-1.0:LRA=7" -ar 44100 -b:a 128k come-back-soon.mp3
```

Taps are debounced: every tap resets a timer and only a tap that follows `tapDebounceSeconds` of quiet is acted on. A flurry of taps starts the bus once and cannot dismiss it.

`animation` picks the scene for a song: `bus` (the default) or `rainbow`. A tap picks from the local songs, a long press from the Spotify ones.

Each tap picks a random song (never the same one twice in a row) and the bus animation adapts to the trimmed length (`end`, or the file length, minus `start`), so the bus's exit drive lands when the music actually stops. To find a silent tail: `ffmpeg -i song.mp3 -af silencedetect=n=-40dB:d=0.5 -f null -`. Songs listed but missing on the Pi are skipped with a note on stderr (`journalctl -u piclock`). If the file is absent, every MP3 in `assets/` is a song with no offset. The fade is done by mpv's `afade` filter; note that it runs on the file's own clock, so the app starts the fade at the offset rather than at zero.

#### 8. Hue lights (optional)

With a Philips Hue Bridge (Pro or the older square bridge) on the same network, the lights in one room or zone turn school-bus yellow when the bus arrives, shimmer through shades of yellow while the song plays, and go back to exactly how they were (on/off, brightness, colour or white temperature) when it leaves. Without `assets/hue.json` nothing changes.

Pair once on the Pi. Find the bridge's IP in the Hue app (Settings > Bridges > ⓘ), run this, and press the round button on the bridge within 60 seconds:

```bash
sudo systemctl stop piclock
~/PiClock/PiClock --hue-pair 192.168.7.50 "Nursery"
sudo systemctl start piclock
```

It lists the bridge's rooms and zones and writes `~/PiClock/assets/hue.json`. That file holds the bridge key, so it is not in the repo and `deploy.ps1` never overwrites it. Leave the room name off to see the list first, then edit the file:

```jsonc
{
  "bridge": "192.168.7.50",
  "appKey": "…",               // from pairing
  "room": "Nursery",           // Hue room or zone name (case doesn't matter)
  "color": "#FFC20E",          // while the bus is on screen
  "brightness": 80,            // 1-100 %
  "transitionSeconds": 1.5,    // fade in both directions
  // Optional: while the song plays, each colour bulb drifts to a different shade and
  // brightness every few seconds. These are the defaults; "danceSeconds": 0 holds "color".
  "danceColors": ["#FFC20E", "#FFD700", "#FFB300", "#FFE45C", "#FFA000", "#FFF06A"],
  "danceSeconds": 2,
  "danceBrightnessMin": 45     // dance brightness is between this and "brightness"
}
```

White-only bulbs just change brightness. Any changes made to the lights while a song plays are overwritten when the bus leaves. Errors (bridge unreachable, room not found) go to `journalctl -u piclock`; the clock carries on without the lights.

#### 9. Spotify songs (optional)

Spotify songs are played by **Spotify Soloist**, Spotify's headless Connect player. It streams into PipeWire like mpv does, so the music reaches the Echo over the same Bluetooth link, and PiClock drives it over Soloist's local WebSocket API (play this track, start here, pause). PiClock holds no Spotify secrets. Needs Spotify Premium.

1. In the [Spotify for Developers dashboard](https://developer.spotify.com/dashboard), open **Spotify Soloist API Key**, accept the terms and generate a key. Keep it private.
2. Download the **arm64** build from Soloist's [downloads page](https://developer.spotify.com/documentation/soloist/reference/downloads-and-updates) (Spotify asks that the binary not be redistributed, so it is not in this repo) and put it on the Pi:

```bash
mkdir -p ~/soloist && tar -xzf soloist_release_arm64.tar.gz -C ~/soloist
~/soloist/soloist --version
```

3. Keep the key in a file only you can read. This asks for it without echoing it, so it stays out of the shell history:

```bash
mkdir -p ~/.config/soloist && chmod 700 ~/.config/soloist
install -m 600 /dev/null ~/.config/soloist/env
read -rs -p "Soloist API key: " k && printf 'SOLOIST_API_KEY=%s\n' "$k" > ~/.config/soloist/env && unset k && echo
```

4. Run it as a *user* service, so it shares the PipeWire session with the Bluetooth sink:

```bash
mkdir -p ~/.config/systemd/user
cat > ~/.config/systemd/user/soloist.service << 'EOF'
[Unit]
Description=Spotify Soloist (Spotify Connect player for PiClock)
After=pipewire.service wireplumber.service

[Service]
EnvironmentFile=%h/.config/soloist/env
ExecStart=%h/soloist/soloist --device-name PiClock --api-key ${SOLOIST_API_KEY} --ws 127.0.0.1:9090
Restart=on-failure
RestartSec=10
# Exit code 10 = this build has expired; download a newer one
RestartPreventExitStatus=10

[Install]
WantedBy=default.target
EOF
systemctl --user daemon-reload
systemctl --user enable --now soloist
sudo loginctl enable-linger $USER     # start it at boot, before anyone logs in
```

5. Pair it once: on a phone on the same Wi-Fi, open Spotify, tap the devices icon and pick **PiClock**. Soloist stores the login in `~/.local/share/soloist` and restores it on every restart.

Check it with `journalctl --user -u soloist -f`. If PiClock cannot reach Soloist or it is not paired, the scene still plays and the reason goes to `journalctl -u piclock`.

**Soloist builds expire 90 days after they are built.** When the service stops with exit code 10, download the new arm64 build, replace `~/soloist/soloist` and `systemctl --user restart soloist`.

Notes:
- `start`/`end` work for Spotify songs; `fadeIn`, `fadeOut` and `gain` do not. `"volume"` in the `spotify` section (1-100) sets Soloist's volume before each play.
- Without `end`, the scene's length comes from Soloist once the track loads (the scene starts assuming 3 minutes and adjusts).
- A play starts from the song's beginning and then jumps to `start`, so a fraction of a second of the intro can be heard.
- Spotify playing on the Echo *itself* (through Alexa) still takes the Echo off Bluetooth; Soloist avoids that by playing through the Pi.

#### 10. Publish and copy (manual alternative to deploy.ps1)


```bash
# On Windows
dotnet publish -c Release -r linux-arm64 --self-contained true
scp -r bin/Release/net8.0/linux-arm64/publish/* user@pi-ip:~/PiClock/

# On the Pi
chmod +x ~/PiClock/PiClock
~/PiClock/PiClock --kiosk
```

## Project Structure

```
PiClock/
├── Controls/
│   ├── AnalogClock.cs           # Analog clock face with diamond hands
│   ├── SevenSegmentDigit.cs     # Seven-segment digit renderer (0-9)
│   ├── SixteenSegmentChar.cs    # Sixteen-segment character renderer (A-Z, 0-9)
│   ├── ColonDisplay.cs          # Blinking colon between HH and MM
│   ├── RunningMouse.cs          # Animated mouse with personality
│   ├── SongScene.cs             # Base for song scenes: play/stop, song length, shared sky/sun/clouds/grass
│   ├── WheelsOnTheBus.cs        # Bus scene, timing derived from the song length
│   ├── ILoveARainbow.cs         # Rainbow scene: paints in, shimmers, wipes away at the song's end
│   └── NapCountdown.cs          # Sleeping bus + shrinking bar shown during the nap
├── Audio/
│   ├── SongLibrary.cs           # songs.json loader, random pick without repeats
│   ├── SpotifyPlayer.cs         # Spotify songs via Spotify Soloist's local WebSocket API
│   └── PlayLimiter.cs           # "3 plays in 5 minutes → 10 minute nap" rule
├── Lights/
│   ├── HueLights.cs             # Hue CLIP v2: room goes bus-yellow, then restored
│   └── HuePairing.cs            # `PiClock --hue-pair <ip> "Room"` → assets/hue.json
├── ViewModels/
│   ├── ViewModelBase.cs         # INotifyPropertyChanged base
│   └── ClockViewModel.cs        # Clock logic, mode toggle, 500ms timer
├── Views/
│   ├── MainWindow.axaml         # Layout — analog/digital/calendar + bus overlay
│   └── MainWindow.axaml.cs      # Kiosk mode, tap/swipe handling, mpv + Bluetooth audio
├── App.axaml / App.axaml.cs     # Application entry, dark theme
├── Program.cs                   # Main entry point
├── assets/songs.json            # Song list (offset/fade per song) + nap settings
├── play-audio.sh                # mpv wrapper with the PipeWire environment
├── PiClock.csproj               # .NET 8 + Avalonia 11
└── deploy.ps1                   # One-command publish & deploy to Pi

On the Pi only (not in the repo):
~/PiClock/assets/*.mp3
~/PiClock/assets/hue.json        (optional, written by --hue-pair)
~/soloist/soloist                (optional, Spotify Soloist arm64 build)
~/.config/soloist/env            (Soloist API key, mode 600)
~/.config/systemd/user/soloist.service
~/.config/wireplumber/wireplumber.conf.d/50-bluez-roles.conf
~/.config/wireplumber/wireplumber.conf.d/51-bluez-no-suspend.conf
```

## Hardware

- Raspberry Pi 5 (with 5A USB-C power supply)
- WaveShare 8-DSI-TOUCH-A (8" 1280×800 capacitive touchscreen)
- Philips Hue Bridge Pro (optional) for the bus-coloured lights
- Amazon Echo Studio as the Bluetooth speaker (any A2DP speaker works; change the MAC in `MainWindow.axaml.cs`)
- Raspberry Pi OS (64-bit, Bookworm) with Wayland/labwc, PipeWire + WirePlumber

## License

MIT
