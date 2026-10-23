import { useEffect, useState } from 'react'
import { dinero, fecha, reciboImpreso, sesionGuardada, SesionVencida, type ReciboImpreso as Datos } from './api'

/**
 * El recibo de ingreso (media carta, como ppReport3 de UFrmMntRecibos de Delphi): hemos recibido de, la suma en numero
 * y en letras, el concepto, las formas de pago y las firmas. Se abre en otra pestana: /recibo/{numero}.
 */
export default function ReciboImpreso({ numero }: { numero: number }) {
  const [d, setD] = useState<Datos | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    if (!sesionGuardada()) {
      setError('Entre al sistema en otra pestaña y vuelva a abrir el recibo.')
      return
    }
    reciboImpreso(numero).then((r) => {
      setD(r)
      document.title = `Recibo ${r.recibo.numero}`
    }).catch((e) => setError(e instanceof SesionVencida ? 'La sesión venció: entre otra vez y vuelva a abrir el recibo.' : e instanceof Error ? e.message : String(e)))
  }, [numero])

  if (error) return <main className="media-hoja"><p className="error">{error}</p></main>
  if (!d) return <main className="media-hoja"><p>Cargando el recibo...</p></main>
  const r = d.recibo
  const anulado = r.estatus !== 'Activo'

  return (
    <main className="media-hoja">
      <div className="no-imprimir botones">
        <button type="button" onClick={() => window.print()}>Imprimir</button>
        <button type="button" className="secundario" onClick={() => window.close()}>Cerrar</button>
      </div>
      <header className="factura-cabeza">
        <div>
          <h1>{d.emisor.razonSocial}</h1>
          <p>{d.emisor.direccion}</p>
          {d.emisor.telefono && <p>Tel.: {d.emisor.telefono}</p>}
          <p>RNC {d.emisor.rnc}</p>
        </div>
        <div className="factura-ncf">
          <h2>Recibo de ingresos</h2>
          <p><strong>No. {r.numero}</strong></p>
          <p>Fecha: {fecha(r.fecha, true)}</p>
          {r.habitacion > 0 && <p>Habitación: {r.habitacion}</p>}
        </div>
      </header>
      {anulado && <p className="sello-anulado">{r.estatus.toUpperCase()}</p>}

      <dl className="recibo-datos">
        {d.clienteId && <><dt>Código del cliente</dt><dd>{d.clienteId}</dd></>}
        <dt>Hemos recibido de</dt><dd>{r.nombre}</dd>
        <dt>La suma de</dt><dd><strong>RD$ {dinero(r.monto)}</strong> ({d.enLetras})</dd>
        <dt>Por concepto de</dt><dd>{r.concepto}</dd>
      </dl>

      {r.pagos.length > 0 && (
        <table className="factura-tabla recibo-pagos">
          <thead><tr><th>Forma de pago</th><th>Descripción</th><th>Referencia</th><th className="num">US$</th><th className="num">RD$</th></tr></thead>
          <tbody>
            {r.pagos.map((p, i) => (
              <tr key={i}>
                <td>{p.formaPagoNombre}</td>
                <td>{p.descripcion}</td>
                <td>{[p.noCheque && `Cheque ${p.noCheque}`, p.banco].filter(Boolean).join(' · ')}</td>
                <td className="num">{p.montoUs ? dinero(p.montoUs) : ''}</td>
                <td className="num">{dinero(p.monto)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      <div className="firmas">
        <div>Preparado por: {r.usuario}</div>
        <div>Verificado por</div>
      </div>
    </main>
  )
}
