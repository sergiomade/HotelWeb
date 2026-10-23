import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import FacturaImpresa from './FacturaImpresa.tsx'
import NotaImpresa from './NotaImpresa.tsx'
import TicketWalkIn from './TicketWalkIn.tsx'
import PreCuenta from './PreCuenta.tsx'

// paginas para imprimir, solas en su pestana (usan la sesion guardada):
// /factura/{numero} = factura del check-out (carta); /walkin/{numero} = ticket del walk-in (POS 80 mm);
// /nota/{hospedaje|walkin}/{numero} = nota de credito E34 (carta)
// /precuenta/{factura} = pre-cuenta de una mesa de la cafeteria (POS 80 mm)
const ruta = window.location.pathname
const factura = /^\/factura\/(\d+)\/?$/.exec(ruta)
const walkin = /^\/walkin\/(\d+)\/?$/.exec(ruta)
const nota = /^\/nota\/(hospedaje|walkin)\/(\d+)\/?$/.exec(ruta)
const precuenta = /^\/precuenta\/(\d+)\/?$/.exec(ruta)

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    {factura ? <FacturaImpresa numero={Number(factura[1])} />
      : walkin ? <TicketWalkIn numero={Number(walkin[1])} />
      : precuenta ? <PreCuenta factura={Number(precuenta[1])} />
      : nota ? <NotaImpresa origen={nota[1]} numero={Number(nota[2])} />
      : <App />}
  </StrictMode>,
)
