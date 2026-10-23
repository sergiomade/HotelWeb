import { useCallback, useEffect, useState, type FormEvent } from 'react'
import Fondo from './Fondo'
import {
  anularCargo, cargarACuenta, dinero, fecha, obtenerCuenta, obtenerServicios, permisosCuenta, seAnula, SesionVencida,
  type CuentaHabitacion, type HabitacionRack, type Servicio,
} from './api'

/** Panel con la cuenta abierta de una habitacion: cargos, pagos y saldo; agregar un cargo y anular uno. */
export default function Cuenta({ habitacion: h, alCerrar, alVencer, alCambiar }: {
  habitacion: HabitacionRack
  alCerrar: () => void
  alVencer: () => void
  alCambiar?: () => void
}) {
  const [cuenta, setCuenta] = useState<CuentaHabitacion | null>(null)
  const [anula, setAnula] = useState(false)
  const [error, setError] = useState('')
  const ocupada = h.situacion === 'Ocupada'

  const manejar = useCallback((e: unknown) => {
    if (e instanceof SesionVencida) alVencer()
    else setError(e instanceof Error ? e.message : String(e))
  }, [alVencer])

  useEffect(() => {
    if (!ocupada) return
    obtenerCuenta(h.numero).then(setCuenta).catch(manejar)
    permisosCuenta().then((p) => setAnula(p.anulaCargos)).catch(() => setAnula(false))
  }, [h.numero, ocupada, manejar])

  useEffect(() => {
    const tecla = (e: KeyboardEvent) => e.key === 'Escape' && alCerrar()
    window.addEventListener('keydown', tecla)
    return () => window.removeEventListener('keydown', tecla)
  }, [alCerrar])

  function actualizada(c: CuentaHabitacion) {
    setCuenta(c)
    setError('')
    alCambiar?.()
  }

  async function anular(secuencia: number, descripcion: string, importe: number) {
    if (!window.confirm(`¿Anular "${descripcion}" por ${dinero(importe)}? Queda anulado en la cuenta (no se borra).`)) return
    try {
      actualizada(await anularCargo(h.numero, secuencia))
    } catch (e) {
      manejar(e)
    }
  }

  return (
    <Fondo alCerrar={alCerrar}>
      <aside className="panel" role="dialog" aria-modal="true" aria-label={`Habitacion ${h.numero}`}>
        <header>
          <div>
            <h2>Habitación {h.numero}</h2>
            <p className="tenue">{h.tipo} · {h.estado}{h.comentario ? ` · ${h.comentario}` : ''}</p>
          </div>
          <button type="button" className="secundario" onClick={alCerrar} aria-label="Cerrar">Cerrar</button>
        </header>

        {!ocupada && <Libre h={h} />}
        {ocupada && error && <p className="error" role="alert">{error}</p>}
        {ocupada && !cuenta && !error && <p className="tenue">Cargando la cuenta...</p>}
        {cuenta && (
          <>
            {!cuenta.transferida && <AgregarCargo habitacion={h.numero} alCargar={actualizada} alError={manejar} />}
            <Detalle c={cuenta} credito={h.credito} anula={anula} alAnular={anular} />
          </>
        )}
      </aside>
    </Fondo>
  )
}

