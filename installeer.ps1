# Hart Werkplek - installatie voor alleen jouw gebruikersaccount (geen beheerdersrechten nodig)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Stap($t) { Write-Host ""; Write-Host ">> $t" -ForegroundColor Magenta }
function Fout($t) { Write-Host ""; Write-Host "Er ging iets mis: $t" -ForegroundColor Red; exit 1 }

$bron = Join-Path $PSScriptRoot 'bron'
$doel = Join-Path $env:LOCALAPPDATA 'HartWerkplek'

Write-Host "Hart Werkplek wordt geinstalleerd in: $doel"

# 1. Bestanden klaarzetten
Stap "Bestanden kopieren"
New-Item -ItemType Directory -Force -Path $doel | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $doel 'web') | Out-Null
Copy-Item (Join-Path $bron 'Werkplek.cs') $doel -Force
Copy-Item (Join-Path $bron 'web\*') (Join-Path $doel 'web') -Force -Recurse
Copy-Item (Join-Path $bron 'versie.txt') $doel -Force
Copy-Item (Join-Path $bron 'hartwerkplek.ico') $doel -Force
Copy-Item (Join-Path $PSScriptRoot 'bijwerken.ps1') $doel -Force
$cfg = Join-Path $doel 'instellingen.txt'
if (Test-Path $cfg) {
    Write-Host "   Bestaande instellingen blijven bewaard."
} else {
    Copy-Item (Join-Path $bron 'instellingen.txt') $cfg
}

# 2. Controleren of de ingebouwde browser van Windows (WebView2) aanwezig is
Stap "Ingebouwde browser (Microsoft Edge WebView2) controleren"
$wvKey = '{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}'
$wv = @(
  "HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\$wvKey",
  "HKLM:\SOFTWARE\Microsoft\EdgeUpdate\Clients\$wvKey",
  "HKCU:\Software\Microsoft\EdgeUpdate\Clients\$wvKey"
) | Where-Object { Test-Path $_ }
if ($wv) { Write-Host "   Aanwezig." }
else { Write-Host "   Niet gevonden. Op Windows 11 hoort die er standaard te zijn; we proberen het toch." -ForegroundColor Yellow }

# 3. Onderdelen van de ingebouwde browser ophalen (officieel pakket van Microsoft via nuget.org)
$heeftDlls = (Test-Path (Join-Path $doel 'Microsoft.Web.WebView2.WinForms.dll')) -and (Test-Path (Join-Path $doel 'WebView2Loader.dll'))
if ($heeftDlls) { Stap "Onderdelen van de ingebouwde browser zijn er al" } else {
Stap "Onderdelen van de ingebouwde browser downloaden"
$tmp = Join-Path $env:TEMP ('hartwerkplek_' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
$zip = Join-Path $tmp 'webview2.zip'
try {
    Invoke-WebRequest -UseBasicParsing -Uri 'https://www.nuget.org/api/v2/package/Microsoft.Web.WebView2' -OutFile $zip
} catch { Fout "downloaden lukte niet. Controleer of je internet hebt. ($($_.Exception.Message))" }
Expand-Archive -Path $zip -DestinationPath (Join-Path $tmp 'pkg') -Force
$pkg = Join-Path $tmp 'pkg'

$lib = @('lib\net462','lib\net45') | ForEach-Object { Join-Path $pkg $_ } | Where-Object { Test-Path (Join-Path $_ 'Microsoft.Web.WebView2.WinForms.dll') } | Select-Object -First 1
if (-not $lib) { Fout "de juiste onderdelen zaten niet in het pakket." }
Copy-Item (Join-Path $lib 'Microsoft.Web.WebView2.Core.dll') $doel -Force
Copy-Item (Join-Path $lib 'Microsoft.Web.WebView2.WinForms.dll') $doel -Force
$loader = Join-Path $pkg 'runtimes\win-x64\native\WebView2Loader.dll'
if (-not (Test-Path $loader)) { Fout "WebView2Loader.dll niet gevonden in het pakket." }
Copy-Item $loader $doel -Force
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}

# 4. Het programma bouwen met de compiler die standaard in Windows zit
Stap "Programma bouwen"
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { Fout "de Windows-compiler (csc.exe) is niet gevonden." }
$exe = Join-Path $doel 'HartWerkplek.exe'
$out = & $csc /nologo /target:winexe /platform:x64 /codepage:65001 "/win32icon:$(Join-Path $doel 'hartwerkplek.ico')" /optimize+ "/out:$exe" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll `
    "/r:$(Join-Path $doel 'Microsoft.Web.WebView2.Core.dll')" `
    "/r:$(Join-Path $doel 'Microsoft.Web.WebView2.WinForms.dll')" `
    (Join-Path $doel 'Werkplek.cs') 2>&1
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $exe)) {
    $log = Join-Path $doel 'bouwfout.txt'
    $out | Out-File $log -Encoding UTF8
    Write-Host ($out -join "`n")
    Fout "het bouwen is mislukt. De foutmelding staat in $log - stuur die naar Claude."
}
Write-Host "   Gelukt."

# 5. Snelkoppeling op het bureaublad
Stap "Snelkoppeling op het bureaublad maken"
$desk = [Environment]::GetFolderPath('Desktop')
$sh = New-Object -ComObject WScript.Shell
$lnk = $sh.CreateShortcut((Join-Path $desk 'Hart Werkplek.lnk'))
$lnk.TargetPath = $exe
$lnk.WorkingDirectory = $doel
$lnk.Description = 'Hart Werkplek'
$lnk.IconLocation = (Join-Path $doel 'hartwerkplek.ico') + ',0'
$lnk.Save()

Write-Host ""
Write-Host "Klaar! Start Hart Werkplek via de snelkoppeling op je bureaublad." -ForegroundColor Green
Write-Host "Eerste keer: klik links op Instellingen en vul het adres van James en je bestanden in."
