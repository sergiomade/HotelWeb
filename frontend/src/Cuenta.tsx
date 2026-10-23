import { useCallback, useEffect, useState, type FormEvent } from 'react'
import Fondo from './Fondo'
import RecibosCuenta from './RecibosCuenta'
import {
  anularCargo, cambiarHabitacion, cambiarSalida, cargarACuenta, dinero, FACTOR_IMPUESTOS, fecha, obtenerCuenta, obtenerPrecios,
  obtenerServicios, permisosCuenta, prepararCambio, seAnula, SesionVencida, verSalida,
  type CuentaHabitacion, type HabitacionRack, type PantallaPrecios, type PreparacionCambio, type SalidaCuenta, type Servicio,
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
  const [anulaRecibos, setAnulaRecibos] = useState(false)
  const [abierto, setAbierto] = useState<'cargo' | 'pago' | 'salida' | 'cambio' | null>(null)
  const [error, setError] = useState('')
  const [avisos, setAvisos] = useState<string[]>([])
  // despues de mover la cuenta a otra habitacion: el panel muestra el resultado (esta habitacion ya quedo libre)
  const [movida, setMovida] = useState<{ hasta: number; avisos: string[] } | null>(null)
  const ocupada = h.situacion === 'Ocupada'

  const manejar = useCallback((e: unknown) => {
    if (e instanceof SesionVencida) alVencer()
    else setError(e instanceof Error ? e.message : String(e))
  }, [alVencer])

  useEffect(() => {
    if (!ocupada) return
    obtenerCuenta(h.numero).then(setCuenta).catch(manejar)
    permisosCuenta().then((p) => { setAnula(p.anulaCargos); setAnulaRecibos(p.anulaRecibos) }).catch(() => setAnula(false))
  }, [h.numero, ocupada, manejar])

  useEffect(() => {
    const tecla = (e: KeyboardEvent) => e.key === 'Escape' && alCerrar()
    window.addEventListener('keydown', tecla)
    return () => window.removeEventListener('keydown', tecla)
  }, [alCerrar])

  // despues de un recibo: la cuenta se vuelve a leer (el pago lo pone el procedimiento de Delphi)
  function recargar() {
    obtenerCuenta(h.numero).then(actualizada).catch(manejar)
  }

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

        {movida && (
          <div role="status">
            <p className="ok-texto">La cuenta pasó a la habitación {movida.hasta}. La {h.numero} quedó libre.</p>
            {movida.avisos.map((a) => <p key={a} className="alerta">{a}</p>)}
            <div className="botones"><button type="button" onClick={alCerrar}>Cerrar</button></div>
          </div>
        )}
        {!movida && !ocupada && <Libre h={h} />}
        {!movida && ocupada && error && <p className="error" role="alert">{error}</p>}
        {!movida && ocupada && !cuenta && !error && <p className="tenue">Cargando la cuenta...</p>}
        {!movida && cuenta && (
          <>
            {!cuenta.transferida && (
              <div className="acciones-cuenta">
                {/* los dos botones uno al lado del otro; el formulario que se abra va debajo */}
                {!abierto && (
                  <div className="barra-cuenta">
                    <button type="button" className="boton-cargo" onClick={() => setAbierto('cargo')}>Agregar cargo</button>
                    <button type="button" className="boton-pago" onClick={() => setAbierto('pago')}>Registrar pago</button>
                    <button type="button" className="secundario boton-mover" onClick={() => { setAvisos([]); setAbierto('cambio') }}>Cambiar habitación</button>
                  </div>
                )}
                {abierto === 'cambio' && (
                  <CambiarHabitacion habitacion={h.numero} alError={manejar} alCerrar={() => setAbierto(null)}
                                     alCambiar={(hasta, a) => { setMovida({ hasta, avisos: a }); setAbierto(null); alCambiar?.() }} />
                )}
                {abierto === 'cargo' && (
                  <AgregarCargo habitacion={h.numero} alCargar={(c) => { actualizada(c); setAbierto(null) }}
                                alCerrar={() => setAbierto(null)} alError={manejar} />
                )}
                {abierto === 'salida' && (
                  <CambiarSalida habitacion={h.numero} alError={manejar} alCerrar={() => setAbierto(null)}
                                 alCambiar={(a) => { setAvisos(a); setAbierto(null); recargar() }} />
                )}
                {avisos.map((a) => <p key={a} className="alerta">{a}</p>)}
                <RecibosCuenta habitacion={h.numero} saldo={cuenta.saldo} anula={anulaRecibos} abierto={abierto === 'pago'}
                               alCerrar={() => setAbierto(null)} alCambiar={recargar} alError={manejar} />
              </div>
            )}
            <Detalle c={cuenta} credito={h.credito} anula={anula} alAnular={anular}
                     alCambiarSalida={!cuenta.transferida && !abierto ? () => { setAvisos([]); setAbierto('salida') } : undefined} />
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
function AgregarCargo({ habitacion, alCargar, alCerrar, alError }: {
  habitacion: number
  alCargar: (c: CuentaHabitacion) => void
  alCerrar: () => void
  alError: (e: unknown) => void
}) {
  const [servicios, setServicios] = useState<Servicio[]>([])
  const [servicio, setServicio] = useState(0)
  const [descripcion, setDescripcion] = useState('')
  const [cantidad, setCantidad] = useState('1')
  const [precio, setPrecio] = useState('')
  const [error, setError] = useState('')
  const [enviando, setEnviando] = useState(false)

  useEffect(() => {
    if (servicios.length > 0) return
    obtenerServicios().then((r) => setServicios(r.servicios.filter((s) => !DEL_SISTEMA.has(s.id)))).catch(alError)
  }, [servicios.length, alError])

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
    } catch (e) {
      if (e instanceof SesionVencida) alError(e)
      else setError(e instanceof Error ? e.message : String(e))
    } finally {
      setEnviando(false)
    }
  }

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
        <button type="button" className="secundario" onClick={alCerrar}>Cancelar</button>
        <button type="submit" disabled={enviando || !servicio || cant < 1 || prec <= 0 || !descripcion.trim()}>
          {enviando ? 'Cargando...' : 'Cargar'}
        </button>
      </div>
    </form>
  )
}

