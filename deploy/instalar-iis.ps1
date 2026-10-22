<#
.SYNOPSIS
  Instala HotelWeb en IIS: un sitio (la API sirve tambien la pagina web), permisos y regla de firewall.

.DESCRIPTION
  Se ejecuta EN EL SERVIDOR del hotel, como administrador, desde la carpeta del paquete (preparar-paquete.ps1).
  Es repetible: al actualizar no toca el appsettings.json del servidor (las conexiones a las bases).

  Primera instalacion: genera una clave de sesiones (Jwt:Key) aleatoria para este servidor y deja el sitio
  DETENIDO hasta que se configuren las conexiones en appsettings.json.

  Requisito: IIS con el ASP.NET Core Hosting Bundle 8.

.EXAMPLE
  .\instalar-iis.ps1
  .\instalar-iis.ps1 -Puerto 8098 -Destino D:\hotelweb
#>
[CmdletBinding()]
param(
    [string]$Destino = 'C:\inetpub\hotelweb',
    [int]$Puerto = 8098,
    [string]$Nombre = 'HotelWeb',
    [switch]$SinFirewall
)

$ErrorActionPreference = 'Stop'
# en Windows PowerShell 5.1 $PSScriptRoot llega vacio dentro de param()
$paquete = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$origen = Join-Path $paquete 'app'

# --- Comprobaciones ---------------------------------------------------------------------------------
$esAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $esAdmin) { throw 'Ejecute este script como administrador.' }
if (-not (Test-Path $origen)) { throw "No existe la carpeta del paquete: $origen" }

Import-Module WebAdministration
if (-not (Get-WebGlobalModule -Name AspNetCoreModuleV2 -ErrorAction SilentlyContinue)) {
    throw 'Falta el ASP.NET Core Hosting Bundle 8 (modulo AspNetCoreModuleV2 de IIS). Instalelo y ejecute este script de nuevo.'
}
$enUso = Get-NetTCPConnection -State Listen -LocalPort $Puerto -ErrorAction SilentlyContinue
if ($enUso -and -not (Get-Website -Name $Nombre -ErrorAction SilentlyContinue)) { throw "El puerto $Puerto ya esta en uso por otro sitio o programa." }

$config = Join-Path $Destino 'appsettings.json'
$primeraVez = -not (Test-Path $config)

function Detener {
    $s = Get-Website -Name $Nombre -ErrorAction SilentlyContinue
    if ($s -and $s.State -eq 'Started') { Stop-Website -Name $Nombre }
    if ((Test-Path "IIS:\AppPools\$Nombre") -and (Get-WebAppPoolState -Name $Nombre).Value -eq 'Started') { Stop-WebAppPool -Name $Nombre }
}

# --- Copiar ------------------------------------------------------------------------------------------
Detener
Start-Sleep -Seconds 2
New-Item -ItemType Directory -Force -Path $Destino | Out-Null
# /XF appsettings.json: una actualizacion no pisa la configuracion del servidor
robocopy $origen $Destino /MIR /XF appsettings.json /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "No se pudo copiar la aplicacion (robocopy codigo $LASTEXITCODE)." }

if ($primeraVez) {
    # la plantilla, con una clave de sesiones aleatoria propia de este servidor
    $bytes = New-Object byte[] 48
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $texto = [IO.File]::ReadAllText((Join-Path $origen 'appsettings.json'))
    $texto = $texto.Replace('<CAMBIAR: clave aleatoria de 32 caracteres o mas>', [Convert]::ToBase64String($bytes))
    [IO.File]::WriteAllText($config, $texto, (New-Object Text.UTF8Encoding $false))
}

# --- Application pool (sin codigo administrado), siempre despierto ------------------------------------
if (-not (Test-Path "IIS:\AppPools\$Nombre")) { New-WebAppPool -Name $Nombre | Out-Null }
Set-ItemProperty "IIS:\AppPools\$Nombre" -Name managedRuntimeVersion -Value ''
Set-ItemProperty "IIS:\AppPools\$Nombre" -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)
Set-ItemProperty "IIS:\AppPools\$Nombre" -Name startMode -Value 'AlwaysRunning'

icacls $Destino /grant 'IIS_IUSRS:(OI)(CI)RX' /T /Q | Out-Null

# --- Sitio -------------------------------------------------------------------------------------------
if (-not (Get-Website -Name $Nombre -ErrorAction SilentlyContinue)) {
    New-Website -Name $Nombre -Port $Puerto -PhysicalPath $Destino -ApplicationPool $Nombre | Out-Null
} else {
    Set-ItemProperty "IIS:\Sites\$Nombre" -Name physicalPath -Value $Destino
}

# --- Firewall (red del hotel) ------------------------------------------------------------------------
if (-not $SinFirewall) {
    $regla = "$Nombre TCP $Puerto"
    if (-not (Get-NetFirewallRule -DisplayName $regla -ErrorAction SilentlyContinue)) {
        New-NetFirewallRule -DisplayName $regla -Direction Inbound -Protocol TCP -LocalPort $Puerto -Action Allow -Profile Domain, Private | Out-Null
    }
}

# --- Arranque ----------------------------------------------------------------------------------------
if ([IO.File]::ReadAllText($config) -match '<SERVIDOR>') {
    Detener   # IIS arranca los sitios nuevos: espera a estar configurado
    Write-Host "`nInstalado. El sitio quedo DETENIDO: falta configurar $config" -ForegroundColor Yellow
    Write-Host '  1. ConnectionStrings:Hotel y ConnectionStrings:Seguridad (las mismas bases que usa Delphi, ver LEEME.md).'
    Write-Host "  2. Inicie el sitio:  Start-WebAppPool $Nombre ; Start-Website $Nombre"
    Write-Host "  3. Revise:  http://localhost:$Puerto/api/diagnostico/ping  y entre con un usuario de Delphi."
} else {
    Start-WebAppPool -Name $Nombre
    Start-Website -Name $Nombre
    Write-Host "`nActualizacion terminada: sitio iniciado." -ForegroundColor Green
}

Write-Host "`nHotelWeb: http://localhost:$Puerto"
