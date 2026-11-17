# Firma digital de StreamDelay.exe (Authenticode) con tu certificado de firma de código.
# No hay que instalar nada: usa lo que ya trae Windows.
#
# Uso:
#   1. Instala tu certificado de firma de código (p. ej. Certum con SimplySign: aparece solo en
#      "Certificados - Usuario actual > Personal" mientras SimplySign está conectado).
#   2. Ejecuta:  powershell -ExecutionPolicy Bypass -File firmar.ps1 -Elegir
#      Te enseña tus certificados de firma de código y guarda el elegido en firma.cfg.
#   3. A partir de ahí build.bat firma solo al compilar (si existe firma.cfg).
#
# Opciones:
#   -Exe <ruta>        archivo a firmar (por defecto StreamDelay.exe junto a este script)
#   -Elegir            elegir el certificado y guardarlo en firma.cfg
#   -Prueba            prueba sin certificado real: firma una COPIA con un certificado temporal
#                      (solo para ver que el proceso funciona; esa firma no es de confianza)
param([string]$Exe = "", [switch]$Elegir, [switch]$Prueba)

$ErrorActionPreference = 'Stop'
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
if ($Exe -eq "") { $Exe = Join-Path $dir "StreamDelay.exe" }
$cfg = Join-Path $dir "firma.cfg"
# Servidores de sello de tiempo: la firma sigue siendo válida aunque el certificado caduque después.
$timestamps = @("http://time.certum.pl", "http://timestamp.digicert.com", "http://timestamp.sectigo.com")

function Certificados {
    @(Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Where-Object { $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) })
}

function Firmar($archivo, $cert) {
    foreach ($ts in $timestamps) {
        try {
            $r = Set-AuthenticodeSignature -FilePath $archivo -Certificate $cert -HashAlgorithm SHA256 -TimestampServer $ts -IncludeChain NotRoot
            if ($r.Status -eq 'Valid' -or $Prueba) { return $r }
            Write-Host "  Sello de tiempo $ts`: $($r.StatusMessage)"
        } catch { Write-Host "  Sello de tiempo $ts no responde: $($_.Exception.Message)" }
    }
    # Sin sello de tiempo (sin internet): la firma vale hasta que caduque el certificado.
    Write-Host "  Firmando sin sello de tiempo."
    return Set-AuthenticodeSignature -FilePath $archivo -Certificate $cert -HashAlgorithm SHA256 -IncludeChain NotRoot
}

if ($Prueba) {
    # Certificado temporal en memoria (no se instala en Windows ni se guarda en ningún sitio).
    $rsa = [System.Security.Cryptography.RSA]::Create(2048)
    $req = New-Object System.Security.Cryptography.X509Certificates.CertificateRequest("CN=StreamDelay PRUEBA", $rsa, [System.Security.Cryptography.HashAlgorithmName]::SHA256, [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $oid = New-Object System.Security.Cryptography.OidCollection
    [void]$oid.Add((New-Object System.Security.Cryptography.Oid "1.3.6.1.5.5.7.3.3")) # firma de código
    $req.CertificateExtensions.Add((New-Object System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension($oid, $false)))
    $tmp = $req.CreateSelfSigned((Get-Date).AddDays(-1), (Get-Date).AddDays(1))
    $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($tmp.Export('Pfx', 'x'), 'x', 'Exportable')
    $copia = Join-Path ([IO.Path]::GetTempPath()) ("StreamDelay-prueba-firma-" + [guid]::NewGuid().ToString('N').Substring(0, 8) + ".exe")
    Copy-Item $Exe $copia
    $r = Firmar $copia $cert
    $s = Get-AuthenticodeSignature $copia
    Write-Host "Prueba de firma sobre una copia: $copia"
    Write-Host "  Firmado por: $($s.SignerCertificate.Subject)"
    Write-Host "  Estado: $($s.Status) (con un certificado de prueba lo normal es 'UnknownError': no es de confianza)"
    Write-Host "  Sello de tiempo: $(if ($s.TimeStamperCertificate) { $s.TimeStamperCertificate.Subject } else { 'ninguno' })"
    Remove-Item $copia -Force
    exit 0
}

if ($Elegir) {
    $lista = Certificados
    if ($lista.Count -eq 0) {
        Write-Host "No hay ningún certificado de firma de código instalado para tu usuario."
        Write-Host "Consigue uno (ver FIRMA.txt) y, si es de Certum, abre SimplySign Desktop y conéctate."
        exit 1
    }
    for ($i = 0; $i -lt $lista.Count; $i++) {
        Write-Host ("[{0}] {1}  (caduca {2:dd/MM/yyyy}, emisor {3})" -f ($i + 1), $lista[$i].Subject, $lista[$i].NotAfter, $lista[$i].Issuer)
    }
    $n = Read-Host "Número del certificado"
    $c = $lista[[int]$n - 1]
    "huella=" + $c.Thumbprint | Set-Content -Encoding ASCII $cfg
    Write-Host "Guardado en firma.cfg. A partir de ahora build.bat firmará StreamDelay.exe al compilar."
    exit 0
}

# Firma normal con el certificado guardado en firma.cfg.
if (-not (Test-Path $cfg)) { Write-Host "Falta firma.cfg: ejecuta primero  firmar.ps1 -Elegir"; exit 1 }
$huella = ((Get-Content $cfg) -match '^huella=' | Select-Object -First 1) -replace '^huella=', ''
$cert = Certificados | Where-Object { $_.Thumbprint -eq $huella.Trim() } | Select-Object -First 1
if (-not $cert) {
    Write-Host "No encuentro el certificado de firma.cfg ($huella). ¿Caducó, o SimplySign no está conectado?"
    exit 1
}
if (-not (Test-Path $Exe)) { Write-Host "No existe $Exe"; exit 1 }
$r = Firmar $Exe $cert
$s = Get-AuthenticodeSignature $Exe
if ($s.Status -eq 'Valid') {
    Write-Host "Firmado: $(Split-Path $Exe -Leaf) por $($s.SignerCertificate.Subject)$(if ($s.TimeStamperCertificate) { ', con sello de tiempo' })."
    exit 0
}
Write-Host "La firma no es válida: $($s.StatusMessage)"
exit 1
