import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// En desarrollo, /api va a la API (dotnet run, puerto 5081). En el servidor la API sirve esta misma pagina.
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': 'http://localhost:5081',
    },
  },
  build: {
    // la API la sirve desde su carpeta wwwroot
    outDir: '../backend/HotelWeb.Api/wwwroot',
    emptyOutDir: true,
  },
})
