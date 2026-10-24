# Arranca la API de HotelWeb (puerto 5081) contra HotelPruebas, la base del hotel EN LA NUBE (decision del usuario
# 2026-10-23: el desarrollo y las pruebas van en la nube; asi se ve lo que prueban en el hotel).
#
# La conexion se lee al arrancar de C:\Contabilidad\exe\conexiones.ini (lineas de db_aa9d01_hotelpruebas y
# db_aa9d01_hseguridad, aunque esten comentadas con ;). La clave no queda en ningun archivo del proyecto ni en pantalla.
#
#   .\hotelpruebas.ps1                 hotel = HotelPruebas (nube), seguridad = hSeguridad LOCAL (copia bajada de la nube)
#   .\hotelpruebas.ps1 -SeguridadNube  seguridad = db_aa9d01_hseguridad (nube): son los usuarios REALES del hotel;
#                                      lo que se cambie en el menu Seguridad lo ven Delphi y el hotel enseguida.
#
# La pagina: en otra ventana, cd ..\frontend ; npm run dev  (http://localhost:5173), o la compilada en :5081.
param([switch]$SeguridadNube)

$ErrorActionPreference = 'Stop'
$ini = 'C:\Contabilidad\exe\conexiones.ini'

function Conexion([string]$base) {
    $linea = (Select-String -Path $ini -Pattern "Initial Catalog=$base;" | Select-Object -First 1).Line
    if (-not $linea) { throw "No hay una linea de $base en $ini." }
    function V($n) { ([regex]::Match($linea, "$n=([^;]*)")).Groups[1].Value }
    "Server=$(V 'Data Source');Database=$(V 'Initial Catalog');User ID=$(V 'User ID');Password=$(V 'Password');TrustServerCertificate=true"
}

$env:ConnectionStrings__Hotel = Conexion 'db_aa9d01_hotelpruebas'
if ($SeguridadNube) {
    $env:ConnectionStrings__Seguridad = Conexion 'db_aa9d01_hseguridad'
    Write-Host 'Seguridad: db_aa9d01_hseguridad EN LA NUBE (usuarios reales del hotel).' -ForegroundColor Yellow
} else {
    $env:ConnectionStrings__Seguridad = 'Server=.;Database=hSeguridad;Integrated Security=true;TrustServerCertificate=true'
    Write-Host 'Seguridad: hSeguridad local.'
}
$env:ASPNETCORE_ENVIRONMENT = 'Development'
Write-Host 'Hotel: db_aa9d01_hotelpruebas EN LA NUBE.' -ForegroundColor Green

Set-Location (Join-Path $PSScriptRoot 'HotelWeb.Api')
dotnet run --launch-profile http
