# Crea el ZIP de descarga en dist\: VortexsLive.exe, la guía, el LEEME, la licencia y las licencias
# de las fuentes (nunca VortexsLive.ini). Lo usa GitHub al publicar una versión y sirve igual en tu PC:
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
$exe = Join-Path $dir "VortexsLive.exe"
if (-not (Test-Path $exe)) { throw "Falta VortexsLive.exe: compila primero con build.bat" }

$dist = Join-Path $dir "dist"
$carpeta = Join-Path $dist "VortexsLive-$Version"
$zip = Join-Path $dist "VortexsLive-$Version.zip"
if (Test-Path $carpeta) { Remove-Item -Recurse -Force $carpeta }
if (Test-Path $zip) { Remove-Item -Force $zip }
New-Item -ItemType Directory -Force (Join-Path $carpeta "licencias-fuentes") | Out-Null

Copy-Item $exe, (Join-Path $dir "GUIA.html"), (Join-Path $dir "LEEME.txt"), (Join-Path $dir "LICENSE") $carpeta
Copy-Item (Join-Path $dir "res\OFL-*.txt") (Join-Path $carpeta "licencias-fuentes")
Compress-Archive -Path (Join-Path $carpeta "*") -DestinationPath $zip

# Copia para la actualización automática de las versiones 3.x (se llamaban StreamDelay): solo buscan
# un ZIP que empiece por "StreamDelay" con StreamDelay.exe dentro. Es el mismo programa.
$compat = Join-Path $dist "StreamDelay-$Version-actualizacion.zip"
if (Test-Path $compat) { Remove-Item -Force $compat }
Rename-Item (Join-Path $carpeta "VortexsLive.exe") "StreamDelay.exe"
Compress-Archive -Path (Join-Path $carpeta "*") -DestinationPath $compat
Rename-Item (Join-Path $carpeta "StreamDelay.exe") "VortexsLive.exe"
Write-Host "Listo: dist\VortexsLive-$Version.zip ($([math]::Round((Get-Item $zip).Length / 1KB)) KB)"
