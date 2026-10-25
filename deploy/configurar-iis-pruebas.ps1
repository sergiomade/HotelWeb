<#
.SYNOPSIS
  Configura el HotelWeb de IIS de ESTA maquina para pruebas, todo EN LA NUBE: hotel = HotelPruebas y
  seguridad = db_aa9d01_hseguridad (los usuarios reales del hotel). Las bases locales no se usan (decision 2026-10-25).

.DESCRIPTION
  Despues de instalar-iis.ps1. Como administrador:
    1. Escribe ConnectionStrings:Hotel y ConnectionStrings:Seguridad en C:\inetpub\hotelweb\appsettings.json con las
       conexiones que lee de C:\Contabilidad\exe\conexiones.ini (la clave queda solo en ese appsettings del servidor,
       nunca en el proyecto). Ecf:Ambiente = 1 (testecf), porque HotelPruebas envia al ambiente de pruebas.
    2. Inicia el sitio y prueba /api/diagnostico/ping.
  Se puede repetir.

.EXAMPLE
  .\configurar-iis-pruebas.ps1
#>
[CmdletBinding()]
param(
    [string]$Destino = 'C:\inetpub\hotelweb',
    [string]$Nombre = 'HotelWeb',
    [int]$Puerto = 8098,
    [string]$Ini = 'C:\Contabilidad\exe\conexiones.ini',
    [string]$BaseHotel = 'db_aa9d01_hotelpruebas',
    [string]$BaseSeguridad = 'db_aa9d01_hseguridad',
    [int]$Ambiente = 1,   # Ecf:Ambiente: 1 = testecf (HotelPruebas), 2 = certecf, 3 = produccion
    [switch]$SeguridadNube   # ya es siempre asi; se deja para no romper la costumbre
)

$ErrorActionPreference = 'Stop'
$esAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $esAdmin) { throw 'Ejecute este script como administrador.' }
$config = Join-Path $Destino 'appsettings.json'
if (-not (Test-Path $config)) { throw "No existe $config. Ejecute primero instalar-iis.ps1." }

# --- 1) Las dos conexiones de conexiones.ini -----------------------------------------------------------
function ConexionIni([string]$base) {
    $linea = (Select-String -Path $Ini -Pattern "Initial Catalog=$base;" | Select-Object -First 1).Line
    if (-not $linea) { throw "No hay una linea de $base en $Ini." }
    function V($n) { ([regex]::Match($linea, "$n=([^;]*)")).Groups[1].Value }
    "Server=$(V 'Data Source');Database=$(V 'Initial Catalog');User ID=$(V 'User ID');Password=$(V 'Password');TrustServerCertificate=true"
}
$hotel = ConexionIni $BaseHotel
$seguridad = ConexionIni $BaseSeguridad
$textoSeguridad = "$BaseSeguridad en la nube"

# --- 2) appsettings.json del sitio ----------------------------------------------------------------------
$j = Get-Content $config -Raw | ConvertFrom-Json
$j.ConnectionStrings.Hotel = $hotel
$j.ConnectionStrings.Seguridad = $seguridad
# HotelPruebas envia a testecf: el QR de las facturas debe apuntar al ambiente de pruebas de la DGII (1)
if ($j.PSObject.Properties.Name -notcontains 'Ecf') { $j | Add-Member -NotePropertyName Ecf -NotePropertyValue ([pscustomobject]@{}) }
$j.Ecf | Add-Member -NotePropertyName Ambiente -NotePropertyValue $Ambiente -Force
[IO.File]::WriteAllText($config, ($j | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding $false))
Write-Host "Conexiones escritas en $config (hotel: $BaseHotel en la nube; seguridad: $textoSeguridad)."

# --- 3) Arrancar y probar ---------------------------------------------------------------------------------
Import-Module WebAdministration
Start-WebAppPool -Name $Nombre -ErrorAction SilentlyContinue
Restart-WebAppPool -Name $Nombre
Start-Website -Name $Nombre
Start-Sleep -Seconds 3
try {
    $r = Invoke-RestMethod "http://localhost:$Puerto/api/diagnostico/ping" -TimeoutSec 60
    $e = Invoke-RestMethod "http://localhost:$Puerto/api/empresa" -TimeoutSec 60
    Write-Host "`nHotelWeb funcionando: http://localhost:$Puerto  ($($e.nombre))" -ForegroundColor Green
    Write-Host "Desde otra PC de la red: http://$($env:COMPUTERNAME):$Puerto"
} catch {
    Write-Host "`nEl sitio no respondio: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Revise el Visor de eventos (Aplicacion) o active stdoutLogEnabled en $Destino\web.config."
}
