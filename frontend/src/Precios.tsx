import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import Fondo from './Fondo'
import {
  dinero, fecha, FACTOR_IMPUESTOS, guardarPrecio, obtenerPrecios, SesionVencida,
  type CategoriaPrecio, type PantallaPrecios, type Precio, type TipoHabitacion,
} from './api'

interface Edicion {
  tipo: TipoHabitacion
  categoria: CategoriaPrecio
  actual: Precio | undefined
}

/** Precios por tipo de habitacion y categoria (etapa 2). */
export default function Precios({ alVencer }: { alVencer: () => void }) {
  const [datos, setDatos] = useState<PantallaPrecios | null>(null)
  const [error, setError] = useState('')
  const [editando, setEditando] = useState<Edicion | null>(null)
  const [todos, setTodos] = useState(false)
  const [aviso, setAviso] = useState('')

  const manejar = useCallback(
    (e: unknown) => (e instanceof SesionVencida ? alVencer() : setError(e instanceof Error ? e.message : String(e))),
    [alVencer],
  )

  useEffect(() => {
    obtenerPrecios().then(setDatos).catch(manejar)
  }, [manejar])

  const precios = useMemo(() => {
    const m = new Map<string, Precio>()
    datos?.tabla.precios.forEach((p) => m.set(`${p.tipo}|${p.categoria}`, p))
    return m
  }, [datos])

  if (error && !datos) return <p className="error">{error}</p>
  if (!datos) return <p className="tenue">Cargando los precios...</p>

  const { tipos, categorias } = datos.tabla
  // los tipos sin habitaciones (como los "0" de la base) se esconden, salvo que se pidan
  const visibles = tipos.filter((t) => todos || t.habitaciones > 0)
  const ocultos = tipos.length - visibles.length

  function alGuardar(p: Precio) {
    setDatos((d) => d && {
      ...d,
      tabla: { ...d.tabla, precios: [...d.tabla.precios.filter((x) => !(x.tipo === p.tipo && x.categoria === p.categoria)), p] },
    })
    setEditando(null)
    setAviso(`Guardado: ${tipos.find((t) => t.id === p.tipo)?.descripcion} ${p.categoria} = ${dinero(p.conImpuestos)}`)
  }

  return (
    <section className="precios">
      <h2>Precios de las habitaciones</h2>
      <p className="tenue">
        Precio por noche que paga el huesped, con ITBIS (18 %) y propina (10 %). Debajo, el que se guarda sin impuestos.
        Los cambios valen para los check-in nuevos: las estadias en curso siguen con su precio.
        {!datos.puedeEditar && ' Su usuario solo puede consultar.'}
      </p>
      {aviso && <p className="ok-texto" role="status">{aviso}</p>}

      <div className="tabla-marco">
        <table className="tabla matriz">
          <thead>
            <tr>
              <th>Tipo</th>
              {categorias.map((c) => (
                <th key={c.id} className="num" title={c.descripcion}>
                  {c.id}{c.descripcion !== c.id && <span className="tenue"> {c.descripcion.toLowerCase()}</span>}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {visibles.map((t) => (
              <tr key={t.id}>
                <th scope="row">
                  {t.descripcion}
                  <span className="tenue fila-sub">{t.habitaciones} hab. · {t.minimo === t.maximo ? t.maximo : `${t.minimo}-${t.maximo}`} pers.</span>
                </th>
                {categorias.map((c) => {
                  const p = precios.get(`${t.id}|${c.id}`)
                  const raro = p && Math.round(p.conImpuestos) !== p.conImpuestos
                  const contenido = p ? (
                    <>
                      <strong className={raro ? 'alerta' : ''}>{dinero(p.conImpuestos)}</strong>
                      <span className="tenue">{dinero(p.sinImpuestos)}</span>
                    </>
                  ) : <span className="tenue">-</span>
                  const titulo = p
                    ? `Sin impuestos ${p.sinImpuestos}${p.personaAdicional ? ` · persona adicional ${dinero(p.personaAdicional)}` : ''}` +
                      (p.usuario ? ` · ${p.usuario} ${fecha(p.fecha, true)}` : '') +
                      (raro ? ' · el precio final no es redondo (redondeo viejo)' : '')
                    : 'Sin precio'
                  return (
                    <td key={c.id} className="num celda">
                      {datos.puedeEditar
                        ? <button type="button" className="celda-boton" title={titulo}
                                  onClick={() => setEditando({ tipo: t, categoria: c, actual: p })}>{contenido}</button>
                        : <span title={titulo}>{contenido}</span>}
                    </td>
                  )
                })}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {ocultos > 0 && (
        <button type="button" className="secundario" onClick={() => setTodos(!todos)}>
          {todos ? 'Esconder' : 'Mostrar'} {ocultos} tipos sin habitaciones
        </button>
      )}
      <p className="tenue pie">Los precios en color tienen un precio final con centavos (por ejemplo 2,500.01), de cuando se escribian sin impuestos.</p>

      {editando && (
        <Editor e={editando} alCerrar={() => setEditando(null)} alGuardar={alGuardar} alError={manejar} />
      )}
    </section>
  )
}

function Editor({ e, alCerrar, alGuardar, alError }: {
  e: Edicion
  alCerrar: () => void
  alGuardar: (p: Precio) => void
  alError: (x: unknown) => void
}) {
  const [final, setFinal] = useState(e.actual ? String(e.actual.conImpuestos) : '')
  const [adicional, setAdicional] = useState(e.actual ? String(e.actual.personaAdicional) : '0')
  const [error, setError] = useState('')
  const [guardando, setGuardando] = useState(false)

  const valor = Number(final.replace(/,/g, ''))
  const valorAdicional = Number(adicional.replace(/,/g, '') || '0')
  const valido = final.trim() !== '' && Number.isFinite(valor) && valor >= 0 && Number.isFinite(valorAdicional) && valorAdicional >= 0

  useEffect(() => {
    const tecla = (k: KeyboardEvent) => k.key === 'Escape' && alCerrar()
    window.addEventListener('keydown', tecla)
    return () => window.removeEventListener('keydown', tecla)
  }, [alCerrar])

  async function enviar(ev: FormEvent) {
    ev.preventDefault()
    if (!valido) return
    setGuardando(true)
    setError('')
    try {
      alGuardar(await guardarPrecio(e.tipo.id, e.categoria.id, Math.round(valor * 100) / 100, Math.round(valorAdicional * 100) / 100))
    } catch (x) {
      if (x instanceof SesionVencida) alError(x)
      else setError(x instanceof Error ? x.message : String(x))
    } finally {
      setGuardando(false)
    }
  }

  return (
    <Fondo centro>
      <form className="dialogo" role="dialog" aria-modal="true" aria-label="Cambiar precio"
            onSubmit={enviar}>
        <h2>{e.tipo.descripcion} · categoria {e.categoria.id}</h2>
        {e.actual && <p className="tenue">Hoy: {dinero(e.actual.conImpuestos)} ({e.actual.sinImpuestos} sin impuestos)</p>}
        <label>
          Precio por noche que paga el huesped (con ITBIS y propina)
          <input inputMode="decimal" value={final} onChange={(x) => setFinal(x.target.value)} autoFocus />
        </label>
        <p className="tenue">
          Se guarda sin impuestos: <strong>{valido ? (Math.round((valor / FACTOR_IMPUESTOS) * 1000) / 1000).toFixed(3) : '-'}</strong>
          . A un cliente sin ITBIS o sin propina se le cobra menos, como en Delphi.
        </p>
        <label>
          Persona adicional (sin impuestos)
          <input inputMode="decimal" value={adicional} onChange={(x) => setAdicional(x.target.value)} />
        </label>
        {error && <p className="error" role="alert">{error}</p>}
        <div className="botones">
          <button type="button" className="secundario" onClick={alCerrar}>Cancelar</button>
          <button type="submit" disabled={!valido || guardando}>{guardando ? 'Guardando...' : 'Guardar'}</button>
        </div>
      </form>
    </Fondo>
  )
}
