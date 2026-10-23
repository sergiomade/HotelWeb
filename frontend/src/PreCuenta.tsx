import { useEffect, useState } from 'react'
import { dinero, preCuentaImpresa, sesionGuardada, SesionVencida, type PreCuentaImpresa } from './api'

/**
 * La pre-cuenta de una mesa (rollo POS de 80 mm, como ppRepxPantalla de Delphi). No es comprobante fiscal: el e-CF
 * sale al cobrar. Se abre en otra pestana: /precuenta/{factura}.
 */
export default function PreCuenta({ factura }: { factura: number }) {
  const [p, setP] = useState<PreCuentaImpresa | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    if (!sesionGuardada()) {
      setError('Entre al sistema en otra pestaña y vuelva a abrir la pre-cuenta.')
      return
    }
    preCuentaImpresa(factura).then((r) => {
      setP(r)
      document.title = `Pre-cuenta mesa ${r.cuenta.mesa}`
    }).catch((e) =>
      setError(e instanceof SesionVencida ? 'La sesión venció: entre otra vez y vuelva a abrir la pre-cuenta.' : e instanceof Error ? e.message : String(e)))
  }, [factura])

  if (error) return <main className="ticket"><p className="error">{error}</p></main>
  if (!p) return <main className="ticket"><p>Cargando la pre-cuenta...</p></main>
  const c = p.cuenta
  const t = c.totales

  return (
    <main className="ticket">
      <div className="no-imprimir botones">
        <button type="button" onClick={() => window.print()}>Imprimir</button>
        <button type="button" className="secundario" onClick={() => window.close()}>Cerrar</button>
      </div>
      <header className="ticket-centro">
        <strong>{p.emisor.razonSocial}</strong>
        <p>RNC {p.emisor.rnc}</p>
        <p>{p.emisor.direccion}</p>
      </header>
      <hr />
      <div className="ticket-centro">
        <strong>PRE-CUENTA</strong>
        <p>No es comprobante fiscal</p>
      </div>
      <p>{c.nombreMesa} · Cuenta #{c.factura}</p>
      <p>{new Date(p.hora).toLocaleString('es-DO', { dateStyle: 'short', timeStyle: 'short' })}</p>
      <hr />
      <table className="ticket-tabla">
        <tbody>
          {c.lineas.map((l) => (
            <tr key={l.secuencia}>
              <td>{l.cantidad > 1 ? `${l.cantidad} x ` : ''}{l.descripcion}{l.cantidad > 1 && <span className="ticket-sub">{dinero(l.precio)} c/u</span>}</td>
              <td className="num">{dinero(l.importe)}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <hr />
      <table className="ticket-tabla">
        <tbody>
          <tr><td>Subtotal</td><td className="num">{dinero(t.subTotal)}</td></tr>
          {c.conItbis && <tr><td>ITBIS 18 %</td><td className="num">{dinero(t.itbis)}</td></tr>}
          {c.conPropina && <tr><td>Propina 10 %</td><td className="num">{dinero(t.propina)}</td></tr>}
          <tr className="ticket-total"><td>TOTAL</td><td className="num">{dinero(t.total)}</td></tr>
          {c.tasaDolar > 0 && <tr><td>En dólares (tasa {dinero(c.tasaDolar)})</td><td className="num">US$ {dinero(t.total / c.tasaDolar)}</td></tr>}
        </tbody>
      </table>
      <hr />
      <div className="ticket-centro">
        {p.usuario && <p>Atendido por: {p.usuario}</p>}
        <p>¡Gracias por su visita!</p>
      </div>
    </main>
  )
}
