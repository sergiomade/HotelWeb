import { useEffect, useState } from 'react'
import { dinero, fecha, reservaImpresa, sesionGuardada, SesionVencida, type ReservaImpresa as Datos } from './api'

/**
 * La confirmacion de la reserva (como ppReport2 de UFrmMntReservacion de Delphi): el huesped, las fechas, las
 * habitaciones con su tarifa, y ademas los adelantos y lo que falta por pagar. Se abre en otra pestana: /reserva/{id}.
 */
export default function ReservaImpresa({ id }: { id: number }) {
  const [d, setD] = useState<Datos | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    if (!sesionGuardada()) {
      setError('Entre al sistema en otra pestaña y vuelva a abrir la reserva.')
      return
    }
    reservaImpresa(id).then((r) => {
      setD(r)
      document.title = `Reserva ${r.reserva.id}`
    }).catch((e) => setError(e instanceof SesionVencida ? 'La sesión venció: entre otra vez y vuelva a abrir la reserva.' : e instanceof Error ? e.message : String(e)))
  }, [id])

  if (error) return <main className="factura-hoja"><p className="error">{error}</p></main>
  if (!d) return <main className="factura-hoja"><p>Cargando la reserva...</p></main>
  const r = d.reserva
  const adelantos = r.adelantos.filter((a) => a.estatus === 'Activa')
  const saldo = Math.round((r.total - r.totalAdelantos) * 100) / 100
  const identificacion = [r.cedula && `Cédula ${r.cedula}`, r.pasaporte && `Pasaporte ${r.pasaporte}`, r.rnc && `RNC ${r.rnc}`].filter(Boolean).join(' · ')

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
          {d.emisor.telefono && <p>Tel.: {d.emisor.telefono}</p>}
          <p>RNC {d.emisor.rnc}</p>
        </div>
        <div className="factura-ncf">
          <h2>Confirmación de reserva</h2>
          <p><strong>Reserva No. {r.id}</strong></p>
          <p>Estado: {r.estadoNombre}</p>
          {r.creada && <p>Hecha el {fecha(r.creada, true)}{r.usuario ? ` por ${r.usuario}` : ''}</p>}
        </div>
      </header>

      <dl className="recibo-datos">
        <dt>A nombre de</dt><dd>{r.cliente}</dd>
        {r.huesped && r.huesped !== r.cliente && <><dt>Huésped</dt><dd>{r.huesped}</dd></>}
        {identificacion && <><dt>Identificación</dt><dd>{identificacion}</dd></>}
        {(r.contacto || r.telefono) && <><dt>Contacto</dt><dd>{[r.contacto, r.telefono].filter(Boolean).join(' · Tel. ')}</dd></>}
        <dt>Entrada</dt><dd>{fecha(r.entrada, true)}</dd>
        <dt>Salida</dt><dd>{fecha(r.salida, true)} ({r.noches} {r.noches === 1 ? 'noche' : 'noches'})</dd>
        <dt>Personas</dt><dd>{r.personas}</dd>
      </dl>

      <table className="factura-tabla">
        <thead>
          <tr><th>Tipo de habitación</th><th className="num">Cant.</th><th>Categoría</th><th className="num">Adultos</th><th className="num">Niños</th><th className="num">Infantes</th><th className="num">Tarifa</th><th className="num">Importe</th></tr>
        </thead>
        <tbody>
          {r.lineas.map((l) => (
            <tr key={l.secuencia}>
              <td>{l.tipoNombre}{l.noHabitacion ? ` (hab. ${l.noHabitacion})` : ''}</td>
              <td className="num">{l.cantidad}</td>
              <td>{l.categoria}</td>
              <td className="num">{l.adultos}</td>
              <td className="num">{l.ninos}</td>
              <td className="num">{l.bebes}</td>
              <td className="num">{dinero(l.tarifa)}</td>
              <td className="num">{dinero(l.importe)}</td>
            </tr>
          ))}
        </tbody>
      </table>

      <div className="factura-pie">
        <div className="factura-pagos">
          {adelantos.length > 0 && <>
            <h3>Adelantos recibidos</h3>
            <table className="factura-tabla">
              <tbody>
                {adelantos.map((a) => (
                  <tr key={a.id}><td>{fecha(a.fecha, true)}</td><td>{a.formaPagoNombre}</td><td className="num">{a.montoUs ? `US$ ${dinero(a.montoUs)}` : ''}</td><td className="num">{dinero(a.monto)}</td></tr>
                ))}
              </tbody>
            </table>
          </>}
        </div>
        <dl className="factura-totales">
          <dt>Total de la estadía</dt><dd>{dinero(r.total)}</dd>
          <dt>Adelantos</dt><dd>{dinero(r.totalAdelantos)}</dd>
          {saldo >= 0
            ? <><dt className="grande">Pendiente por pagar</dt><dd className="grande">{dinero(saldo)}</dd></>
            : <><dt className="grande">Saldo a favor</dt><dd className="grande">{dinero(-saldo)}</dd></>}
        </dl>
      </div>
      <p className="tenue-impreso">La tarifa es por habitación y por noche.</p>
      {r.comentario && <p><strong>Comentarios:</strong> {r.comentario}</p>}
    </main>
  )
}
