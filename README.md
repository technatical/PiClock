# PiClock 🕐🐭

A clock for the **Raspberry Pi 5** with a **WaveShare 8-DSI-TOUCH-A** display (1280×800), built with [Avalonia UI](https://avaloniaui.net/) and .NET 8.

Three display modes (the default is the calendar):

- **Analog** — clean white clock face with capsule hands, dimmed dial, bold blue day/date text
- **Digital** — seven-segment time digits with sixteen-segment day-of-week and seven-segment date
- **Calendar** — month grid with today highlighted, analog clock alongside

**Toddler mode:** tapping the screen drives a bus in from the bottom and plays *Wheels on the Bus* through a Bluetooth speaker (an Echo Studio). The bus parks for the length of the song and drives off near the end. Tap again to send it away early. Swipe gestures (cycle modes, dismiss the bus) exist in the code but are commented out so small hands can't get lost.

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

Copy the song and the wrapper script into the app folder. Neither is in the repo (the MP3 is copyrighted, and `deploy.ps1` never touches them), so keep a copy somewhere safe:

```bash
mkdir -p ~/PiClock/assets
# scp your MP3 to ~/PiClock/assets/wheels-on-the-bus.mp3
cat > ~/PiClock/play-audio.sh << 'EOF'
#!/bin/bash
export XDG_RUNTIME_DIR=/run/user/1000
exec mpv --no-video --no-terminal "$1"
EOF
chmod +x ~/PiClock/play-audio.sh
```

The wrapper exists because the app runs from a system service with no user environment; without `XDG_RUNTIME_DIR` mpv cannot find the user's PipeWire socket. Test from a shell (Ctrl+C to stop):

```bash
~/PiClock/play-audio.sh ~/PiClock/assets/wheels-on-the-bus.mp3
```

Before each play the app runs `bluetoothctl connect` so an idle-disconnected Echo comes back on its own; nothing needs to be reconnected by hand.

#### 7. Publish and copy (manual alternative to deploy.ps1)

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
│   └── WheelsOnTheBus.cs        # Bus overlay, timing derived from the song length
├── ViewModels/
│   ├── ViewModelBase.cs         # INotifyPropertyChanged base
│   └── ClockViewModel.cs        # Clock logic, mode toggle, 500ms timer
├── Views/
│   ├── MainWindow.axaml         # Layout — analog/digital/calendar + bus overlay
│   └── MainWindow.axaml.cs      # Kiosk mode, tap/swipe handling, mpv + Bluetooth audio
├── App.axaml / App.axaml.cs     # Application entry, dark theme
├── Program.cs                   # Main entry point
├── PiClock.csproj               # .NET 8 + Avalonia 11
└── deploy.ps1                   # One-command publish & deploy to Pi

On the Pi only (not in the repo):
~/PiClock/assets/wheels-on-the-bus.mp3
~/PiClock/play-audio.sh
~/.config/wireplumber/wireplumber.conf.d/50-bluez-roles.conf
```

## Hardware

- Raspberry Pi 5 (with 5A USB-C power supply)
- WaveShare 8-DSI-TOUCH-A (8" 1280×800 capacitive touchscreen)
- Amazon Echo Studio as the Bluetooth speaker (any A2DP speaker works; change the MAC in `MainWindow.axaml.cs`)
- Raspberry Pi OS (64-bit, Bookworm) with Wayland/labwc, PipeWire + WirePlumber

## License

MIT
