import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import FacturaImpresa from './FacturaImpresa.tsx'

// /factura/{numero}: la factura para imprimir, sola en su pestana (usa la sesion guardada)
const factura = /^\/factura\/(\d+)\/?$/.exec(window.location.pathname)

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    {factura ? <FacturaImpresa numero={Number(factura[1])} /> : <App />}
  </StrictMode>,
)
