import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import FacturaImpresa from './FacturaImpresa.tsx'
import TicketWalkIn from './TicketWalkIn.tsx'

// paginas para imprimir, solas en su pestana (usan la sesion guardada):
// /factura/{numero} = factura del check-out (carta); /walkin/{numero} = ticket del walk-in (POS 80 mm)
const factura = /^\/factura\/(\d+)\/?$/.exec(window.location.pathname)
const walkin = /^\/walkin\/(\d+)\/?$/.exec(window.location.pathname)

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    {factura ? <FacturaImpresa numero={Number(factura[1])} />
      : walkin ? <TicketWalkIn numero={Number(walkin[1])} />
      : <App />}
  </StrictMode>,
)
