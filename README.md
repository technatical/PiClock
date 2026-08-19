# PiClock 🕐🐭

A clock for the **Raspberry Pi 5** with a **WaveShare 8-DSI-TOUCH-A** display (1280×800), built with [Avalonia UI](https://avaloniaui.net/) and .NET 8.

Two display modes — tap the touchscreen to toggle:

- **Analog** — clean white clock face with diamond hands, dimmed dial, bold blue day/date text
- **Digital** — seven-segment time digits with sixteen-segment day-of-week and seven-segment date

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
| Click/tap | Toggle analog ↔ digital |

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

#### 6. Publish and copy (manual alternative to deploy.ps1)

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
│   └── RunningMouse.cs          # Animated mouse with personality
├── ViewModels/
│   ├── ViewModelBase.cs         # INotifyPropertyChanged base
│   └── ClockViewModel.cs        # Clock logic, mode toggle, 500ms timer
├── Views/
│   ├── MainWindow.axaml         # Layout — analog/digital + day/date panels
│   └── MainWindow.axaml.cs      # Kiosk mode, fullscreen toggle, tap handler
├── App.axaml / App.axaml.cs     # Application entry, dark theme
├── Program.cs                   # Main entry point
├── PiClock.csproj               # .NET 8 + Avalonia 11
└── deploy.ps1                   # One-command publish & deploy to Pi
```

## Hardware

- Raspberry Pi 5 (with 5A USB-C power supply)
- WaveShare 8-DSI-TOUCH-A (8" 1280×800 capacitive touchscreen)
- Raspberry Pi OS (64-bit, Bookworm) with Wayland/labwc

## License

MIT
