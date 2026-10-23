import { useEffect, useState } from 'react'
import { Resumen } from './Caja'
import { cierreImpreso, dinero, fecha, sesionGuardada, SesionVencida, type CierreImpreso as Datos } from './api'

const hora = (iso: string) => new Date(iso).toLocaleString('es-DO', { dateStyle: 'short', timeStyle: 'short' })

/**
 * El reporte del cierre de caja (carta, como ppRepcuadreCaja de Delphi): el detalle, el resumen por forma de pago, lo
 * esperado y lo contado, y las firmas de quien entrega y quien supervisa. Se abre en otra pestana: /cierre/{numero}.
 */
export default function CierreImpreso({ numero }: { numero: number }) {
  const [d, setD] = useState<Datos | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    if (!sesionGuardada()) {
      setError('Entre al sistema en otra pestaña y vuelva a abrir el cierre.')
      return
    }
    cierreImpreso(numero).then((r) => {
      setD(r)
      document.title = `Cierre de caja ${r.cierre.cierre}`
    }).catch((e) => setError(e instanceof SesionVencida ? 'La sesión venció: entre otra vez y vuelva a abrir el cierre.' : e instanceof Error ? e.message : String(e)))
  }, [numero])

  if (error) return <main className="factura-hoja"><p className="error">{error}</p></main>
  if (!d) return <main className="factura-hoja"><p>Cargando el cierre...</p></main>
  const c = d.cierre
  const hotel = c.ambito === 'Hotel'
  const difEf = Math.round((c.contadoEfectivo - c.esperadoEfectivo) * 100) / 100
  const difUs = Math.round((c.contadoDolares - c.esperadoDolares) * 100) / 100

  return (
    <main className="factura-hoja">
      <div className="no-imprimir botones">
        <button type="button" onClick={() => window.print()}>Imprimir</button>
        <button type="button" className="secundario" onClick={() => window.close()}>Cerrar</button>
      </div>
      <header className="factura-cabeza">
        <div>
          <h1>{d.emisor.razonSocial}</h1>
          <p>{d.emisor.direccion}</p>
        </div>
        <div className="factura-ncf">
          <h2>Cuadre de caja · {hotel ? 'Hotel' : 'Cafetería'}</h2>
          <p><strong>Cierre #{c.cierre}</strong></p>
          <p>Turno de: {c.usuario}</p>
          <p>{d.desde ? `Del ${hora(d.desde)} al ${hora(c.hasta)}` : `Al ${hora(c.hasta)}`}</p>
        </div>
      </header>

      <table className="factura-tabla">
        <thead>
          <tr><th>{hotel ? 'Recibo' : 'Factura'}</th><th>Fecha</th><th>{hotel ? 'Hab.' : 'Mesa'}</th><th>Nombre</th><th>Forma de pago</th><th className="num">RD$</th>{hotel && <th className="num">US$</th>}</tr>
        </thead>
        <tbody>
          {d.movimientos.map((m, i) => (
            <tr key={i}>
              <td>{m.tipo === 'A' ? `Adel. ${m.numero}` : m.numero}</td>
              <td>{fecha(m.fecha, true)}</td>
              <td>{m.referencia}</td>
              <td>{m.nombre}</td>
              <td>{m.formaNombre}</td>
              <td className="num">{dinero(m.monto)}</td>
              {hotel && <td className="num">{m.montoUs ? dinero(m.montoUs) : ''}</td>}
            </tr>
          ))}
        </tbody>
      </table>

      <h3>Resumen</h3>
      <Resumen r={d.resumen} hotel={hotel} />
      <dl className="totales">
        <dt>Efectivo contado</dt><dd>{dinero(c.contadoEfectivo)}</dd>
        <dt className="saldo">Diferencia</dt><dd className="saldo">{difEf === 0 ? 'Cuadra' : difEf > 0 ? `Sobran ${dinero(difEf)}` : `Faltan ${dinero(-difEf)}`}</dd>
        {hotel && (c.esperadoDolares > 0 || c.contadoDolares > 0) && <>
          <dt>Dólares contados</dt><dd>US$ {dinero(c.contadoDolares)}</dd>
          <dt className="saldo">Diferencia en dólares</dt><dd className="saldo">{difUs === 0 ? 'Cuadra' : `US$ ${dinero(difUs)}`}</dd>
        </>}
      </dl>
      {c.comentario && <p><strong>Comentario:</strong> {c.comentario}</p>}

      <div className="firmas">
        <div><span /> Entregado por: {c.usuario}</div>
        <div><span /> Supervisado por</div>
      </div>
    </main>
  )
}
