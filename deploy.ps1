## Deploy PiClock to Raspberry Pi
## Usage: .\deploy.ps1 [-Pi <ip>] [-User <username>]

param(
    [string]$Pi   = "192.168.7.113",
    [string]$User = "mgibson"
)

$target = "${User}@${Pi}"
$remotePath = "~/PiClock"

Write-Host "`n=== Publishing for linux-arm64 ===" -ForegroundColor Cyan
dotnet publish -c Release -r linux-arm64 --self-contained true -o publish
if ($LASTEXITCODE -ne 0) { Write-Host "Build failed!" -ForegroundColor Red; exit 1 }

Write-Host "`n=== Stopping PiClock service ===" -ForegroundColor Cyan
ssh $target "sudo systemctl stop piclock 2>/dev/null; echo 'stopped'"

Write-Host "`n=== Copying files to Pi ===" -ForegroundColor Cyan
scp -r publish/* "${target}:${remotePath}/"
if ($LASTEXITCODE -ne 0) { Write-Host "Copy failed!" -ForegroundColor Red; exit 1 }

Write-Host "`n=== Starting PiClock service ===" -ForegroundColor Cyan
ssh $target "sudo systemctl start piclock"

Write-Host "`n=== Deployed! ===" -ForegroundColor Green
