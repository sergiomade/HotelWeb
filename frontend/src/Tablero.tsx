import { useCallback, useEffect, useState } from 'react'
import { Asignar } from './CheckIn'
import Cuenta from './Cuenta'
import ReservaEditor from './ReservaEditor'
import {
  buscarReservas, dinero, fecha, hoyIso, llegadasHoy, obtenerDiagnostico, obtenerRack, reservasHechas, SesionVencida,
  type Diagnostico, type EstadoBase, type HabitacionRack, type LlegadaHoy, type RackHoy, type ReservaResumen,
} from './api'

type Actividad = 'llegadas' | 'salidas' | 'hospedados' | 'continuan' | 'reservas' | 'canceladas'

interface Datos {
  rack: RackHoy
  llegadas: LlegadaHoy[]
  entranHoy: ReservaResumen[]
  hechas: ReservaResumen[]
}

const HOSPEDADO = 3
const CANCELADA = 2

/**
 * El tablero del dia: la fecha, la ocupacion y la actividad de hoy (llegadas, salidas, hospedados, los que siguen,
 * las reservas que se hicieron y las canceladas). El check-in y la cuenta se abren aqui mismo. Abajo, el sistema.
 */
export default function Tablero({ alVencer }: { alVencer: () => void }) {
  const [d, setD] = useState<Datos | null>(null)
  const [error, setError] = useState('')
  const [cargando, setCargando] = useState(false)
  const [actividad, setActividad] = useState<Actividad>('llegadas')
  const [checkIn, setCheckIn] = useState<number | null>(null)
  const [cuenta, setCuenta] = useState<HabitacionRack | null>(null)
  const [reserva, setReserva] = useState<number | null>(null)
  const [aviso, setAviso] = useState<{ texto: string; avisos: string[] } | null>(null)

  const manejar = useCallback(
    (e: unknown) => (e instanceof SesionVencida ? alVencer() : setError(e instanceof Error ? e.message : String(e))),
    [alVencer],
  )
  const cargar = useCallback(() => {
    const hoy = hoyIso()
    setCargando(true)
    Promise.all([obtenerRack(), llegadasHoy(), buscarReservas(hoy, hoy, false, '', 0), reservasHechas(hoy)])
      .then(([rack, llegadas, entranHoy, hechas]) => { setD({ rack, llegadas, entranHoy, hechas }); setError('') })
      .catch(manejar)
      .finally(() => setCargando(false))
  }, [manejar])
  useEffect(cargar, [cargar])

  if (!d) return error ? <p className="error">{error}</p> : <p className="tenue">Cargando el tablero...</p>

  const hoy = new Date(d.rack.hoy)
  const habs = d.rack.habitaciones
  const ocupadas = habs.filter((h) => h.situacion === 'Ocupada')
  const hospedados = ocupadas.filter((h) => !h.transferida)
  const bloqueadas = habs.filter((h) => h.situacion === 'Bloqueada').length
  const llegaHoy = habs.filter((h) => h.situacion === 'LlegaHoy').length
  const libres = habs.filter((h) => h.situacion === 'Libre').length + llegaHoy
  const usables = habs.length - bloqueadas
  const ocupacion = usables > 0 ? (ocupadas.length / usables) * 100 : 0
  const salidas = hospedados.filter((h) => h.avisos.saleHoy || h.avisos.salidaVencida)
  const continuan = hospedados.filter((h) => !h.avisos.saleHoy && !h.avisos.salidaVencida)
  const llegaron = d.entranHoy.filter((r) => r.estado === HOSPEDADO)
  const canceladas = d.entranHoy.filter((r) => r.estado === CANCELADA)
  const personas = hospedados.reduce((s, h) => s + (h.personas ?? 0), 0)
  const habitacionesPorLlegar = d.llegadas.reduce((s, l) => s + l.habitaciones, 0)

  const tarjetas: { id: Actividad; n: number; nombre: string; extra?: string }[] = [
    { id: 'llegadas', n: d.llegadas.length, nombre: 'Llegadas', extra: llegaron.length ? `${llegaron.length} ya llegaron` : undefined },
    { id: 'salidas', n: salidas.length, nombre: 'Salidas', extra: salidas.some((h) => h.avisos.salidaVencida) ? 'con vencidas' : undefined },
    { id: 'hospedados', n: hospedados.length, nombre: 'Hospedados', extra: personas ? `${personas} personas` : undefined },
    { id: 'continuan', n: continuan.length, nombre: 'Continúan' },
    { id: 'reservas', n: d.hechas.length, nombre: 'Reservas de hoy' },
    { id: 'canceladas', n: canceladas.length, nombre: 'Canceladas' },
  ]

  return (
    <section className="tablero">
      <div className="tablero-cabeza">
        <h2>Tablero</h2>
        <button type="button" className="secundario" onClick={cargar} disabled={cargando}>{cargando ? 'Actualizando...' : 'Actualizar'}</button>
      </div>
      {error && <p className="error" role="alert">{error}</p>}
      {aviso && <div role="status"><p className="ok-texto">{aviso.texto}</p>{aviso.avisos.map((a) => <p key={a} className="alerta">{a}</p>)}</div>}

      <div className="tablero-cuerpo">
        <aside className="tablero-lado">
          <div className="tablero-caja tablero-fecha">
            <span>{hoy.toLocaleDateString('es-DO', { weekday: 'long' })}</span>
            <strong>{hoy.getDate()}</strong>
            <span>{hoy.toLocaleDateString('es-DO', { month: 'long', year: 'numeric' })}</span>
          </div>
          <div className="tablero-caja">
            <Dona porciento={ocupacion} />
            <dl className="tablero-cifras">
              <dt>Libres</dt><dd>{libres}</dd>
              <dt>Ocupadas</dt><dd>{ocupadas.length}</dd>
              <dt>Por llegar hoy</dt><dd title="Habitaciones de las reservas que faltan por entrar (incluye atrasadas)">{habitacionesPorLlegar}</dd>
              <dt>Fuera de servicio</dt><dd>{bloqueadas}</dd>
              <dt>Total</dt><dd>{habs.length}</dd>
            </dl>
            {habitacionesPorLlegar > libres && <p className="alerta">Faltan habitaciones libres para las llegadas pendientes.</p>}
          </div>
        </aside>

        <div className="tablero-caja tablero-actividad">
          <h3>Actividad de hoy</h3>
          <div className="tablero-tarjetas" role="tablist">
            {tarjetas.map((t) => (
              <button key={t.id} type="button" role="tab" aria-selected={actividad === t.id}
                      className={actividad === t.id ? 'activo' : ''} onClick={() => setActividad(t.id)}>
                <strong>{t.n}</strong>
                <span>{t.nombre}</span>
                {t.extra && <small>{t.extra}</small>}
              </button>
            ))}
          </div>

          <div className="tabla-marco">
            {actividad === 'llegadas' && (
              <table className="tabla">
                <thead><tr><th>Huésped</th><th>Habitaciones</th><th>Estadía</th><th className="num">Adelantos</th><th></th></tr></thead>
                <tbody>
                  {d.llegadas.map((l) => (
                    <tr key={l.reserva}>
                      <td><strong>{l.huesped || l.cliente}</strong><span className="fila-sub">Reserva {l.reserva}{l.huesped && l.huesped !== l.cliente ? ` · ${l.cliente}` : ''}</span></td>
                      <td>{l.detalle}</td>
                      <td className={l.atrasada ? 'mal' : ''}>{fecha(l.entrada)} – {fecha(l.salida)}{l.atrasada ? ' (atrasada)' : ''}</td>
                      <td className="num">{l.adelantos ? dinero(l.adelantos) : ''}</td>
                      <td className="acciones-fila">
                        <button type="button" onClick={() => setCheckIn(l.reserva)}>Check-in</button>
                        <button type="button" className="secundario chico" onClick={() => setReserva(l.reserva)}>Reserva</button>
                      </td>
                    </tr>
                  ))}
                  {llegaron.map((r) => (
                    <tr key={`h${r.id}`} className="llego">
                      <td><strong>{r.huesped || r.cliente}</strong><span className="fila-sub">Reserva {r.id}</span></td>
                      <td>{r.habitaciones}</td>
                      <td>{fecha(r.entrada)} – {fecha(r.salida)}</td>
                      <td className="num">{r.adelantos ? dinero(r.adelantos) : ''}</td>
                      <td className="ok-texto">Ya llegó</td>
                    </tr>
                  ))}
                  {d.llegadas.length + llegaron.length === 0 && <tr><td colSpan={5} className="tenue">No hay llegadas para hoy.</td></tr>}
                </tbody>
              </table>
            )}
            {(actividad === 'salidas' || actividad === 'hospedados' || actividad === 'continuan') && (
              <Hospedados lista={actividad === 'salidas' ? salidas : actividad === 'hospedados' ? hospedados : continuan}
                          vacio={actividad === 'salidas' ? 'No hay salidas para hoy.' : 'No hay habitaciones ocupadas.'}
                          alAbrir={setCuenta} />
            )}
            {(actividad === 'reservas' || actividad === 'canceladas') && (
              <Reservas lista={actividad === 'reservas' ? d.hechas : canceladas}
                        vacio={actividad === 'reservas' ? 'Hoy no se han hecho reservas.' : 'No hay reservas canceladas que entraban hoy.'}
                        alAbrir={setReserva} />
            )}
          </div>
          {actividad === 'canceladas' && <p className="tenue">Las reservas que entraban hoy y se cancelaron.</p>}
        </div>
      </div>

      <Sistema alVencer={alVencer} />

      {checkIn && (
        <Asignar reserva={checkIn} alVencer={alVencer}
                 alCerrar={(r) => { setCheckIn(null); if (r) setAviso(r); cargar() }} />
      )}
      {cuenta && <Cuenta habitacion={cuenta} alCerrar={() => { setCuenta(null); cargar() }} alVencer={alVencer} alCambiar={cargar} />}
      {reserva && <ReservaEditor id={reserva} alVencer={alVencer} alCerrar={() => { setReserva(null); cargar() }} />}
    </section>
  )
}

