# HotelWeb

Web app del hotel y la cafeteria, para sustituir con el tiempo el programa Delphi. Trabaja sobre la misma base que
Delphi y conviven mientras tanto. Plan y reglas del negocio: [docs/PLAN.md](docs/PLAN.md).

## Estructura

- `backend/` - .NET 8: `HotelWeb.Api` (API y sitio), `HotelWeb.Core` (logica sin base de datos), `HotelWeb.Tests`.
- `frontend/` - React 19 + Vite + TypeScript. `npm run build` lo deja en `backend/HotelWeb.Api/wwwroot`.
- `deploy/` - paquete e instalacion en IIS ([deploy/LEEME.md](deploy/LEEME.md)).

## Desarrollo

```powershell
# Todo va EN LA NUBE (las bases locales no se usan): hotel = db_aa9d01_hotelpruebas,
# seguridad = db_aa9d01_hseguridad (los usuarios REALES del hotel: lo que se cambie lo ve Delphi enseguida).

# API leyendo las conexiones de C:\Contabilidad\exe\conexiones.ini
cd backend ; .\hotelpruebas.ps1

# o con dotnet run: las conexiones (con clave) van en los user secrets, fuera del proyecto.
# Una sola vez por PC (en el formato de .NET, no el de Delphi: Server=...;Database=...;User ID=...;Password=...):
cd backend\HotelWeb.Api
dotnet user-secrets set "ConnectionStrings:Hotel" "Server=sql5113.site4now.net;Database=db_aa9d01_hotelpruebas;User ID=...;Password=...;TrustServerCertificate=true"
dotnet user-secrets set "ConnectionStrings:Seguridad" "Server=sql5088.site4now.net;Database=db_aa9d01_hseguridad;User ID=...;Password=...;TrustServerCertificate=true"
dotnet run --launch-profile http

# pagina (puerto 5173; /api va a la API)
cd frontend ; npm run dev

# pruebas
cd backend ; dotnet test HotelWeb.slnx
```

Se entra con un usuario y clave de Delphi (tabla `Seg_Usuario`).
