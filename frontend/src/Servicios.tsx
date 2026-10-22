import { useCallback, useEffect, useState, type FormEvent } from 'react'
import Fondo from './Fondo'
import {
  crearServicio, dinero, modificarServicio, obtenerServicios, SesionVencida, type DatosServicio, type Servicio,
} from './api'

type Edicion = { nuevo: true } | { nuevo: false; s: Servicio }

/** Servicios que se cargan a las cuentas (etapa 2). El precio es sin impuestos, como en Delphi. No se borran. */
export default function Servicios({ alVencer }: { alVencer: () => void }) {
  const [servicios, setServicios] = useState<Servicio[] | null>(null)
  const [puedeEditar, setPuedeEditar] = useState(false)
  const [error, setError] = useState('')
  const [editando, setEditando] = useState<Edicion | null>(null)
  const [aviso, setAviso] = useState('')

  const manejar = useCallback(
    (e: unknown) => (e instanceof SesionVencida ? alVencer() : setError(e instanceof Error ? e.message : String(e))),
    [alVencer],
  )

  useEffect(() => {
    obtenerServicios()
      .then((r) => {
        setServicios(r.servicios)
        setPuedeEditar(r.puedeEditar)
      })
      .catch(manejar)
  }, [manejar])

  if (error && !servicios) return <p className="error">{error}</p>
  if (!servicios) return <p className="tenue">Cargando los servicios...</p>

  function alGuardar(s: Servicio, nuevo: boolean) {
    setServicios((l) => l && [...l.filter((x) => x.id !== s.id), s].sort((a, b) => a.id - b.id))
    setEditando(null)
    setAviso(`${nuevo ? 'Creado' : 'Guardado'} el servicio ${s.descripcion}.`)
  }

  return (
    <section>
      <div className="rack-cabecera">
        <p className="tenue">
          Precio sin impuestos, como en Delphi; en la cuenta de la habitación el ITBIS y la propina dependen del cliente.
          Un cambio vale para los cargos nuevos. No se borran.
          {!puedeEditar && ' Su usuario solo puede consultar.'}
        </p>
        {puedeEditar && <button type="button" onClick={() => setEditando({ nuevo: true })}>Nuevo servicio</button>}
      </div>
      {aviso && <p className="ok-texto" role="status">{aviso}</p>}

      <div className="tabla-marco">
        <table className="tabla catalogo">
          <thead>
            <tr>
              <th className="num">No.</th><th>Descripción</th><th className="num">Precio</th>
              <th className="num">Con ITBIS</th><th className="num">Con ITBIS y propina</th><th>ITBIS</th>
              <th className="num">Cargos</th>
            </tr>
          </thead>
          <tbody>
            {servicios.map((s) => (
              <tr key={s.id} className={puedeEditar ? 'fila-editable' : ''}
                  onClick={puedeEditar ? () => setEditando({ nuevo: false, s }) : undefined}
                  tabIndex={puedeEditar ? 0 : undefined}
                  onKeyDown={puedeEditar ? (e) => e.key === 'Enter' && setEditando({ nuevo: false, s }) : undefined}>
                <td className="num">{s.id}</td>
                <td>
                  <strong>{s.descripcion}</strong>
                  {s.delSistema && <span className="tenue fila-sub" title="El sistema usa este servicio por su numero">Del sistema: {s.delSistema}</span>}
                </td>
                <td className="num">{dinero(s.precio)}</td>
                <td className="num">{dinero(s.conItbis)}</td>
                <td className="num">{dinero(s.conItbisYPropina)}</td>
                <td>{s.impuesto ? 'Sí' : 'No'}</td>
                <td className="num">{s.usos || ''}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {editando && <Editor e={editando} alCerrar={() => setEditando(null)} alGuardar={alGuardar} alError={manejar} />}
    </section>
  )
}

const BASES = { itbis: { nombre: 'con ITBIS (18 %)', factor: 1.18 }, ambos: { nombre: 'con ITBIS y propina (28 %)', factor: 1.28 } }

function Editor({ e, alCerrar, alGuardar, alError }: {
  e: Edicion
  alCerrar: () => void
  alGuardar: (s: Servicio, nuevo: boolean) => void
  alError: (x: unknown) => void
}) {
  const s = e.nuevo ? null : e.s
  const [descripcion, setDescripcion] = useState(s?.descripcion ?? '')
  const [precio, setPrecio] = useState(s ? String(s.precio) : '')
  const [impuesto, setImpuesto] = useState(s?.impuesto ?? true)
  const [final, setFinal] = useState('')
  const [base, setBase] = useState<keyof typeof BASES>('itbis')
  const [error, setError] = useState('')
  const [guardando, setGuardando] = useState(false)

  useEffect(() => {
    const tecla = (k: KeyboardEvent) => k.key === 'Escape' && alCerrar()
    window.addEventListener('keydown', tecla)
    return () => window.removeEventListener('keydown', tecla)
  }, [alCerrar])

  const valor = Number(precio.replace(/,/g, ''))
  const valido = descripcion.trim() !== '' && precio.trim() !== '' && Number.isFinite(valor) && valor >= 0

  function calcular(texto: string, b: keyof typeof BASES) {
    setFinal(texto)
    const f = Number(texto.replace(/,/g, ''))
    if (texto.trim() !== '' && Number.isFinite(f) && f >= 0) setPrecio((Math.round((f / BASES[b].factor) * 100) / 100).toFixed(2))
  }

  async function enviar(ev: FormEvent) {
    ev.preventDefault()
    if (!valido) return
    setGuardando(true)
    setError('')
    const d: DatosServicio = { descripcion, precio: Math.round(valor * 100) / 100, impuesto }
    try {
      alGuardar(e.nuevo ? await crearServicio(d) : await modificarServicio(s!.id, d), e.nuevo)
    } catch (x) {
      if (x instanceof SesionVencida) alError(x)
      else setError(x instanceof Error ? x.message : String(x))
    } finally {
      setGuardando(false)
    }
  }

  return (
    <Fondo centro>
      <form className="dialogo ancho" role="dialog" aria-modal="true" aria-label="Servicio"
            onSubmit={enviar}>
        <h2>{e.nuevo ? 'Nuevo servicio' : `Servicio ${s!.id}`}</h2>
        {s?.delSistema && (
          <p className="alerta">Lo usa el sistema por su número ({s.delSistema}). Puede cambiar su precio y descripción, pero no su uso.</p>
        )}
        <label>
          Descripción
          <input maxLength={50} value={descripcion} onChange={(x) => setDescripcion(x.target.value)} autoFocus />
        </label>
        <label>
          Precio sin impuestos
          <input inputMode="decimal" value={precio} onChange={(x) => { setPrecio(x.target.value); setFinal('') }} />
          {valido && (
            <span className="tenue">
              Con ITBIS {dinero(Math.round(valor * 118) / 100)} · con ITBIS y propina {dinero(Math.round(valor * 128) / 100)}
            </span>
          )}
        </label>
        <div className="dos">
          <label>
            O calcúlelo desde el precio final
            <input inputMode="decimal" value={final} placeholder="precio final" onChange={(x) => calcular(x.target.value, base)} />
          </label>
          <label>
            que es
            <select value={base} onChange={(x) => { const b = x.target.value as keyof typeof BASES; setBase(b); if (final) calcular(final, b) }}>
              {Object.entries(BASES).map(([k, v]) => <option key={k} value={k}>{v.nombre}</option>)}
            </select>
          </label>
        </div>
        <label className="check">
          <input type="checkbox" checked={impuesto} onChange={(x) => setImpuesto(x.target.checked)} /> Lleva ITBIS
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
