import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { crearTipo, modificarTipo, obtenerTipos, SesionVencida, type DatosTipo, type TipoHabitacion } from './api'

type Edicion = { nuevo: true } | { nuevo: false; t: TipoHabitacion }

/** Tipos de habitacion (etapa 2). No se borran: las habitaciones y los precios apuntan al tipo. */
export default function Tipos({ alVencer }: { alVencer: () => void }) {
  const [tipos, setTipos] = useState<TipoHabitacion[] | null>(null)
  const [puedeEditar, setPuedeEditar] = useState(false)
  const [error, setError] = useState('')
  const [editando, setEditando] = useState<Edicion | null>(null)
  const [aviso, setAviso] = useState('')

  const manejar = useCallback(
    (e: unknown) => (e instanceof SesionVencida ? alVencer() : setError(e instanceof Error ? e.message : String(e))),
    [alVencer],
  )

  useEffect(() => {
    obtenerTipos()
      .then((r) => {
        setTipos(r.tipos)
        setPuedeEditar(r.puedeEditar)
      })
      .catch(manejar)
  }, [manejar])

  if (error && !tipos) return <p className="error">{error}</p>
  if (!tipos) return <p className="tenue">Cargando los tipos...</p>

  function alGuardar(t: TipoHabitacion, nuevo: boolean) {
    setTipos((l) => l && [...l.filter((x) => x.id !== t.id), t].sort((a, b) => a.id - b.id))
    setEditando(null)
    setAviso(`${nuevo ? 'Creado' : 'Guardado'} el tipo ${t.descripcion}.${nuevo ? ' Ponga sus precios en la pestaña Precios.' : ''}`)
  }

  return (
    <section>
      <div className="rack-cabecera">
        <p className="tenue">
          {tipos.length} tipos. El nombre se guarda en mayúsculas, como en Delphi. No se borran.
          {!puedeEditar && ' Su usuario solo puede consultar.'}
        </p>
        {puedeEditar && <button type="button" onClick={() => setEditando({ nuevo: true })}>Nuevo tipo</button>}
      </div>
      {aviso && <p className="ok-texto" role="status">{aviso}</p>}

      <div className="tabla-marco">
        <table className="tabla catalogo">
          <thead>
            <tr><th className="num">No.</th><th>Nombre</th><th>Siglas</th><th className="num">Personas</th><th className="num">Habitaciones</th></tr>
          </thead>
          <tbody>
            {tipos.map((t) => (
              <tr key={t.id} className={`${puedeEditar ? 'fila-editable' : ''}${t.habitaciones === 0 ? ' apagada' : ''}`}
                  onClick={puedeEditar ? () => setEditando({ nuevo: false, t }) : undefined}
                  tabIndex={puedeEditar ? 0 : undefined}
                  onKeyDown={puedeEditar ? (e) => e.key === 'Enter' && setEditando({ nuevo: false, t }) : undefined}>
                <td className="num">{t.id}</td>
                <td><strong>{t.descripcion}</strong></td>
                <td>{t.siglas}</td>
                <td className="num">{t.minimo === t.maximo ? t.maximo : `${t.minimo} a ${t.maximo}`}</td>
                <td className="num">{t.habitaciones || <span className="tenue">ninguna</span>}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {editando && <Editor e={editando} alCerrar={() => setEditando(null)} alGuardar={alGuardar} alError={manejar} />}
    </section>
  )
}

function Editor({ e, alCerrar, alGuardar, alError }: {
  e: Edicion
  alCerrar: () => void
  alGuardar: (t: TipoHabitacion, nuevo: boolean) => void
  alError: (x: unknown) => void
}) {
  const t = e.nuevo ? null : e.t
  const [d, setD] = useState<DatosTipo>({
    descripcion: t?.descripcion ?? '',
    siglas: t?.siglas ?? '',
    minimo: t?.minimo || 1,
    maximo: t?.maximo || 2,
  })
  const [error, setError] = useState('')
  const [guardando, setGuardando] = useState(false)

  useEffect(() => {
    const tecla = (k: KeyboardEvent) => k.key === 'Escape' && alCerrar()
    window.addEventListener('keydown', tecla)
    return () => window.removeEventListener('keydown', tecla)
  }, [alCerrar])

  async function enviar(ev: FormEvent) {
    ev.preventDefault()
    setGuardando(true)
    setError('')
    try {
      alGuardar(e.nuevo ? await crearTipo(d) : await modificarTipo(t!.id, d), e.nuevo)
    } catch (x) {
      if (x instanceof SesionVencida) alError(x)
      else setError(x instanceof Error ? x.message : String(x))
    } finally {
      setGuardando(false)
    }
  }

  const numero = (v: string) => Math.max(0, Math.min(99, Number(v.replace(/\D/g, '')) || 0))

  return (
    <div className="fondo-panel centro" onClick={alCerrar}>
      <form className="dialogo" role="dialog" aria-modal="true" aria-label="Tipo de habitacion"
            onClick={(x) => x.stopPropagation()} onSubmit={enviar}>
        <h2>{e.nuevo ? 'Nuevo tipo de habitación' : `Tipo ${t!.id}`}</h2>
        {t && t.habitaciones > 0 && <p className="tenue">Lo usan {t.habitaciones} habitaciones.</p>}
        <label>
          Nombre
          <input maxLength={50} value={d.descripcion} onChange={(x) => setD({ ...d, descripcion: x.target.value })} autoFocus />
        </label>
        <label>
          Siglas
          <input maxLength={3} value={d.siglas ?? ''} onChange={(x) => setD({ ...d, siglas: x.target.value })} />
        </label>
        <div className="dos">
          <label>
            Personas mínimo
            <input inputMode="numeric" value={d.minimo} onChange={(x) => setD({ ...d, minimo: numero(x.target.value) })} />
          </label>
          <label>
            Personas máximo
            <input inputMode="numeric" value={d.maximo} onChange={(x) => setD({ ...d, maximo: numero(x.target.value) })} />
          </label>
        </div>
        {error && <p className="error" role="alert">{error}</p>}
        <div className="botones">
          <button type="button" className="secundario" onClick={alCerrar}>Cancelar</button>
          <button type="submit" disabled={guardando || d.descripcion.trim() === ''}>{guardando ? 'Guardando...' : 'Guardar'}</button>
        </div>
      </form>
    </div>
  )
}