/**
 * Cambiar la fecha de salida de la habitacion (Delphi no lo tiene). Cada cambio queda guardado por habitacion; si
 * todas las de la reserva quedan con la misma fecha, la reserva tambien cambia.
 */
function CambiarSalida({ habitacion, alCambiar, alCerrar, alError }: {
  habitacion: number
  alCambiar: (avisos: string[]) => void
  alCerrar: () => void
  alError: (e: unknown) => void
}) {
  const [s, setS] = useState<SalidaCuenta | null>(null)
  const [nueva, setNueva] = useState('')
  const [todas, setTodas] = useState(false)
  const [comentario, setComentario] = useState('')
  const [error, setError] = useState('')
  const [enviando, setEnviando] = useState(false)

  useEffect(() => {
    verSalida(habitacion).then((r) => { setS(r); setNueva(r.salida.slice(0, 10)) }).catch(alError)
  }, [habitacion, alError])

  const hoy = new Date()
  const hoyIso = `${hoy.getFullYear()}-${String(hoy.getMonth() + 1).padStart(2, '0')}-${String(hoy.getDate()).padStart(2, '0')}`
  const minimo = s ? (s.entrada.slice(0, 10) >= hoyIso ? sumarDia(s.entrada.slice(0, 10)) : hoyIso) : hoyIso
  const igual = s ? nueva === s.salida.slice(0, 10) : true

  async function enviar(ev: FormEvent) {
    ev.preventDefault()
    if (!s || !nueva || igual) return
    const cuales = todas && s.otras.length ? `las habitaciones ${[habitacion, ...s.otras.map((o) => o.habitacion)].join(', ')}` : `la habitación ${habitacion}`
    if (!window.confirm(`¿Cambiar la salida de ${cuales} al ${fecha(nueva, true)}?`)) return
    setEnviando(true)
    setError('')
    try {
      const r = await cambiarSalida(habitacion, nueva, todas, comentario)
      alCambiar([
        `Salida cambiada al ${fecha(nueva, true)}${r.cambioLaReserva ? ' (también en la reserva)' : ' (solo esta habitación; la reserva sigue con la de las demás)'}.`,
        ...r.avisos,
      ])
    } catch (e) {
      if (e instanceof SesionVencida) alError(e)
      else setError(e instanceof Error ? e.message : String(e))
    } finally {
      setEnviando(false)
    }
  }

  if (!s) return <p className="tenue">Cargando la salida...</p>
  return (
    <form className="grupo-cargo" onSubmit={enviar}>
      <p>Entró el {fecha(s.entrada, true)} · sale el <strong>{fecha(s.salida, true)}</strong>
        {s.salidaReserva && s.salidaReserva.slice(0, 10) !== s.salida.slice(0, 10) && <span className="tenue"> (la reserva dice {fecha(s.salidaReserva, true)})</span>}</p>
      <div className="dos">
        <label>Nueva salida<input type="date" value={nueva} min={minimo} onChange={(e) => setNueva(e.target.value)} autoFocus /></label>
        <label>Comentario<input maxLength={200} value={comentario} onChange={(e) => setComentario(e.target.value)} placeholder="Opcional" /></label>
      </div>
      {s.otras.length > 0 && (
        <label className="check">
          <input type="checkbox" checked={todas} onChange={(e) => setTodas(e.target.checked)} />
          También las otras habitaciones de la reserva: {s.otras.map((o) => `${o.habitacion} (sale ${fecha(o.salida)})`).join(', ')}
        </label>
      )}
      {s.cambios.length > 0 && (
        <details>
          <summary className="tenue">Cambios anteriores ({s.cambios.length})</summary>
          <ul className="tenue">
            {s.cambios.map((c, i) => (
              <li key={i}>{fecha(c.fecha, true)}: {c.anterior ? fecha(c.anterior, true) : '—'} → {fecha(c.nueva, true)} · {c.usuario}{c.comentario ? ` · ${c.comentario}` : ''}</li>
            ))}
          </ul>
        </details>
      )}
      {error && <p className="error" role="alert">{error}</p>}
      <div className="botones">
        <button type="button" className="secundario" onClick={alCerrar}>Cancelar</button>
        <button type="submit" disabled={enviando || !nueva || igual}>{enviando ? 'Guardando...' : 'Cambiar salida'}</button>
      </div>
    </form>
  )
}

