# Hart Werkplek bijwerken: haalt de nieuwste versie van GitHub en installeert die.
param([int]$WachtOp = 0)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$Host.UI.RawUI.WindowTitle = 'Hart Werkplek bijwerken'

$doel = Join-Path $env:LOCALAPPDATA 'HartWerkplek'
$exe  = Join-Path $doel 'HartWerkplek.exe'

function Stop-Met($t) {
    Write-Host ""
    Write-Host "Bijwerken is niet gelukt: $t" -ForegroundColor Red
    Write-Host "Je oude versie blijft gewoon werken."
    Read-Host "Druk op Enter om te sluiten"
    if (Test-Path $exe) { Start-Process $exe }
    exit 1
}

Write-Host "Hart Werkplek wordt bijgewerkt..."
if ($WachtOp -gt 0) {
    try { Wait-Process -Id $WachtOp -Timeout 30 -ErrorAction SilentlyContinue } catch { }
}

$tmp = Join-Path $env:TEMP ('hartwerkplek_bijwerken_' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
$zip = Join-Path $tmp 'nieuw.zip'
try {
    Invoke-WebRequest -UseBasicParsing -Uri 'https://github.com/boddie91/hart-werkplek/archive/refs/heads/main.zip' -OutFile $zip
} catch { Stop-Met "downloaden van GitHub lukte niet. ($($_.Exception.Message))" }

Expand-Archive -Path $zip -DestinationPath (Join-Path $tmp 'uit') -Force
$map = Get-ChildItem (Join-Path $tmp 'uit') -Directory | Select-Object -First 1
if (-not $map -or -not (Test-Path (Join-Path $map.FullName 'installeer.ps1'))) { Stop-Met "het pakket van GitHub is niet compleet." }

& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $map.FullName 'installeer.ps1')
$code = $LASTEXITCODE
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
if ($code -ne 0) { Stop-Met "het installeren gaf een fout (zie hierboven)." }

Write-Host ""
Write-Host "Bijgewerkt! Hart Werkplek start opnieuw op." -ForegroundColor Green
Start-Sleep -Seconds 2
Start-Process $exe
