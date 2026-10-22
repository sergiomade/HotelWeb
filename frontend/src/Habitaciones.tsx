import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import {
  crearHabitacion, fecha, modificarHabitacion, obtenerHabitaciones, posicionSugerida, SesionVencida,
  type DatosHabitacion, type EstadoHabitacion, type Habitacion, type PantallaHabitaciones, type TipoHabitacion,
} from './api'

type Edicion = { nueva: true } | { nueva: false; h: Habitacion }

/**
 * Catalogo de habitaciones (etapa 2): tipo, estado, comentario y caracteristicas. No se borran.
 * La columna hotelHabitacion.cabana es en realidad "sabanas" (lo aclaro el usuario): en pantalla dice Sabanas.
 */
export default function Habitaciones({ alVencer }: { alVencer: () => void }) {
  const [datos, setDatos] = useState<PantallaHabitaciones | null>(null)
  const [error, setError] = useState('')
  const [buscar, setBuscar] = useState('')
  const [estado, setEstado] = useState<number | 0>(0)
  const [editando, setEditando] = useState<Edicion | null>(null)
  const [aviso, setAviso] = useState('')

  const manejar = useCallback(
    (e: unknown) => (e instanceof SesionVencida ? alVencer() : setError(e instanceof Error ? e.message : String(e))),
    [alVencer],
  )

  useEffect(() => {
    obtenerHabitaciones().then(setDatos).catch(manejar)
  }, [manejar])

  const tipos = useMemo(() => new Map(datos?.catalogo.tipos.map((t) => [t.id, t])), [datos])
  const estados = useMemo(() => new Map(datos?.catalogo.estados.map((e) => [e.id, e])), [datos])

  if (error && !datos) return <p className="error">{error}</p>
  if (!datos) return <p className="tenue">Cargando las habitaciones...</p>

  const texto = buscar.trim().toLowerCase()
  const lista = datos.catalogo.habitaciones.filter((h) =>
    (estado === 0 || h.estado === estado) &&
    (texto === '' || String(h.numero).includes(texto) || (h.comentario ?? '').toLowerCase().includes(texto) ||
      (tipos.get(h.tipo)?.descripcion ?? '').toLowerCase().includes(texto)))

  function alGuardar(h: Habitacion, nueva: boolean) {
    setDatos((d) => d && {
      ...d,
      catalogo: {
        ...d.catalogo,
        habitaciones: [...d.catalogo.habitaciones.filter((x) => x.numero !== h.numero), h].sort((a, b) => a.numero - b.numero),
      },
    })
    setEditando(null)
    setAviso(`${nueva ? 'Creada' : 'Guardada'} la habitacion ${h.numero}.`)
  }

  return (
    <section>
      <div className="rack-cabecera">
        <p className="tenue">
          {datos.catalogo.habitaciones.length} habitaciones. Para sacar una de uso, cambiele el estado (no se borran).
          {!datos.puedeEditar && ' Su usuario solo puede consultar.'}
        </p>
        {datos.puedeEditar && <button type="button" onClick={() => setEditando({ nueva: true })}>Nueva habitacion</button>}
      </div>

      <div className="filtros">
        <input placeholder="Buscar numero, tipo o comentario" value={buscar} onChange={(e) => setBuscar(e.target.value)} />
        <select value={estado} onChange={(e) => setEstado(Number(e.target.value))} aria-label="Estado">
          <option value={0}>Todos los estados</option>
          {datos.catalogo.estados.map((e) => <option key={e.id} value={e.id}>{e.descripcion}</option>)}
        </select>
      </div>
      {aviso && <p className="ok-texto" role="status">{aviso}</p>}

      <div className="tabla-marco">
        <table className="tabla catalogo">
          <thead>
            <tr>
              <th className="num">No.</th><th>Tipo</th><th>Estado</th><th>Comentario</th><th>Ext.</th>
              <th>TV</th><th>Aire</th><th>Sábanas</th><th>Ahora</th><th>Ultimo cambio</th>
            </tr>
          </thead>
          <tbody>
            {lista.map((h) => {
              const est = estados.get(h.estado)
              return (
                <tr key={h.numero} className={datos.puedeEditar ? 'fila-editable' : ''}
                    onClick={datos.puedeEditar ? () => setEditando({ nueva: false, h }) : undefined}
                    tabIndex={datos.puedeEditar ? 0 : undefined}
                    onKeyDown={datos.puedeEditar ? (e) => e.key === 'Enter' && setEditando({ nueva: false, h }) : undefined}>
                  <td className="num"><strong>{h.numero}</strong></td>
                  <td>{tipos.get(h.tipo)?.descripcion ?? h.tipo}</td>
                  <td className={est && !est.disponible ? 'mal' : ''}>{est?.descripcion ?? h.estado}</td>
                  <td>{h.comentario}</td>
                  <td>{h.extension}</td>
                  <td>{h.television ? 'Si' : ''}</td>
                  <td>{h.aireAcondicionado ? 'Si' : ''}</td>
                  <td>{h.cabana ? 'Si' : ''}</td>
                  <td>{h.ocupada ? 'Ocupada' : ''}</td>
                  <td className="tenue">{h.usuario} {fecha(h.fecha, true)}</td>
                </tr>
              )
            })}
            {lista.length === 0 && <tr><td colSpan={10} className="tenue">Ninguna habitacion con ese filtro.</td></tr>}
          </tbody>
        </table>
      </div>

      {editando && (
        <Editor e={editando} tipos={datos.catalogo.tipos} estados={datos.catalogo.estados}
                alCerrar={() => setEditando(null)} alGuardar={alGuardar} alError={manejar} />
      )}
    </section>
  )
}

