import { useCallback, useEffect, useState, type FormEvent } from 'react'
import Fondo from './Fondo'
import FotoTipo from './FotoTipo'
import { useFotosTipos } from './useFotosTipos'
import RecibosCuenta from './RecibosCuenta'
import Espera from './Espera'
import {
  anularCargo, cambiarHabitacion, cambiarSalida, cargarACuenta, dinero, FACTOR_IMPUESTOS, fecha, hacerCheckout, obtenerCuenta,
  obtenerPrecios, obtenerServicios, permisosCuenta, prepararCambio, prepararCheckout, prepararTransferencia, reenviarEcf, seAnula,
  SesionVencida, transferirCuenta, verSalida,
  type CuentaHabitacion, type EstadoEcf, type HabitacionRack, type PantallaPrecios, type PreparacionCambio, type PreparacionCheckout,
  type PreparacionTransferencia, type RespuestaCheckout, type SalidaCuenta, type Servicio,
} from './api'

/** Panel con la cuenta abierta de una habitacion: cargos, pagos y saldo; agregar un cargo y anular uno. */
export default function Cuenta({ habitacion: h, alCerrar, alVencer, alCambiar }: {
  habitacion: HabitacionRack
  alCerrar: () => void
  alVencer: () => void
  alCambiar?: () => void
}) {
  const [cuenta, setCuenta] = useState<CuentaHabitacion | null>(null)
  const fotos = useFotosTipos()
  const [anula, setAnula] = useState(false)
  const [anulaRecibos, setAnulaRecibos] = useState(false)
  const [abierto, setAbierto] = useState<'cargo' | 'pago' | 'salida' | 'cambio' | 'checkout' | null>(null)
  // despues del check-out: la factura cerrada y lo que dijo la DGII
  const [cerrada, setCerrada] = useState<RespuestaCheckout | null>(null)
  const [error, setError] = useState('')
  const [avisos, setAvisos] = useState<string[]>([])
  // despues de mover la cuenta a otra habitacion: el panel muestra el resultado (esta habitacion ya quedo libre)
  const [movida, setMovida] = useState<{ hasta: number; avisos: string[]; transferida: boolean } | null>(null)
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
          <FotoTipo nombre={h.tipo} fotos={fotos} className="foto-tipo cabeza" />
          <div className="crece">
            <h2>Habitación {h.numero}</h2>
            <p className="tenue">{h.tipo} · {h.estado}{h.comentario ? ` · ${h.comentario}` : ''}</p>
          </div>
          <button type="button" className="secundario" onClick={alCerrar} aria-label="Cerrar">Cerrar</button>
        </header>

        {cerrada && <Cerrada r={cerrada} habitacion={h.numero} alCerrar={alCerrar} alError={manejar} />}
        {!cerrada && movida && (
          <div role="status">
            <p className="ok-texto">
              {movida.transferida
                ? `La cuenta se transfirió a la habitación ${movida.hasta} y la ${h.numero} se cerró (quedó libre).`
                : `La cuenta pasó a la habitación ${movida.hasta}. La ${h.numero} quedó libre.`}
            </p>
            {movida.avisos.map((a) => <p key={a} className="alerta">{a}</p>)}
            <div className="botones"><button type="button" onClick={alCerrar}>Cerrar</button></div>
          </div>
        )}
        {!cerrada && !movida && !ocupada && <Libre h={h} />}
        {!cerrada && !movida && ocupada && error && <p className="error" role="alert">{error}</p>}
        {!cerrada && !movida && ocupada && !cuenta && !error && <p className="tenue">Cargando la cuenta...</p>}
        {!cerrada && !movida && cuenta && (
          <>
            {!cuenta.transferida && (
              <div className="acciones-cuenta">
                {/* los dos botones uno al lado del otro; el formulario que se abra va debajo */}
                {!abierto && (
                  <div className="barra-cuenta">
                    <button type="button" className="boton-cargo" onClick={() => setAbierto('cargo')}>Agregar cargo</button>
                    <button type="button" className="boton-pago" onClick={() => setAbierto('pago')}>Registrar pago</button>
                    <button type="button" className="secundario boton-mover" onClick={() => { setAvisos([]); setAbierto('cambio') }}>Cambiar habitación</button>
                    <button type="button" className="boton-checkout" onClick={() => { setAvisos([]); setAbierto('checkout') }}>Check-out</button>
                  </div>
                )}
                {abierto === 'checkout' && (
                  <CheckOut habitacion={h.numero} alError={manejar} alCerrar={() => setAbierto(null)}
                            alCerrada={(r) => { setCerrada(r); setAbierto(null); alCambiar?.() }} />
                )}
                {abierto === 'cambio' && (
                  <MoverCuenta habitacion={h.numero} alError={manejar} alCerrar={() => setAbierto(null)}
                               alCambiar={(hasta, a, transferida) => { setMovida({ hasta, avisos: a, transferida }); setAbierto(null); alCambiar?.() }} />
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
 * Check-out con e-CF (etapa 5), como Delphi: solo con el balance en 0; se escoge el comprobante y se revisa todo antes
 * de tomar el e-NCF; la cuenta se cierra y despues se envia a la DGII.
 */
function CheckOut({ habitacion, alCerrada, alCerrar, alError }: {
  habitacion: number
  alCerrada: (r: RespuestaCheckout) => void
  alCerrar: () => void
  alError: (e: unknown) => void
}) {
  const [p, setP] = useState<PreparacionCheckout | null>(null)
  const [tipo, setTipo] = useState(2)
  const [rnc, setRnc] = useState('')
  const [error, setError] = useState('')
  const [enviando, setEnviando] = useState(false)

  useEffect(() => {
    prepararCheckout(habitacion).then((r) => {
      setP(r)
      setTipo(r.sugerido)
      setRnc(r.rnc && r.rnc.replace(/0/g, '') !== '' ? r.rnc : '')
    }).catch(alError)
  }, [habitacion, alError])

  const sinRnc = rnc.trim() === '' || rnc.replace(/0/g, '').trim() === ''
  // sin RNC solo hay consumo (E32), como Delphi
  const tipos = p ? (sinRnc ? p.tipos.filter((t) => t.id === 2) : p.tipos) : []
  const tipoActual = p?.ncfActual ? null : tipos.find((t) => t.id === tipo) ?? tipos[0]

  async function enviar(ev: FormEvent) {
    ev.preventDefault()
    if (!p || p.impide) return
    const comp = p.transferida ? 'sin e-NCF (cuenta transferida)' : p.ncfActual ? `con el ${p.ncfActual}` : `con ${tipoActual?.codigo} (${tipoActual?.nombre.toLowerCase()})`
    if (!window.confirm(`¿Hacer el check-out de la habitación ${habitacion} ${comp} por ${dinero(p.total)}? Se envía a la DGII y no se puede deshacer.`)) return
    setEnviando(true)
    setError('')
    try {
      alCerrada(await hacerCheckout(habitacion, tipoActual?.id ?? tipo, rnc))
    } catch (e) {
      if (e instanceof SesionVencida) alError(e)
      else setError(e instanceof Error ? e.message : String(e))
    } finally {
      setEnviando(false)
    }
  }

  if (!p) return <p className="tenue">Preparando el check-out...</p>
  return (
    <form className="grupo-cargo" onSubmit={enviar}>
      {enviando && <Espera texto={p.transferida ? 'Haciendo el check-out, espere...' : 'Conectando con la DGII, espere...'} />}
      <dl className="datos-lista">
        <dt>Cliente</dt><dd>{p.cliente}</dd>
        <dt>Factura</dt><dd>{dinero(p.subTotal)}{p.itbis ? ` + ITBIS ${dinero(p.itbis)}` : ''}{p.propina ? ` + propina ${dinero(p.propina)}` : ''} = <strong>{dinero(p.total)}</strong></dd>
        <dt>Balance</dt><dd className={p.saldo !== 0 ? 'mal' : ''}>{dinero(p.saldo)}</dd>
      </dl>
      {p.impide && <p className="error">{p.impide}</p>}
      {!p.impide && p.transferida && <p className="alerta">Cuenta transferida: se cierra en 0, sin e-NCF y sin envío a la DGII.</p>}
      {!p.impide && !p.transferida && (
        p.ncfActual
          ? <p className="alerta">La cuenta ya tiene el {p.ncfActual}: se usa ese.</p>
          : (
            <div className="dos">
              <label>RNC / cédula / pasaporte
                <input value={rnc} maxLength={11} onChange={(e) => setRnc(e.target.value.trim())} placeholder="En blanco: consumidor final" autoFocus />
              </label>
              <label>Comprobante
                <select value={tipoActual?.id ?? ''} onChange={(e) => setTipo(Number(e.target.value))}>
                  {tipos.map((t) => <option key={t.id} value={t.id}>{t.codigo} · {t.nombre}</option>)}
                </select>
              </label>
            </div>
          )
      )}
      {!p.impide && !p.transferida && !p.ncfActual && tipoActual?.id === 2 && p.total >= 250000 && (
        <p className="alerta">Consumo de 250,000 o más: la DGII pide el RNC, la cédula o el pasaporte y el nombre del cliente.</p>
      )}
      {error && <p className="error" role="alert">{error}</p>}
      <div className="botones">
        <button type="button" className="secundario" onClick={alCerrar}>Cancelar</button>
        <button type="submit" disabled={enviando || !!p.impide}>{enviando ? 'Cerrando y enviando a la DGII...' : 'Hacer el check-out'}</button>
      </div>
    </form>
  )
}

/** Despues del check-out: el numero, lo que dijo la DGII, imprimir y reenviar si no llego. */
function Cerrada({ r, habitacion, alCerrar, alError }: {
  r: RespuestaCheckout
  habitacion: number
  alCerrar: () => void
  alError: (e: unknown) => void
}) {
  const [dgii, setDgii] = useState<EstadoEcf | null>(r.dgii)
  const [enviando, setEnviando] = useState(false)
  const [error, setError] = useState('')
  const f = r.factura

  async function reenviar() {
    setEnviando(true)
    setError('')
    try {
      setDgii(await reenviarEcf(f.numero))
    } catch (e) {
      if (e instanceof SesionVencida) alError(e)
      else setError(e instanceof Error ? e.message : String(e))
    } finally {
      setEnviando(false)
    }
  }

  return (
    <div role="status">
      {enviando && <Espera texto="Conectando con la DGII, espere..." />}
      <p className="ok-texto">Check-out hecho: la habitación {habitacion} quedó libre. Factura #{f.numero}{f.encf ? ` · ${f.encf}` : ''} · {dinero(f.total)}.</p>
      {f.transferida && <p className="tenue">Cuenta transferida: sin e-NCF ni envío a la DGII.</p>}
      {dgii && (
        <p className={dgii.aceptado ? 'ok-texto' : 'error'}>
          DGII: {dgii.estado}{dgii.mensaje ? ` · ${dgii.mensaje}` : ''}
        </p>
      )}
      {dgii && !dgii.aceptado && <p className="tenue">La factura ya está cerrada con su e-NCF: si no llegó o la rechazaron, corrija lo que diga la DGII y reenvíela.</p>}
      {error && <p className="error" role="alert">{error}</p>}
      <div className="botones">
        {dgii && !dgii.aceptado && <button type="button" className="secundario" disabled={enviando} onClick={reenviar}>{enviando ? 'Enviando...' : 'Reenviar a la DGII'}</button>}
        {!f.transferida && <button type="button" className="secundario" onClick={() => window.open(`/factura/${f.numero}`, '_blank')}>Imprimir factura</button>}
        <button type="button" onClick={alCerrar}>Cerrar</button>
      </div>
    </div>
  )
}

/** Como Delphi (Transferir huesped): a una habitacion libre o a una ocupada. */
function MoverCuenta({ habitacion, alCambiar, alCerrar, alError }: {
  habitacion: number
  alCambiar: (hasta: number, avisos: string[], transferida: boolean) => void
  alCerrar: () => void
  alError: (e: unknown) => void
}) {
  const [modo, setModo] = useState<'libre' | 'ocupada'>('libre')
  return (
    <>
      <div className="modo-mover" role="radiogroup" aria-label="A qué habitación">
        <label className="check"><input type="radio" checked={modo === 'libre'} onChange={() => setModo('libre')} /> A una libre</label>
        <label className="check"><input type="radio" checked={modo === 'ocupada'} onChange={() => setModo('ocupada')} /> A una ocupada (transferir la cuenta)</label>
      </div>
      {modo === 'libre'
        ? <CambiarHabitacion habitacion={habitacion} alError={alError} alCerrar={alCerrar} alCambiar={(h, a) => alCambiar(h, a, false)} />
        : <TransferirCuenta habitacion={habitacion} alError={alError} alCerrar={alCerrar} alCambiar={(h, a) => alCambiar(h, a, true)} />}
    </>
  )
}

/**
 * Transferir la cuenta a otra habitacion OCUPADA (decisiones del usuario 2026-10-23): los cargos pasan en una linea sin
 * impuestos (la destino les pone los de su cliente), el PAY-OUT y los pagos pasan aparte, y esta habitacion se cierra
 * en el mismo paso (sin e-NCF).
 */
function TransferirCuenta({ habitacion, alCambiar, alCerrar, alError }: {
  habitacion: number
  alCambiar: (hasta: number, avisos: string[]) => void
  alCerrar: () => void
  alError: (e: unknown) => void
}) {
  const [p, setP] = useState<PreparacionTransferencia | null>(null)
  const [hasta, setHasta] = useState(0)
  const [error, setError] = useState('')
  const [enviando, setEnviando] = useState(false)

  useEffect(() => {
    prepararTransferencia(habitacion).then(setP).catch((e) => {
      if (e instanceof SesionVencida) alError(e)
      else setError(e instanceof Error ? e.message : String(e))
    })
  }, [habitacion, alError])

  const destino = p?.ocupadas.find((o) => o.habitacion === hasta)

  async function enviar(ev: FormEvent) {
    ev.preventDefault()
    if (!p || !destino) return
    if (!window.confirm(`¿Transferir la cuenta de la ${habitacion} a la ${hasta} (${destino.huesped ?? ''})? La ${habitacion} se cierra y queda libre. No se puede deshacer.`)) return
    setEnviando(true)
    setError('')
    try {
      const r = await transferirCuenta(habitacion, hasta)
      alCambiar(r.hasta, r.avisos)
    } catch (e) {
      if (e instanceof SesionVencida) alError(e)
      else setError(e instanceof Error ? e.message : String(e))
    } finally {
      setEnviando(false)
    }
  }

  if (!p) return error ? <p className="error" role="alert">{error}</p> : <p className="tenue">Cargando las habitaciones ocupadas...</p>
  return (
    <form className="grupo-cargo" onSubmit={enviar}>
      <dl className="datos-lista">
        <dt>Cargos</dt><dd>{dinero(p.cargos)} sin impuestos <span className="tenue">(aquí con {p.impuestos})</span></dd>
        {p.payOut !== 0 && <><dt>PAY-OUT</dt><dd>{dinero(p.payOut)}</dd></>}
        {p.pagos !== 0 && <><dt>Pagos</dt><dd>{dinero(p.pagos)} (pasan como pagos)</dd></>}
        <dt>Saldo hoy</dt><dd>{dinero(p.saldo)}</dd>
      </dl>
      <label>Transferir a
        <select value={hasta || ''} onChange={(e) => setHasta(Number(e.target.value))} autoFocus>
          <option value="">Escoja la habitación ocupada...</option>
          {p.ocupadas.map((o) => <option key={o.habitacion} value={o.habitacion}>{o.habitacion} · {o.huesped ?? ''}{o.cliente && o.cliente !== o.huesped ? ` (${o.cliente})` : ''}</option>)}
        </select>
      </label>
      {destino && destino.impuestos !== p.impuestos && (
        <p className="alerta">En la {destino.habitacion} los cargos se cobran con {destino.impuestos} (su cliente); aquí eran con {p.impuestos}.</p>
      )}
      <p className="tenue">Los cargos pasan en una línea y se facturan en la otra habitación. Esta cuenta se cierra sin e-NCF.</p>
      {error && <p className="error" role="alert">{error}</p>}
      <div className="botones">
        <button type="button" className="secundario" onClick={alCerrar}>Cancelar</button>
        <button type="submit" disabled={enviando || !destino}>{enviando ? 'Transfiriendo...' : 'Transferir y cerrar'}</button>
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