function Hospedados({ lista, vacio, alAbrir }: { lista: HabitacionRack[]; vacio: string; alAbrir: (h: HabitacionRack) => void }) {
  return (
    <table className="tabla">
      <thead><tr><th>Huésped</th><th>Habitación</th><th>Estadía</th><th className="num">Personas</th><th className="num">Saldo</th><th></th></tr></thead>
      <tbody>
        {lista.map((h) => (
          <tr key={h.numero}>
            <td><strong>{h.huesped}</strong>{h.cliente && h.cliente !== h.huesped && <span className="fila-sub">{h.cliente}</span>}</td>
            <td>{h.numero} · {h.tipo}</td>
            <td className={h.avisos.salidaVencida ? 'mal' : ''}>
              {fecha(h.fechaEntrada)} – {fecha(h.fechaSalida)}
              {h.avisos.saleHoy ? ' (sale hoy)' : h.avisos.salidaVencida ? ' (salida vencida)' : ''}
            </td>
            <td className="num">{h.personas ?? ''}</td>
            <td className="num">{dinero(h.saldo)}</td>
            <td><button type="button" className="secundario chico" onClick={() => alAbrir(h)}>Cuenta</button></td>
          </tr>
        ))}
        {lista.length === 0 && <tr><td colSpan={6} className="tenue">{vacio}</td></tr>}
      </tbody>
    </table>
  )
}

