# HotelWeb

Web app del hotel y la cafeteria, para sustituir con el tiempo el programa Delphi. Trabaja sobre la misma base que
Delphi y conviven mientras tanto. Plan y reglas del negocio: [docs/PLAN.md](docs/PLAN.md).

## Estructura

- `backend/` - .NET 8: `HotelWeb.Api` (API y sitio), `HotelWeb.Core` (logica sin base de datos), `HotelWeb.Tests`.
- `frontend/` - React 19 + Vite + TypeScript. `npm run build` lo deja en `backend/HotelWeb.Api/wwwroot`.
- `deploy/` - paquete e instalacion en IIS ([deploy/LEEME.md](deploy/LEEME.md)).

## Desarrollo

```powershell
# API (puerto 5081), contra Hotel_new y Seguridad locales (appsettings.Development.json)
cd backend\HotelWeb.Api ; dotnet run --launch-profile http

# pagina (puerto 5173; /api va a la API)
cd frontend ; npm run dev

# pruebas
cd backend ; dotnet test HotelWeb.slnx
```

Se entra con un usuario y clave de Delphi (tabla `Seg_Usuario`).