/**
 * Mover la cuenta a una habitacion LIBRE. Como el check-in (decision del usuario): si es de otro tipo se cobra su
 * precio desde la proxima noche; si es mas cara se puede respetar el precio de ahora. Las noches cargadas no cambian.
 */
function CambiarHabitacion({ habitacion, alCambiar, alCerrar, alError }: {
  habitacion: number
  alCambiar: (hasta: number, avisos: string[]) => void
  alCerrar: () => void
  alError: (e: unknown) => void
}) {
  const [p, setP] = useState<PreparacionCambio | null>(null)
  const [tabla, setTabla] = useState<PantallaPrecios['tabla'] | null>(null)
  const [hasta, setHasta] = useState(0)
  const [categoria, setCategoria] = useState('A')
  const [tarifa, setTarifa] = useState<number | null>(null)
  const [respetar, setRespetar] = useState(false)
  const [error, setError] = useState('')
  const [enviando, setEnviando] = useState(false)

  useEffect(() => {
    prepararCambio(habitacion).then((r) => { setP(r); setCategoria(r.categoria || 'A'); setTarifa(r.tarifa) }).catch(alError)
    obtenerPrecios().then((x) => setTabla(x.tabla)).catch(alError)
  }, [habitacion, alError])

  const destino = p?.libres.find((x) => x.numero === hasta)
  const tipo = destino?.tipo ?? p?.tipo

  /** La tarifa: la de ahora si no cambia el tipo ni la categoria; si cambia, la de la tabla de precios. */
  function tarifaPara(t: number | undefined, cat: string): number | null {
    if (!p) return null
    if (t === p.tipo && cat === (p.categoria || 'A')) return p.tarifa
    return tabla?.precios.find((x) => x.tipo === t && x.categoria === cat)?.sinImpuestos ?? null
  }

  // respetar el precio solo protege al cliente: si la nueva (en su categoria de ahora) cuesta MAS que lo que paga
  const masCara = !!p && !!destino && destino.tipo !== p.tipo && (() => {
    const n = tarifaPara(destino.tipo, p.categoria || 'A')
    return n == null || n > p.tarifa
  })()

  function elegir(n: number) {
    setHasta(n)
    const t = p?.libres.find((x) => x.numero === n)?.tipo
    if (respetar && p && t !== p.tipo) {
      const nn = tarifaPara(t, p.categoria || 'A')
      if (nn == null || nn > p.tarifa) return   // sigue respetando
    }
    setRespetar(false)
    setTarifa(tarifaPara(t, categoria))
  }

  function respetarAhora(si: boolean) {
    setRespetar(si)
    if (!p) return
    const cat = p.categoria || 'A'
    setCategoria(cat)
    setTarifa(si ? p.tarifa : tarifaPara(tipo, cat))
  }

  async function enviar(ev: FormEvent) {
    ev.preventDefault()
    if (!p || !hasta || tarifa == null) return
    const cobro = p.tieneLinea ? ` Desde la próxima noche se cobra ${dinero(tarifa)} sin impuestos.` : ''
    if (!window.confirm(`¿Pasar la cuenta de la habitación ${habitacion} a la ${hasta}?${cobro}`)) return
    setEnviando(true)
    setError('')
    try {
      const r = await cambiarHabitacion(habitacion, hasta, categoria, p.tieneLinea ? tarifa : null)
      alCambiar(r.hasta, r.avisos)
    } catch (e) {
      if (e instanceof SesionVencida) alError(e)
      else setError(e instanceof Error ? e.message : String(e))
    } finally {
      setEnviando(false)
    }
  }

  if (!p) return <p className="tenue">Cargando las habitaciones libres...</p>
  const libres = p.libres.filter((x) => x.disponible)
  return (
    <form className="grupo-cargo" onSubmit={enviar}>
      <p>Ahora: {p.tipoNombre}{p.tieneLinea ? ` · categoría ${p.categoria} · ${dinero(p.tarifa)} la noche (sin impuestos)` : ''}</p>
      <div className="tres">
        <label>Pasar a
          <select value={hasta || ''} onChange={(e) => elegir(Number(e.target.value))} autoFocus>
            <option value="">Escoja...</option>
            <optgroup label={`Libres ${p.tipoNombre}`}>
              {libres.filter((x) => x.tipo === p.tipo).map((x) => <option key={x.numero} value={x.numero}>{x.numero}</option>)}
            </optgroup>
            <optgroup label="Otras libres">
              {libres.filter((x) => x.tipo !== p.tipo).map((x) => <option key={x.numero} value={x.numero}>{x.numero} · {x.tipoNombre}</option>)}
            </optgroup>
          </select>
        </label>
        <label>Categoría
          <select value={categoria} disabled={respetar || !p.tieneLinea}
                  onChange={(e) => { setCategoria(e.target.value); setTarifa(tarifaPara(tipo, e.target.value)) }}>
            {(tabla?.categorias ?? [{ id: categoria, descripcion: categoria }]).map((c) => <option key={c.id} value={c.id}>{c.id}</option>)}
          </select>
        </label>
        <label>Tarifa (sin impuestos)
          <input inputMode="decimal" value={tarifa ?? ''} disabled={respetar || !p.tieneLinea}
                 title={tarifa != null ? `Con ITBIS y propina: ${dinero(Math.round(tarifa * FACTOR_IMPUESTOS * 100) / 100)}` : ''}
                 onChange={(e) => {
                   const v = e.target.value.replace(/,/g, '').trim()
                   setTarifa(v === '' || isNaN(Number(v)) ? null : Number(v))
                 }} />
        </label>
      </div>
      {destino && destino.tipo !== p.tipo && p.tieneLinea && (
        <div className="aviso-tipo">
          <span className="alerta">Es {destino.tipoNombre}: {respetar ? `se le respeta el precio de ahora (${dinero(p.tarifa)}).` : 'se cobra su precio desde la próxima noche.'}</span>
          {masCara && (
            <label className="check" title="La habitacion cuesta mas: el hotel le mantiene al cliente el precio de ahora">
              <input type="checkbox" checked={respetar} onChange={(e) => respetarAhora(e.target.checked)} />
              Respetar precio de ahora
            </label>
          )}
        </div>
      )}
      {!p.tieneLinea && destino && destino.tipo !== p.tipo && <p className="alerta">La cuenta no tiene línea de reserva: solo cambia el número, el precio sigue igual.</p>}
      {p.tieneLinea && hasta > 0 && tarifa == null && <p className="alerta">Sin precio en esta categoría: escriba la tarifa.</p>}
      <p className="tenue">Las noches ya cargadas no cambian. Los pagos y recibos pasan con la cuenta.</p>
      {error && <p className="error" role="alert">{error}</p>}
      <div className="botones">
        <button type="button" className="secundario" onClick={alCerrar}>Cancelar</button>
        <button type="submit" disabled={enviando || !hasta || (p.tieneLinea && tarifa == null)}>{enviando ? 'Cambiando...' : 'Cambiar habitación'}</button>
      </div>
    </form>
  )
}

function sumarDia(iso: string): string {
  const d = new Date(`${iso}T12:00:00`)
  d.setDate(d.getDate() + 1)
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

function Detalle({ c, credito, anula, alAnular, alCambiarSalida }: {
  c: CuentaHabitacion
  credito: boolean
  anula: boolean
  alAnular: (secuencia: number, descripcion: string, importe: number) => void
  alCambiarSalida?: () => void
}) {
  const d = c.desglose
  const impuestos = [c.llevaItbis && !c.regimenEspecial ? 'ITBIS 18 %' : null, c.llevaPropina ? 'propina 10 %' : null]
    .filter(Boolean).join(' y ') || 'sin ITBIS ni propina'
  return (
    <>
      <dl className="datos-lista">
        <dt>Huésped</dt><dd>{c.huesped}</dd>
        <dt>Cliente</dt><dd>{c.cliente}{c.rnc ? ` · RNC ${c.rnc}` : ''}</dd>
        <dt>Estadía</dt><dd>{fecha(c.fechaEntrada, true)} al {fecha(c.fechaSalida, true)}
          {alCambiarSalida && <> <button type="button" className="secundario chico" onClick={alCambiarSalida}>Cambiar salida</button></>}</dd>
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
