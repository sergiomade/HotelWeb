<#
.SYNOPSIS
  Arma el paquete de instalacion de HotelWeb (API + pagina web + instalador) para el servidor del hotel.

.DESCRIPTION
  Se ejecuta en la maquina de desarrollo. Genera la carpeta "paquete" con:
    app\              API publicada (Release) con la pagina web compilada en wwwroot. Su appsettings.json es la
                      PLANTILLA del repositorio (sin claves); el de desarrollo no se copia.
    instalar-iis.ps1  Instalador para IIS (en el servidor, como administrador)
    LEEME.md

.EXAMPLE
  .\deploy\preparar-paquete.ps1
#>
[CmdletBinding()]
param([string]$Salida)

$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
if (-not $Salida) { $Salida = Join-Path $raiz 'paquete' }
Write-Host "Salida: $Salida`n"

if (Test-Path $Salida) { Get-ChildItem $Salida -Force | Remove-Item -Recurse -Force }
New-Item -ItemType Directory -Force -Path $Salida | Out-Null

# 1) Pagina web: queda en backend\HotelWeb.Api\wwwroot (la publica dotnet publish)
Write-Host '== Compilando la pagina web...'
Push-Location (Join-Path $raiz 'frontend')
try {
    if (-not (Test-Path 'node_modules')) { npm ci | Out-Null }
    npm run build | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'La compilacion de la pagina web fallo.' }
}
finally { Pop-Location }

# 2) API
Write-Host '== Publicando la API...'
$app = Join-Path $Salida 'app'
dotnet publish (Join-Path $raiz 'backend\HotelWeb.Api\HotelWeb.Api.csproj') -c Release -o $app --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish fallo.' }
# solo la plantilla: el de desarrollo tiene los datos de esta maquina
Get-ChildItem $app -Filter 'appsettings.*.json' | Remove-Item -Force

# 3) Instalador
foreach ($a in 'instalar-iis.ps1', 'LEEME.md') { Copy-Item (Join-Path $PSScriptRoot $a) $Salida }

# 4) Verificaciones
if (-not (Test-Path (Join-Path $app 'wwwroot\index.html'))) { throw 'El paquete no trae la pagina web (wwwroot\index.html).' }
$plantilla = [IO.File]::ReadAllText((Join-Path $app 'appsettings.json'))
if ($plantilla -notmatch '<CAMBIAR') { throw 'El appsettings.json del paquete no es la plantilla (falta el <CAMBIAR> de Jwt:Key): abortado.' }
if ($plantilla -notmatch '<SERVIDOR>') { throw 'El appsettings.json del paquete trae una conexion real: abortado.' }

Write-Host "`nPaquete listo en: $Salida" -ForegroundColor Green
Write-Host 'Copielo al servidor y ejecute .\instalar-iis.ps1 como administrador (ver LEEME.md).'
