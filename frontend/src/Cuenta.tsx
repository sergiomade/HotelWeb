import { useEffect, useState } from 'react'
import Fondo from './Fondo'
import { dinero, fecha, obtenerCuenta, SesionVencida, type CuentaHabitacion, type HabitacionRack } from './api'

/** Panel con la cuenta abierta de una habitacion (o sus datos, si esta libre). Solo lectura. */
export default function Cuenta({ habitacion: h, alCerrar, alVencer }: {
  habitacion: HabitacionRack
  alCerrar: () => void
  alVencer: () => void
}) {
  const [cuenta, setCuenta] = useState<CuentaHabitacion | null>(null)
  const [error, setError] = useState('')
  const ocupada = h.situacion === 'Ocupada'

  useEffect(() => {
    if (!ocupada) return
    obtenerCuenta(h.numero)
      .then(setCuenta)
      .catch((e) => (e instanceof SesionVencida ? alVencer() : setError(e instanceof Error ? e.message : String(e))))
  }, [h.numero, ocupada, alVencer])

  useEffect(() => {
    const tecla = (e: KeyboardEvent) => e.key === 'Escape' && alCerrar()
    window.addEventListener('keydown', tecla)
    return () => window.removeEventListener('keydown', tecla)
  }, [alCerrar])

  return (
    <Fondo alCerrar={alCerrar}>
      <aside className="panel" role="dialog" aria-modal="true" aria-label={`Habitacion ${h.numero}`}>
        <header>
          <div>
            <h2>Habitacion {h.numero}</h2>
            <p className="tenue">{h.tipo} · {h.estado}{h.comentario ? ` · ${h.comentario}` : ''}</p>
          </div>
          <button type="button" className="secundario" onClick={alCerrar} aria-label="Cerrar">Cerrar</button>
        </header>

        {!ocupada && <Libre h={h} />}
        {ocupada && error && <p className="error">{error}</p>}
        {ocupada && !cuenta && !error && <p className="tenue">Cargando la cuenta...</p>}
        {cuenta && <Detalle c={cuenta} credito={h.credito} />}
      </aside>
    </Fondo>
  )
}

function Libre({ h }: { h: HabitacionRack }) {
  if (h.situacion === 'Bloqueada') return <p>La habitacion esta {h.estado?.toLowerCase()}: no se puede ocupar.</p>
  if (h.llegada)
    return (
      <dl className="datos-lista">
        <dt>Llega hoy</dt><dd>{h.llegada.huesped}</dd>
        <dt>Reserva</dt><dd>#{h.llegada.reserva}</dd>
        <dt>Salida</dt><dd>{fecha(h.llegada.fechaSalida, true)}</dd>
      </dl>
    )
  return <p>Libre, sin llegadas para hoy.</p>
}

function Detalle({ c, credito }: { c: CuentaHabitacion; credito: boolean }) {
  const d = c.desglose
  const impuestos = [c.llevaItbis && !c.regimenEspecial ? 'ITBIS 18 %' : null, c.llevaPropina ? 'propina 10 %' : null]
    .filter(Boolean).join(' y ') || 'sin ITBIS ni propina'
  return (
    <>
      <dl className="datos-lista">
        <dt>Huesped</dt><dd>{c.huesped}</dd>
        <dt>Cliente</dt><dd>{c.cliente}{c.rnc ? ` · RNC ${c.rnc}` : ''}</dd>
        <dt>Estadia</dt><dd>{fecha(c.fechaEntrada, true)} al {fecha(c.fechaSalida, true)}</dd>
        <dt>Cuenta</dt><dd>#{c.factura}{c.reserva ? ` · reserva #${c.reserva}` : ''}{c.categoria ? ` · categoria ${c.categoria}` : ''}</dd>
        <dt>Cobra</dt><dd>{impuestos}{c.regimenEspecial ? ' (regimen especial, E44)' : ''}</dd>
        <dt>Cafeteria</dt><dd>{credito ? 'Credito habilitado' : 'Sin credito'}</dd>
        {c.transferida && <><dt>Ojo</dt><dd className="error">Cuenta transferida a otra habitacion</dd></>}
      </dl>

      <div className="tabla-marco">
        <table className="tabla">
          <thead>
            <tr><th>Fecha</th><th>Descripcion</th><th className="num">Cant.</th><th className="num">Importe</th></tr>
          </thead>
          <tbody>
            {c.lineas.map((l) => (
              <tr key={l.secuencia} className={!l.activo ? 'inactiva' : l.importe < 0 ? 'pago' : ''}>
                <td>{fecha(l.fecha)}</td>
                <td>{l.descripcion}{!l.activo && ' (anulada)'}</td>
                <td className="num">{l.cantidad}</td>
                <td className="num">{dinero(l.importe)}</td>
              </tr>
            ))}
            {c.lineas.length === 0 && <tr><td colSpan={4} className="tenue">Sin cargos todavia.</td></tr>}
          </tbody>
        </table>
      </div>

      <dl className="totales">
        <dt>Cargos</dt><dd>{dinero(d.cargos)}</dd>
        {d.itbis !== 0 && <><dt>ITBIS</dt><dd>{dinero(d.itbis)}</dd></>}
        {d.propina !== 0 && <><dt>Propina</dt><dd>{dinero(d.propina)}</dd></>}
        {d.pagosYPayOut !== 0 && <><dt>Pagos y PAY-OUT</dt><dd>{dinero(d.pagosYPayOut)}</dd></>}
        <dt className="saldo">Saldo</dt><dd className={`saldo${c.saldo < 0 ? ' mal' : ''}`}>{dinero(c.saldo)}</dd>
      </dl>
      {c.saldo < 0 && <p className="tenue">Saldo a favor del huesped (pago mayor que los cargos).</p>}
      {d.saldo !== c.saldo && (
        <p className="error">El desglose ({dinero(d.saldo)}) no coincide con el saldo de la base: avisar.</p>
      )}
    </>
  )
}
