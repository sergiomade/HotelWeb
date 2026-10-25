# Instalar HotelWeb en el servidor del hotel

Un solo sitio de IIS: la API sirve tambien la pagina web. Trabaja sobre **las mismas bases que Delphi**
(la del hotel y la de usuarios); no crea tablas.

## Requisitos del servidor

- IIS.
- ASP.NET Core **Hosting Bundle 8** (dotnet.microsoft.com, ".NET 8.0 - Hosting Bundle").
- Que el servidor llegue al SQL Server del hotel.

## En la maquina de desarrollo

```powershell
.\deploy\preparar-paquete.ps1
```

Deja la carpeta `paquete` (app\, instalar-iis.ps1, LEEME.md). Copiela al servidor.

## En el servidor (PowerShell como administrador)

```powershell
cd <carpeta del paquete>
.\instalar-iis.ps1                 # sitio HotelWeb, puerto 8098, en C:\inetpub\hotelweb
```

La primera vez el sitio queda **detenido**. Abra `C:\inetpub\hotelweb\appsettings.json` y ponga las dos conexiones,
las mismas de `conexiones.ini` de Delphi (seccion del hotel):

```json
"ConnectionStrings": {
  "Hotel":     "Server=SERVIDOR;Database=BASE_DEL_HOTEL;User ID=USUARIO;Password=CLAVE;TrustServerCertificate=true",
  "Seguridad": "Server=SERVIDOR;Database=SEGURIDAD;User ID=USUARIO;Password=CLAVE;TrustServerCertificate=true"
}
```

`Jwt:Key` ya la genero el instalador: no la cambie (si cambia, todos deben volver a entrar).

`Permisos:EditanCatalogos` es la lista de usuarios (Usuario_Id de Seg_Usuario) que pueden cambiar precios y demas catalogos; los otros solo consultan. Viene con 1, 4 y 6 (Wilton, Sergio Made y Anabel en hseguridad); al activar la seguridad de la web son los que entran al grupo ADMIN HOTEL.

Despues de activar la seguridad de la web (menu Seguridad, etapa 10) esa lista ya no se usa: los permisos salen de los grupos del hotel en la base Seguridad.

## Seguridad de la web (una vez)

Antes de pulsar "Activar la seguridad" en el menu Seguridad, ejecute en la base Seguridad (con respaldo)
`sql\seguridad-triggers-raiserror.sql` (viene en el paquete): corrige los triggers antiguos de Delphi (13 en hseguridad) que los SQL Server nuevos no aceptan
(sin el, no se puede agregar a nadie a un grupo). Se puede repetir sin problema.

Inicie el sitio y revise:

```powershell
Start-WebAppPool HotelWeb ; Start-Website HotelWeb
```

- `http://localhost:8098/api/diagnostico/ping` debe responder `{"ok":true,...}`.
- `http://SERVIDOR:8098` desde otra PC: entrar con un usuario y clave de Delphi; la pantalla inicial dice si
  conecta a las dos bases.

## IIS de pruebas en la maquina de desarrollo

`deploy\configurar-iis-pruebas.ps1` (como administrador, despues de `instalar-iis.ps1`) deja el sitio con todo
en la nube: HotelPruebas y `db_aa9d01_hseguridad` (los usuarios reales del hotel). Las conexiones salen de
`C:\Contabilidad\exe\conexiones.ini`. Lo inicia y lo prueba.

## Actualizar

Mismo procedimiento: `preparar-paquete.ps1` y luego `instalar-iis.ps1` en el servidor. No toca `appsettings.json`.
