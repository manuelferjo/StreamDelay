# Crea el ZIP de descarga en dist\: StreamDelay.exe, la guía, el LEEME, la licencia y las licencias
# de las fuentes (nunca StreamDelay.ini). Lo usa GitHub al publicar una versión y sirve igual en tu PC:
#   powershell -ExecutionPolicy Bypass -File empaquetar.ps1
# Antes hay que compilar con build.bat. La versión se lee de src\MainForm.cs (const string Version).
param([string]$Version = "")

$ErrorActionPreference = 'Stop'
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
if ($Version -eq "") {
    $m = Select-String -Path (Join-Path $dir "src\MainForm.cs") -Pattern 'const string Version = "([^"]+)"' | Select-Object -First 1
    if (-not $m) { throw "No encuentro la versión en src\MainForm.cs" }
    $Version = $m.Matches[0].Groups[1].Value
}
$exe = Join-Path $dir "StreamDelay.exe"
if (-not (Test-Path $exe)) { throw "Falta StreamDelay.exe: compila primero con build.bat" }

$dist = Join-Path $dir "dist"
$carpeta = Join-Path $dist "StreamDelay-$Version"
$zip = Join-Path $dist "StreamDelay-$Version.zip"
if (Test-Path $carpeta) { Remove-Item -Recurse -Force $carpeta }
if (Test-Path $zip) { Remove-Item -Force $zip }
New-Item -ItemType Directory -Force (Join-Path $carpeta "licencias-fuentes") | Out-Null

Copy-Item $exe, (Join-Path $dir "GUIA.html"), (Join-Path $dir "LEEME.txt"), (Join-Path $dir "LICENSE") $carpeta
Copy-Item (Join-Path $dir "res\OFL-*.txt") (Join-Path $carpeta "licencias-fuentes")
Compress-Archive -Path (Join-Path $carpeta "*") -DestinationPath $zip
Write-Host "Listo: dist\StreamDelay-$Version.zip ($([math]::Round((Get-Item $zip).Length / 1KB)) KB)"
