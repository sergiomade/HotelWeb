import { useCallback, useEffect, useState, type FormEvent } from 'react'
import Fondo from './Fondo'
import { fecha, guardarSecuencia, obtenerSecuencias, SesionVencida, type PantallaSecuencias, type SecuenciaEcf } from './api'

const num = (v: number | null | undefined) => (v == null ? '' : v.toLocaleString('es-DO'))
const claseEstado = (e: string) =>
  e === 'OK' ? 'ok-texto' : e === 'NO SE USA' ? 'tenue' : e === 'POR AGOTARSE' || e === 'POR VENCER' ? 'alerta' : 'error'
const aIso = (iso: string | null) => (iso ? iso.slice(0, 10) : '')

/**
 * Secuencias e-NCF autorizadas por la DGII (etapa 9d), como la pantalla de Delphi (MIG_19): por tipo, el rango desde -
 * hasta, el vencimiento, la ultima usada, cuantas quedan y el estado. Fuera del rango o vencida, el sistema no emite.
 * Cuando la DGII da un rango nuevo se escribe aqui (solo los usuarios autorizados).
 */
export default function Secuencias({ alVencer }: { alVencer: () => void }) {
  const [d, setD] = useState<PantallaSecuencias | null>(null)
  const [error, setError] = useState('')
  const [aviso, setAviso] = useState('')
  const [editar, setEditar] = useState<SecuenciaEcf | null>(null)

  const manejar = useCallback(
    (e: unknown) => (e instanceof SesionVencida ? alVencer() : setError(e instanceof Error ? e.message : String(e))),
    [alVencer],
  )
  const cargar = useCallback(() => {
    setError('')
    obtenerSecuencias().then(setD).catch(manejar)
  }, [manejar])
  useEffect(cargar, [cargar])

  if (!d) return error ? <p className="error">{error}</p> : <p className="tenue">Cargando las secuencias...</p>

  return (
    <section>
      <p className="tenue">Avisa cuando quedan {d.avisoRestantes} números o menos, o faltan {d.avisoDias} días o menos para el vencimiento.
        Fuera del rango autorizado, o vencida, el sistema no da el e-NCF.</p>
      {aviso && <p className="ok-texto" role="status">{aviso}</p>}
      {error && <p className="error" role="alert">{error}</p>}
      <div className="tabla-marco">
        <table className="tabla catalogo">
          <thead>
            <tr><th>Tipo</th><th>Descripción</th><th>Autorización</th><th className="num">Desde</th><th className="num">Hasta</th><th className="num">Última usada</th>
              <th className="num">Quedan</th><th>Vence</th><th>Estado</th>{d.puedeEditar && <th></th>}</tr>
          </thead>
          <tbody>
            {d.secuencias.map((s) => (
              <tr key={s.codigo} className={s.estado === 'NO SE USA' ? 'apagada' : ''}>
                <td><strong>{s.codigo}</strong></td>
                <td>{s.descripcion}</td>
                <td>{s.autorizacion}{s.fechaAutorizacion && <span className="tenue fila-sub">{fecha(s.fechaAutorizacion, true)}</span>}</td>
                <td className="num">{num(s.desde)}</td>
                <td className="num">{num(s.hasta)}</td>
                <td className="num">{num(s.ultima)}</td>
                <td className="num">{s.desde != null ? num(s.restantes) : ''}</td>
                <td>{fecha(s.vence, true)}{s.dias != null && s.dias >= 0 && <span className="tenue fila-sub">en {s.dias} días</span>}</td>
                <td><span className={claseEstado(s.estado)}>{s.estado}</span></td>
                {d.puedeEditar && <td><button type="button" className="secundario chico" onClick={() => setEditar(s)}>Cambiar</button></td>}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {!d.puedeEditar && <p className="tenue">Solo los usuarios autorizados pueden registrar un rango nuevo.</p>}
      {editar && (
        <Editar s={editar} alVencer={alVencer}
                alCerrar={(m) => { setEditar(null); if (m) { setAviso(m); cargar() } }} />
      )}
    </section>
  )
}

function Editar({ s, alCerrar, alVencer }: { s: SecuenciaEcf; alCerrar: (mensaje?: string) => void; alVencer: () => void }) {
  const [autorizacion, setAutorizacion] = useState(s.autorizacion ?? '')
  const [fechaAut, setFechaAut] = useState(aIso(s.fechaAutorizacion))
  const [desde, setDesde] = useState(s.desde != null ? String(s.desde) : '')
  const [hasta, setHasta] = useState(s.hasta != null ? String(s.hasta) : '')
  const [vence, setVence] = useState(aIso(s.vence))
  const [error, setError] = useState('')
  const [enviando, setEnviando] = useState(false)

  useEffect(() => {
    const tecla = (k: KeyboardEvent) => k.key === 'Escape' && !enviando && alCerrar()
    window.addEventListener('keydown', tecla)
    return () => window.removeEventListener('keydown', tecla)
  }, [alCerrar, enviando])

  const d = Number(desde) || 0
  const h = Number(hasta) || 0
  const motivo = !autorizacion.trim() ? 'el número de la autorización'
    : !fechaAut ? 'la fecha de la autorización'
    : d < 1 || h < d ? 'un rango válido (desde 1 o más, hasta no menor)'
    : h <= s.ultima ? `un "hasta" mayor que ${s.ultima} (ya se usó hasta ahí)`
    : !vence || vence < fechaAut ? 'el vencimiento (no antes de la autorización)'
    : ''

  async function guardar(ev: FormEvent) {
    ev.preventDefault()
    if (motivo || enviando) return
    if (!window.confirm(`¿Registrar para el ${s.codigo} el rango ${d.toLocaleString('es-DO')} – ${h.toLocaleString('es-DO')}, que vence el ${fecha(vence, true)}?`)) return
    setEnviando(true)
    setError('')
    try {
      alCerrar((await guardarSecuencia(s.codigo, { autorizacion: autorizacion.trim(), fechaAutorizacion: fechaAut, desde: d, hasta: h, vence })).mensaje)
    } catch (e) {
      if (e instanceof SesionVencida) alVencer()
      else setError(e instanceof Error ? e.message : String(e))
    } finally {
      setEnviando(false)
    }
  }

  return (
    <Fondo centro>
      <form className="dialogo" role="dialog" aria-modal="true" aria-label={`Secuencia del ${s.codigo}`} onSubmit={guardar}>
        <h2>{s.codigo} · {s.descripcion}</h2>
        <p className="tenue">Los datos de la autorización de la Oficina Virtual de la DGII. Ya se usó hasta el {s.ultima.toLocaleString('es-DO')}.</p>
        <div className="dos">
          <label>Número de autorización<input value={autorizacion} maxLength={40} onChange={(e) => setAutorizacion(e.target.value)} autoFocus /></label>
          <label>Fecha de la autorización<input type="date" value={fechaAut} onChange={(e) => setFechaAut(e.target.value)} /></label>
          <label>Desde<input inputMode="numeric" value={desde} onChange={(e) => setDesde(e.target.value.replace(/\D/g, ''))} /></label>
          <label>Hasta<input inputMode="numeric" value={hasta} onChange={(e) => setHasta(e.target.value.replace(/\D/g, ''))} /></label>
          <label>Vence<input type="date" value={vence} onChange={(e) => setVence(e.target.value)} /></label>
        </div>
        {error && <p className="error" role="alert">{error}</p>}
        {!error && motivo && <p className="alerta">Falta: {motivo}.</p>}
        <div className="botones">
          <button type="button" className="secundario" onClick={() => alCerrar()} disabled={enviando}>Cancelar</button>
          <button type="submit" disabled={!!motivo || enviando}>Guardar</button>
        </div>
      </form>
    </Fondo>
  )
}
