# Arranca la API de HotelWeb (puerto 5081) contra las bases EN LA NUBE (decision del usuario 2026-10-23/25: el
# desarrollo y las pruebas van en la nube; las bases locales no se usan):
#   hotel     = db_aa9d01_hotelpruebas
#   seguridad = db_aa9d01_hseguridad: son los usuarios REALES del hotel; lo que se cambie en el menu Seguridad lo
#               ven Delphi y el hotel enseguida.
#
# La conexion se lee al arrancar de C:\Contabilidad\exe\conexiones.ini (lineas de las dos bases, aunque esten
# comentadas con ;). La clave no queda en ningun archivo del proyecto ni en pantalla. Sin este script, dotnet run
# toma las mismas conexiones de los user secrets (README.md).
#
# La pagina: en otra ventana, cd ..\frontend ; npm run dev  (http://localhost:5173), o la compilada en :5081.
param([switch]$SeguridadNube)   # ya es siempre asi; se deja para no romper la costumbre

$ErrorActionPreference = 'Stop'
$ini = 'C:\Contabilidad\exe\conexiones.ini'

function Conexion([string]$base) {
    $linea = (Select-String -Path $ini -Pattern "Initial Catalog=$base;" | Select-Object -First 1).Line
    if (-not $linea) { throw "No hay una linea de $base en $ini." }
    function V($n) { ([regex]::Match($linea, "$n=([^;]*)")).Groups[1].Value }
    "Server=$(V 'Data Source');Database=$(V 'Initial Catalog');User ID=$(V 'User ID');Password=$(V 'Password');TrustServerCertificate=true"
}

$env:ConnectionStrings__Hotel = Conexion 'db_aa9d01_hotelpruebas'
$env:ConnectionStrings__Seguridad = Conexion 'db_aa9d01_hseguridad'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
Write-Host 'Hotel: db_aa9d01_hotelpruebas EN LA NUBE.' -ForegroundColor Green
Write-Host 'Seguridad: db_aa9d01_hseguridad EN LA NUBE (usuarios reales del hotel).' -ForegroundColor Yellow

Set-Location (Join-Path $PSScriptRoot 'HotelWeb.Api')
dotnet run --launch-profile http
