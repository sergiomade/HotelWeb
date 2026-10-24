import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import Fondo from './Fondo'
import FotoTipo from './FotoTipo'
import { useFotosTipos } from './useFotosTipos'
import {
  achicarFoto, crearTipo, guardarFotoTipo, modificarTipo, obtenerTipos, quitarFotoTipo, SesionVencida, type DatosTipo,
  type TipoHabitacion,
} from './api'

type Edicion = { nuevo: true } | { nuevo: false; t: TipoHabitacion }

/** Tipos de habitacion (etapa 2). No se borran: las habitaciones y los precios apuntan al tipo. */
export default function Tipos({ alVencer }: { alVencer: () => void }) {
  const [tipos, setTipos] = useState<TipoHabitacion[] | null>(null)
  const [puedeEditar, setPuedeEditar] = useState(false)
  const [error, setError] = useState('')
  const [editando, setEditando] = useState<Edicion | null>(null)
  const [aviso, setAviso] = useState('')
  const [versionFotos, setVersionFotos] = useState(0)
  const fotos = useFotosTipos(versionFotos)
  const [subiendo, setSubiendo] = useState<number | null>(null)
  const archivo = useRef<HTMLInputElement>(null)
  const paraTipo = useRef<TipoHabitacion | null>(null)

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

  function elegirFoto(t: TipoHabitacion) {
    paraTipo.current = t
    archivo.current?.click()
  }

  async function subirFoto(f: File | undefined) {
    const t = paraTipo.current
    if (archivo.current) archivo.current.value = ''
    if (!f || !t) return
    setSubiendo(t.id)
    setError('')
    try {
      await guardarFotoTipo(t.id, await achicarFoto(f))
      setVersionFotos((v) => v + 1)
      setAviso(`Foto del tipo ${t.descripcion} guardada.`)
    } catch (e) {
      manejar(e instanceof Error && e.name === 'InvalidStateError' ? new Error('El archivo no es una foto.') : e)
    } finally {
      setSubiendo(null)
    }
  }

  async function quitarFoto(t: TipoHabitacion) {
    if (!window.confirm(`¿Quitar la foto del tipo ${t.descripcion}?`)) return
    try {
      await quitarFotoTipo(t.id)
      setVersionFotos((v) => v + 1)
      setAviso(`Quitada la foto del tipo ${t.descripcion}.`)
    } catch (e) {
      manejar(e)
    }
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
      {error && <p className="error" role="alert">{error}</p>}
      <input ref={archivo} type="file" accept="image/*" hidden onChange={(e) => subirFoto(e.target.files?.[0])} />

      <div className="tabla-marco">
        <table className="tabla catalogo">
          <thead>
            <tr><th className="num">No.</th><th>Nombre</th><th>Siglas</th><th className="num">Personas</th><th className="num">Habitaciones</th><th>Foto (ilustrativa)</th></tr>
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
                <td className="celda-foto" onClick={(e) => e.stopPropagation()} onKeyDown={(e) => e.stopPropagation()}>
                  <FotoTipo tipo={t.id} fotos={fotos} className="foto-tipo mini" />
                  {puedeEditar && (
                    <span className="acciones-fila">
                      <button type="button" className="secundario chico" disabled={subiendo === t.id} onClick={() => elegirFoto(t)}>
                        {subiendo === t.id ? 'Subiendo...' : fotos.some((f) => f.tipo === t.id) ? 'Cambiar' : 'Poner foto'}
                      </button>
                      {fotos.some((f) => f.tipo === t.id) && <button type="button" className="secundario chico" onClick={() => quitarFoto(t)}>Quitar</button>}
                    </span>
                  )}
                </td>
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
    <Fondo centro>
      <form className="dialogo" role="dialog" aria-modal="true" aria-label="Tipo de habitacion"
            onSubmit={enviar}>
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
    </Fondo>
  )
}