function Reservas({ lista, vacio, alAbrir }: { lista: ReservaResumen[]; vacio: string; alAbrir: (id: number) => void }) {
  return (
    <table className="tabla">
      <thead><tr><th>Cliente</th><th>Habitaciones</th><th>Estadía</th><th>Estado</th><th className="num">Adelantos</th><th></th></tr></thead>
      <tbody>
        {lista.map((r) => (
          <tr key={r.id}>
            <td><strong>{r.huesped || r.cliente}</strong><span className="fila-sub">Reserva {r.id}{r.telefono ? ` · Tel. ${r.telefono}` : ''}</span></td>
            <td>{r.habitaciones}</td>
            <td>{fecha(r.entrada)} – {fecha(r.salida)} ({r.noches} {r.noches === 1 ? 'noche' : 'noches'})</td>
            <td>{r.estadoNombre}</td>
            <td className="num">{r.adelantos ? dinero(r.adelantos) : ''}</td>
            <td><button type="button" className="secundario chico" onClick={() => alAbrir(r.id)}>Ver</button></td>
          </tr>
        ))}
        {lista.length === 0 && <tr><td colSpan={6} className="tenue">{vacio}</td></tr>}
      </tbody>
    </table>
  )
}

/** Anillo de ocupacion (SVG): la parte ocupada en el color de las ocupadas del rack. */
function Dona({ porciento }: { porciento: number }) {
  const r = 52
  const largo = 2 * Math.PI * r
  const lleno = Math.max(0, Math.min(100, porciento)) / 100 * largo
  return (
    <div className="dona">
      <svg viewBox="0 0 120 120" role="img" aria-label={`Ocupación ${porciento.toFixed(1)} %`}>
        <circle cx="60" cy="60" r={r} className="dona-fondo" />
        <circle cx="60" cy="60" r={r} className="dona-lleno" strokeDasharray={`${lleno} ${largo}`} transform="rotate(-90 60 60)" />
      </svg>
      <div className="dona-texto"><strong>{porciento.toFixed(1)}%</strong><span>Ocupación</span></div>
    </div>
  )
}

/** Lo que antes era la pestana Sistema: version y conexion a las dos bases. */
function Sistema({ alVencer }: { alVencer: () => void }) {
  const [d, setD] = useState<Diagnostico | null>(null)
  const [error, setError] = useState('')
  useEffect(() => {
    obtenerDiagnostico()
      .then(setD)
      .catch((e) => (e instanceof SesionVencida ? alVencer() : setError(e instanceof Error ? e.message : String(e))))
  }, [alVencer])

  return (
    <details className="tablero-sistema">
      <summary>Sistema{d ? ` · ${d.empresa} · RNC ${d.rnc} · versión ${d.version}` : ''}
        {d && (!d.hotel.conecta || !d.seguridad.conecta) && <span className="mal"> · problema de conexión</span>}</summary>
      {error && <p className="error">{error}</p>}
      {d && (
        <ul className="bases">
          <Base titulo="Base del hotel" b={d.hotel} />
          <Base titulo="Usuarios" b={d.seguridad} />
        </ul>
      )}
    </details>
  )
}

function Base({ titulo, b }: { titulo: string; b: EstadoBase }) {
  return (
    <li className={b.conecta ? 'ok' : 'mal'}>
      <span>{b.conecta ? 'Conectada' : 'Sin conexion'}</span> {titulo}: {b.base} en {b.servidor}
      {b.error && <div className="error">{b.error}</div>}
    </li>
  )
}