function Editor({ e, tipos, estados, alCerrar, alGuardar, alError }: {
  e: Edicion
  tipos: TipoHabitacion[]
  estados: EstadoHabitacion[]
  alCerrar: () => void
  alGuardar: (h: Habitacion, nueva: boolean) => void
  alError: (x: unknown) => void
}) {
  const h = e.nueva ? null : e.h
  const [numero, setNumero] = useState('')
  const [pos, setPos] = useState<{ x: number; y: number } | null>(null)
  const [d, setD] = useState<DatosHabitacion>({
    tipo: h?.tipo ?? tipos.find((t) => t.habitaciones > 0)?.id ?? tipos[0]?.id ?? 0,
    estado: h?.estado ?? estados.find((x) => x.disponible)?.id ?? estados[0]?.id ?? 0,
    comentario: h?.comentario ?? '',
    extension: h?.extension ?? '',
    television: h?.television ?? true,
    aireAcondicionado: h?.aireAcondicionado ?? true,
    cabana: h?.cabana ?? false,
  })
  const [error, setError] = useState('')
  const [guardando, setGuardando] = useState(false)

  useEffect(() => {
    const tecla = (k: KeyboardEvent) => k.key === 'Escape' && alCerrar()
    window.addEventListener('keydown', tecla)
    return () => window.removeEventListener('keydown', tecla)
  }, [alCerrar])

  // habitacion nueva: donde quedaria en el plano
  const n = Number(numero)
  useEffect(() => {
    if (!e.nueva || !Number.isInteger(n) || n <= 0) return
    const t = setTimeout(() => posicionSugerida(n).then(setPos).catch(() => setPos(null)), 300)
    return () => clearTimeout(t)
  }, [e.nueva, n])

  const estadoElegido = estados.find((x) => x.id === d.estado)
  const bloqueaOcupada = h?.ocupada && estadoElegido && !estadoElegido.disponible

  async function enviar(ev: FormEvent) {
    ev.preventDefault()
    setGuardando(true)
    setError('')
    try {
      const limpio = { ...d, comentario: d.comentario?.trim() || null, extension: d.extension?.trim() || null }
      const r = e.nueva ? await crearHabitacion(n, pos?.x ?? null, pos?.y ?? null, limpio) : await modificarHabitacion(h!.numero, limpio)
      alGuardar(r, e.nueva)
    } catch (x) {
      if (x instanceof SesionVencida) alError(x)
      else setError(x instanceof Error ? x.message : String(x))
    } finally {
      setGuardando(false)
    }
  }

  const cambiar = <K extends keyof DatosHabitacion>(k: K, v: DatosHabitacion[K]) => setD((x) => ({ ...x, [k]: v }))

  return (
    <div className="fondo-panel centro" onClick={alCerrar}>
      <form className="dialogo ancho" role="dialog" aria-modal="true" aria-label="Habitacion"
            onClick={(x) => x.stopPropagation()} onSubmit={enviar}>
        <h2>{e.nueva ? 'Nueva habitacion' : `Habitacion ${h!.numero}`}</h2>
        {h?.ocupada && <p className="tenue">Esta ocupada ahora.</p>}

        {e.nueva && (
          <label>
            Numero
            <input inputMode="numeric" value={numero} onChange={(x) => setNumero(x.target.value.replace(/\D/g, ''))} autoFocus />
            {pos && <span className="tenue">Va en el plano en x {pos.x}, y {pos.y} (junto a las de su piso).</span>}
          </label>
        )}
        <div className="dos">
          <label>
            Tipo
            <select value={d.tipo} onChange={(x) => cambiar('tipo', Number(x.target.value))}>
              {tipos.map((t) => <option key={t.id} value={t.id}>{t.descripcion}</option>)}
            </select>
          </label>
          <label>
            Estado
            <select value={d.estado} onChange={(x) => cambiar('estado', Number(x.target.value))} autoFocus={!e.nueva}>
              {estados.map((s) => <option key={s.id} value={s.id}>{s.descripcion}{s.disponible ? '' : ' (no se puede ocupar)'}</option>)}
            </select>
          </label>
        </div>
        {bloqueaOcupada && (
          <p className="alerta">Esta ocupada: el huesped sigue en ella. El rack la mostrara ocupada con un aviso.</p>
        )}
        <label>
          Comentario
          <textarea rows={2} maxLength={500} value={d.comentario ?? ''} onChange={(x) => cambiar('comentario', x.target.value)} />
        </label>
        <div className="dos">
          <label>
            Extension
            <input maxLength={4} value={d.extension ?? ''} onChange={(x) => cambiar('extension', x.target.value)} />
          </label>
          <div className="marcas-form">
            <label className="check"><input type="checkbox" checked={d.television} onChange={(x) => cambiar('television', x.target.checked)} /> Television</label>
            <label className="check"><input type="checkbox" checked={d.aireAcondicionado} onChange={(x) => cambiar('aireAcondicionado', x.target.checked)} /> Aire</label>
            <label className="check"><input type="checkbox" checked={d.cabana} onChange={(x) => cambiar('cabana', x.target.checked)} /> Sábanas</label>
          </div>
        </div>
        {error && <p className="error" role="alert">{error}</p>}
        <div className="botones">
          <button type="button" className="secundario" onClick={alCerrar}>Cancelar</button>
          <button type="submit" disabled={guardando || (e.nueva && !(n > 0))}>{guardando ? 'Guardando...' : 'Guardar'}</button>
        </div>
      </form>
    </div>
  )
}
