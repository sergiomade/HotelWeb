import { useCallback, useEffect, useMemo, useState } from 'react'
import { dinero, fecha, obtenerRack, SesionVencida, type HabitacionRack, type RackHoy, type Situacion } from './api'
import Cuenta from './Cuenta'

const NOMBRES: Record<Situacion, string> = {
  Libre: 'Libre',
  Ocupada: 'Ocupada',
  LlegaHoy: 'Llega hoy',
  Bloqueada: 'Bloqueada',
}
const ORDEN: Situacion[] = ['Ocupada', 'LlegaHoy', 'Libre', 'Bloqueada']
const CADA = 60_000 // se refresca solo cada minuto (Delphi y la web cambian la misma base)

type Vista = 'plano' | 'lista'

function vistaInicial(): Vista {
  try {
    const v = localStorage.getItem('hotelweb.rack.vista')
    if (v === 'plano' || v === 'lista') return v
  } catch {
    // sin almacenamiento: se decide por el ancho
  }
  return window.innerWidth >= 760 ? 'plano' : 'lista'
}

/** Rack de habitaciones (etapa 1): solo lectura. */
export default function Rack({ alVencer }: { alVencer: () => void }) {
  const [rack, setRack] = useState<RackHoy | null>(null)
  const [error, setError] = useState('')
  const [vista, setVista] = useState<Vista>(vistaInicial)
  const [filtro, setFiltro] = useState<Situacion | null>(null)
  const [abierta, setAbierta] = useState<HabitacionRack | null>(null)
  const [cargando, setCargando] = useState(false)

  const cargar = useCallback(() => {
    setCargando(true)
    obtenerRack()
      .then((r) => {
        setRack(r)
        setError('')
      })
      .catch((e) => (e instanceof SesionVencida ? alVencer() : setError(e instanceof Error ? e.message : String(e))))
      .finally(() => setCargando(false))
  }, [alVencer])

  useEffect(() => {
    cargar()
    const t = setInterval(cargar, CADA)
    return () => clearInterval(t)
  }, [cargar])

  function cambiarVista(v: Vista) {
    setVista(v)
    try {
      localStorage.setItem('hotelweb.rack.vista', v)
    } catch {
      // no importa
    }
  }

  const conteo = useMemo(() => {
    const c: Record<Situacion, number> = { Libre: 0, Ocupada: 0, LlegaHoy: 0, Bloqueada: 0 }
    rack?.habitaciones.forEach((h) => c[h.situacion]++)
    return c
  }, [rack])

  if (error && !rack) return <p className="error">{error}</p>
  if (!rack) return <p className="tenue">Cargando el rack...</p>

  const salenHoy = rack.habitaciones.filter((h) => h.avisos.saleHoy).length
  const vencidas = rack.habitaciones.filter((h) => h.avisos.salidaVencida).length

  return (
    <section className="rack">
      <div className="rack-cabecera">
        <h2>Habitaciones</h2>
        <div className="rack-acciones">
          <div className="segmentado" role="group" aria-label="Vista">
            <button type="button" className={vista === 'plano' ? 'activo' : ''} onClick={() => cambiarVista('plano')}>Plano</button>
            <button type="button" className={vista === 'lista' ? 'activo' : ''} onClick={() => cambiarVista('lista')}>Lista</button>
          </div>
          <button type="button" className="secundario" onClick={cargar} disabled={cargando}>
            {cargando ? 'Actualizando...' : 'Actualizar'}
          </button>
        </div>
      </div>

      <div className="leyenda">
        {ORDEN.map((s) => (
          <button key={s} type="button" className={`chip s-${s}${filtro === s ? ' activo' : ''}`}
                  onClick={() => setFiltro(filtro === s ? null : s)} aria-pressed={filtro === s}>
            <i /> {NOMBRES[s]} <strong>{conteo[s]}</strong>
          </button>
        ))}
        {salenHoy > 0 && <span className="aviso-texto">{salenHoy} salen hoy</span>}
        {vencidas > 0 && <span className="aviso-texto mal">{vencidas} con la salida vencida</span>}
      </div>
      {error && <p className="error">No se pudo actualizar: {error}</p>}

      {vista === 'plano'
        ? <Plano habitaciones={rack.habitaciones} filtro={filtro} alAbrir={setAbierta} />
        : <Lista habitaciones={rack.habitaciones} filtro={filtro} alAbrir={setAbierta} />}

      <p className="tenue pie">
        Actualizado {new Date(rack.hoy).toLocaleTimeString('es-DO', { hour: '2-digit', minute: '2-digit' })} · se refresca cada minuto
      </p>

      {abierta && <Cuenta habitacion={abierta} alCerrar={() => setAbierta(null)} alVencer={alVencer} alCambiar={cargar} />}
    </section>
  )
}