function Libre({ h }: { h: HabitacionRack }) {
  if (h.situacion === 'Bloqueada') return <p>La habitación está {h.estado?.toLowerCase()}: no se puede ocupar.</p>
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

// servicios que pone el sistema: no se cargan a mano (la API tambien lo revisa)
const DEL_SISTEMA = new Set([3, 7, 11])

/** Agregar un servicio a la cuenta, como el boton de cargos de Delphi. */
function AgregarCargo({ habitacion, alCargar, alError }: {
  habitacion: number
  alCargar: (c: CuentaHabitacion) => void
  alError: (e: unknown) => void
}) {
  const [abierto, setAbierto] = useState(false)
  const [servicios, setServicios] = useState<Servicio[]>([])
  const [servicio, setServicio] = useState(0)
  const [descripcion, setDescripcion] = useState('')
  const [cantidad, setCantidad] = useState('1')
  const [precio, setPrecio] = useState('')
  const [error, setError] = useState('')
  const [enviando, setEnviando] = useState(false)

  useEffect(() => {
    if (!abierto || servicios.length > 0) return
    obtenerServicios().then((r) => setServicios(r.servicios.filter((s) => !DEL_SISTEMA.has(s.id)))).catch(alError)
  }, [abierto, servicios.length, alError])

  function elegir(id: number) {
    setServicio(id)
    const s = servicios.find((x) => x.id === id)
    if (s) {
      // como Delphi: la descripcion del catalogo (sin la fecha de ejemplo del hospedaje) y su precio
      setDescripcion(s.descripcion.replace(/\s*->.*$/, ' ->').trim())
      setPrecio(s.precio ? String(s.precio) : '')
    }
  }

  const cant = Number(cantidad) || 0
  const prec = Number(precio.replace(/,/g, '')) || 0
  const importe = Math.round(cant * prec * 100) / 100

  async function enviar(ev: FormEvent) {
    ev.preventDefault()
    if (!servicio || cant < 1 || prec <= 0 || !descripcion.trim()) return
    if (!window.confirm(`¿Cargar ${cant} x ${descripcion.trim()} por ${dinero(importe)} a la habitación ${habitacion}?`)) return
    setEnviando(true)
    setError('')
    try {
      alCargar(await cargarACuenta(habitacion, servicio, descripcion, cant, Math.round(prec * 100) / 100))
      setServicio(0)
      setDescripcion('')
      setCantidad('1')
      setPrecio('')
      setAbierto(false)
    } catch (e) {
      if (e instanceof SesionVencida) alError(e)
      else setError(e instanceof Error ? e.message : String(e))
    } finally {
      setEnviando(false)
    }
  }

  if (!abierto) return <p><button type="button" onClick={() => setAbierto(true)}>Agregar cargo</button></p>

  return (
    <form className="grupo-cargo" onSubmit={enviar}>
      <label>Servicio
        <select value={servicio} onChange={(e) => elegir(Number(e.target.value))} autoFocus>
          <option value={0}>Escoja...</option>
          {servicios.map((s) => <option key={s.id} value={s.id}>{s.id} · {s.descripcion}{s.precio ? ` (${dinero(s.precio)})` : ''}</option>)}
        </select>
      </label>
      <label>Descripción<input maxLength={50} value={descripcion} onChange={(e) => setDescripcion(e.target.value)} /></label>
      <div className="tres">
        <label>Cantidad<input inputMode="numeric" value={cantidad} onChange={(e) => setCantidad(e.target.value.replace(/\D/g, '').slice(0, 3))} /></label>
        <label>Precio (sin impuestos)<input inputMode="decimal" value={precio} onChange={(e) => setPrecio(e.target.value)} /></label>
        <label>Importe<input value={dinero(importe)} readOnly tabIndex={-1} /></label>
      </div>
      <p className="tenue">El ITBIS y la propina se suman según el cliente, como los demás cargos.</p>
      {error && <p className="error" role="alert">{error}</p>}
      <div className="botones">
        <button type="button" className="secundario" onClick={() => setAbierto(false)}>Cancelar</button>
        <button type="submit" disabled={enviando || !servicio || cant < 1 || prec <= 0 || !descripcion.trim()}>
          {enviando ? 'Cargando...' : 'Cargar'}
        </button>
      </div>
    </form>
  )
}

function Detalle({ c, credito, anula, alAnular }: {
  c: CuentaHabitacion
  credito: boolean
  anula: boolean
  alAnular: (secuencia: number, descripcion: string, importe: number) => void
}) {
  const d = c.desglose
  const impuestos = [c.llevaItbis && !c.regimenEspecial ? 'ITBIS 18 %' : null, c.llevaPropina ? 'propina 10 %' : null]
    .filter(Boolean).join(' y ') || 'sin ITBIS ni propina'
  return (
    <>
      <dl className="datos-lista">
        <dt>Huésped</dt><dd>{c.huesped}</dd>
        <dt>Cliente</dt><dd>{c.cliente}{c.rnc ? ` · RNC ${c.rnc}` : ''}</dd>
        <dt>Estadía</dt><dd>{fecha(c.fechaEntrada, true)} al {fecha(c.fechaSalida, true)}</dd>
        <dt>Cuenta</dt><dd>#{c.factura}{c.reserva ? ` · reserva #${c.reserva}` : ''}{c.categoria ? ` · categoría ${c.categoria}` : ''}</dd>
        <dt>Cobra</dt><dd>{impuestos}{c.regimenEspecial ? ' (régimen especial, E44)' : ''}</dd>
        <dt>Cafetería</dt><dd>{credito ? 'Crédito habilitado' : 'Sin crédito'}</dd>
        {c.transferida && <><dt>Ojo</dt><dd className="error">Cuenta transferida a otra habitación: no recibe cargos</dd></>}
      </dl>

      <div className="tabla-marco">
        <table className="tabla">
          <thead>
            <tr><th>Fecha</th><th>Descripción</th><th className="num">Cant.</th><th className="num">Importe</th>{anula && <th></th>}</tr>
          </thead>
          <tbody>
            {c.lineas.map((l) => (
              <tr key={l.secuencia} className={!l.activo ? 'inactiva' : l.importe < 0 ? 'pago' : ''}>
                <td>{fecha(l.fecha)}</td>
                <td>{l.descripcion}{!l.activo && ' (anulada)'}</td>
                <td className="num">{l.cantidad}</td>
                <td className="num">{dinero(l.importe)}</td>
                {anula && <td>{seAnula(l) && <button type="button" className="secundario chico" onClick={() => alAnular(l.secuencia, l.descripcion, l.importe)}>Anular</button>}</td>}
              </tr>
            ))}
            {c.lineas.length === 0 && <tr><td colSpan={anula ? 5 : 4} className="tenue">Sin cargos todavía.</td></tr>}
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
      {c.saldo < 0 && <p className="tenue">Saldo a favor del huésped (pago mayor que los cargos).</p>}
      {d.saldo !== c.saldo && (
        <p className="error">El desglose ({dinero(d.saldo)}) no coincide con el saldo de la base: avisar.</p>
      )}
    </>
  )
}
