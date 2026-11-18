# Crea claves de licencia "SD1-…" de StreamDelay (para regalar o vender a mano).
# Hace falta la clave privada (clave-privada-StreamDelay.txt), que NO está en GitHub: guárdala tú.
#
# Ejemplos (clic derecho en la carpeta → Abrir en Terminal):
#   powershell -ExecutionPolicy Bypass -File crear-clave.ps1 -Nombre "Pepe"
#       una clave del plan Completo para siempre, a nombre de Pepe
#   powershell -ExecutionPolicy Bypass -File crear-clave.ps1 -Nombre "Sorteo" -Cantidad 5 -Plan Basico -Dias 30
#       5 claves del plan Básico que caducan dentro de 30 días
#
# Cada clave creada se apunta en claves-creadas.csv (junto a la clave privada) con su número:
# si una se filtra, ese número se añade a Revoked en src/License.cs y deja de valer.
param(
  [string]$Nombre = "",
  [ValidateSet("Completo", "Basico", "Básico")][string]$Plan = "Completo",
  [int]$Dias = 0,            # 0 = para siempre
  [string]$Hasta = "",       # o una fecha concreta: 2027-06-30
  [int]$Cantidad = 1,
  [string]$Clave = ""        # dónde está la clave privada (por defecto, junto a este archivo)
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Numerics

if (-not $Clave) { $Clave = Join-Path $PSScriptRoot "clave-privada-StreamDelay.txt" }
if (-not (Test-Path $Clave)) { throw "No encuentro la clave privada: $Clave (ponla junto a este archivo o usa -Clave ruta)" }
$n = $null; $d = $null
foreach ($l in Get-Content $Clave) {
  if ($l -match '^n=([0-9a-fA-F]+)$') { $n = $Matches[1] }
  if ($l -match '^d=([0-9a-fA-F]+)$') { $d = $Matches[1] }
}
if (-not $n -or -not $d) { throw "La clave privada está mal copiada (faltan n= o d=)." }

function HexToBig([string]$h) { [System.Numerics.BigInteger]::Parse("0" + $h, [Globalization.NumberStyles]::HexNumber) }
function ToBE([System.Numerics.BigInteger]$v, [int]$len) {
  $le = $v.ToByteArray(); $be = New-Object byte[] $len
  for ($i = 0; $i -lt $le.Length -and $i -lt $len; $i++) { $be[$len - 1 - $i] = $le[$i] }
  return ,$be
}
function FromBE([byte[]]$be) {
  $le = New-Object byte[] ($be.Length + 1)
  for ($i = 0; $i -lt $be.Length; $i++) { $le[$i] = $be[$be.Length - 1 - $i] }
  return New-Object System.Numerics.BigInteger (,$le)
}
$N = HexToBig $n; $D = HexToBig $d

$planByte = if ($Plan -eq "Completo") { 2 } else { 1 }
$epoch = [DateTime]::new(2026, 1, 1, 0, 0, 0, [DateTimeKind]::Utc)
$expDays = 0
if ($Hasta) { $expDays = [int][Math]::Ceiling(([DateTime]::Parse($Hasta, [Globalization.CultureInfo]::InvariantCulture).ToUniversalTime() - $epoch).TotalDays) }
elseif ($Dias -gt 0) { $expDays = [int][Math]::Ceiling(([DateTime]::UtcNow.AddDays($Dias) - $epoch).TotalDays) }
if ($expDays -lt 0 -or $expDays -gt 65535) { throw "Fecha de caducidad fuera de rango." }

$nameBytes = [Text.Encoding]::UTF8.GetBytes($Nombre)
if ($nameBytes.Length -gt 40) { throw "El nombre es demasiado largo (máximo 40 letras)." }

$info = [byte[]](0x30,0x31,0x30,0x0d,0x06,0x09,0x60,0x86,0x48,0x01,0x65,0x03,0x04,0x02,0x01,0x05,0x00,0x04,0x20)
$domain = [Text.Encoding]::ASCII.GetBytes("StreamDelay licencia 1`n")
$rng = [Security.Cryptography.RandomNumberGenerator]::Create()
$log = Join-Path (Split-Path $Clave) "claves-creadas.csv"
if (-not (Test-Path $log)) { "numero;plan;nombre;caduca;creada;clave" | Out-File $log -Encoding UTF8 }

for ($c = 0; $c -lt $Cantidad; $c++) {
  $serialBytes = New-Object byte[] 4; $rng.GetBytes($serialBytes)
  $payload = New-Object byte[] (8 + $nameBytes.Length)
  $payload[0] = 1; $payload[1] = $planByte
  $payload[2] = ($expDays -shr 8) -band 0xFF; $payload[3] = $expDays -band 0xFF
  [Array]::Copy($serialBytes, 0, $payload, 4, 4)
  [Array]::Copy($nameBytes, 0, $payload, 8, $nameBytes.Length)

  $msg = New-Object byte[] ($domain.Length + $payload.Length)
  [Array]::Copy($domain, 0, $msg, 0, $domain.Length); [Array]::Copy($payload, 0, $msg, $domain.Length, $payload.Length)
  $hash = [Security.Cryptography.SHA256]::Create().ComputeHash($msg)

  # EMSA-PKCS1-v1_5: 00 01 FF…FF 00 DigestInfo hash (128 bytes)
  $em = New-Object byte[] 128
  $t = $info.Length + $hash.Length
  $em[0] = 0; $em[1] = 1
  for ($i = 2; $i -lt 128 - $t - 1; $i++) { $em[$i] = 0xFF }
  $em[128 - $t - 1] = 0
  [Array]::Copy($info, 0, $em, 128 - $t, $info.Length)
  [Array]::Copy($hash, 0, $em, 128 - $hash.Length, $hash.Length)
  $sig = ToBE ([System.Numerics.BigInteger]::ModPow((FromBE $em), $D, $N)) 128

  $all = New-Object byte[] ($payload.Length + 128)
  [Array]::Copy($payload, 0, $all, 0, $payload.Length); [Array]::Copy($sig, 0, $all, $payload.Length, 128)
  $key = "SD1-" + [Convert]::ToBase64String($all).TrimEnd('=').Replace('+', '-').Replace('/', '_')

  $serial = ([uint32]$serialBytes[0] -shl 24) -bor ([uint32]$serialBytes[1] -shl 16) -bor ([uint32]$serialBytes[2] -shl 8) -bor [uint32]$serialBytes[3]
  $exp = if ($expDays -eq 0) { "nunca" } else { $epoch.AddDays($expDays).ToString("yyyy-MM-dd") }
  "$serial;$Plan;$Nombre;$exp;$([DateTime]::Now.ToString('yyyy-MM-dd HH:mm'));$key" | Out-File $log -Append -Encoding UTF8
  Write-Output $key
}