interface PropsVista {
  habitaciones: HabitacionRack[]
  filtro: Situacion | null
  alAbrir: (h: HabitacionRack) => void
}

/** Las habitaciones en la posicion del plano de Delphi (hotelHabitacion.x / .y). */
function Plano({ habitaciones, filtro, alAbrir }: PropsVista) {
  const ANCHO = 80 // tamano de una habitacion en las coordenadas de Delphi (se separan cada 83)
  const minX = Math.min(...habitaciones.map((h) => h.x))
  const minY = Math.min(...habitaciones.map((h) => h.y))
  const w = Math.max(...habitaciones.map((h) => h.x)) - minX + ANCHO
  const alto = Math.max(...habitaciones.map((h) => h.y)) - minY + ANCHO
  return (
    <div className="plano-marco">
      <div className="plano" style={{ aspectRatio: `${w} / ${alto}` }}>
        {habitaciones.map((h) => (
          <Ficha key={h.numero} h={h} apagada={filtro !== null && filtro !== h.situacion} alAbrir={alAbrir}
                 estilo={{
                   left: `${((h.x - minX) / w) * 100}%`,
                   top: `${((h.y - minY) / alto) * 100}%`,
                   width: `${(ANCHO / w) * 100}%`,
                   height: `${(ANCHO / alto) * 100}%`,
                 }} />
        ))}
      </div>
    </div>
  )
}

/** Por piso, en cuadricula (para celular o pantallas angostas). */
function Lista({ habitaciones, filtro, alAbrir }: PropsVista) {
  const pisos = [...new Set(habitaciones.map((h) => h.piso))].sort()
  const visibles = habitaciones.filter((h) => filtro === null || h.situacion === filtro)
  return (
    <div className="lista">
      {pisos.map((p) => {
        const del = visibles.filter((h) => h.piso === p)
        if (del.length === 0) return null
        return (
          <div key={p}>
            <h3>{p > 0 ? `Piso ${p}` : 'Otras'}</h3>
            <div className="cuadricula">
              {del.map((h) => <Ficha key={h.numero} h={h} apagada={false} alAbrir={alAbrir} />)}
            </div>
          </div>
        )
      })}
      {visibles.length === 0 && <p className="tenue">No hay habitaciones en esa situacion.</p>}
    </div>
  )
}

function Ficha({ h, apagada, alAbrir, estilo }: {
  h: HabitacionRack
  apagada: boolean
  alAbrir: (h: HabitacionRack) => void
  estilo?: React.CSSProperties
}) {
  const persona = h.situacion === 'Ocupada' ? h.huesped : h.situacion === 'LlegaHoy' ? h.llegada?.huesped : null
  const titulo = [
    `${h.numero} · ${h.tipo ?? ''}`,
    NOMBRES[h.situacion] + (h.estado && h.situacion !== 'Bloqueada' && h.estado !== 'NORMAL' ? ` (${h.estado})` : ''),
    persona,
    h.situacion === 'Ocupada' && h.fechaSalida ? `Sale ${fecha(h.fechaSalida, true)}` : null,
    h.situacion === 'Ocupada' ? `Saldo ${dinero(h.saldo)}` : null,
    h.comentario,
  ].filter(Boolean).join('\n')

  return (
    <button type="button" className={`ficha s-${h.situacion}${apagada ? ' apagada' : ''}`} style={estilo}
            title={titulo} onClick={() => alAbrir(h)}>
      <span className="num">{h.numero}</span>
      {persona && <span className="quien">{persona}</span>}
      {h.situacion === 'Ocupada' && (
        <span className="datos">
          {h.fechaSalida && <span className={h.avisos.salidaVencida ? 'mal' : h.avisos.saleHoy ? 'alerta' : ''}>
            {h.avisos.salidaVencida ? 'Vencida ' : h.avisos.saleHoy ? 'Sale hoy ' : ''}{fecha(h.fechaSalida)}
          </span>}
          <span>{dinero(h.saldo)}</span>
        </span>
      )}
      {h.situacion !== 'Ocupada' && <span className="datos tenue">{h.tipo}</span>}
      {(h.credito || h.avisos.ocupadaEstandoBloqueada) && (
        <span className="marcas">
          {h.credito && <abbr title="Credito habilitado para la cafeteria">CR</abbr>}
          {h.avisos.ocupadaEstandoBloqueada && <abbr title="Ocupada, pero la habitacion esta bloqueada">!</abbr>}
        </span>
      )}
    </button>
  )
}
