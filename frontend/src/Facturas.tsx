import { useCallback, useEffect, useState } from 'react'
import Espera from './Espera'
import { dinero, fecha, hoyIso, listarFacturas, reenviarEcf, reenviarWalkIn, SesionVencida, type FacturaCerrada } from './api'

/** Las facturas cerradas (check-out) con su estado en la DGII: reimprimir y reenviar las que no llegaron. */
export default function Facturas({ alVencer }: { alVencer: () => void }) {
  const [desde, setDesde] = useState(() => hoyIso(-7))
  const [hasta, setHasta] = useState(() => hoyIso())
  const [texto, setTexto] = useState('')
  const [lista, setLista] = useState<FacturaCerrada[] | null>(null)
  const [cargando, setCargando] = useState(false)
  const [enviando, setEnviando] = useState<number | null>(null)
  const [error, setError] = useState('')
  const [aviso, setAviso] = useState('')

  const manejar = useCallback(
    (e: unknown) => (e instanceof SesionVencida ? alVencer() : setError(e instanceof Error ? e.message : String(e))),
    [alVencer],
  )

  const cargar = useCallback(() => {
    setCargando(true)
    setError('')
    listarFacturas(desde, hasta, texto.trim()).then(setLista).catch(manejar).finally(() => setCargando(false))
  }, [desde, hasta, texto, manejar])

  // al cambiar las fechas se busca solo; el texto, al dejar de escribir
  useEffect(() => {
    const t = setTimeout(cargar, texto ? 400 : 0)
    return () => clearTimeout(t)
  }, [cargar, texto])

  async function reenviar(f: FacturaCerrada) {
    if (!window.confirm(`¿Reenviar el ${f.encf} (${f.walkIn ? "walk-in" : "factura"} #${f.numero}) a la DGII?`)) return
    setEnviando(f.numero)
    setAviso('')
    setError('')
    try {
      const e = await (f.walkIn ? reenviarWalkIn(f.numero) : reenviarEcf(f.numero))
      setLista((l) => l?.map((x) => (x.numero === f.numero && x.walkIn === f.walkIn ? { ...x, dgii: e } : x)) ?? null)
      setAviso(`${e.encf}: ${e.estado}${e.mensaje ? ` · ${e.mensaje}` : ''}`)
    } catch (e) {
      manejar(e)
    } finally {
      setEnviando(null)
    }
  }

  const pendientes = lista?.filter((f) => f.dgii && !f.dgii.aceptado).length ?? 0

  return (
    <section>
      {enviando !== null && <Espera texto="Conectando con la DGII, espere..." />}
      <div className="filtros">
        <label className="corto">Desde<input type="date" value={desde} onChange={(e) => setDesde(e.target.value)} /></label>
        <label className="corto">hasta<input type="date" value={hasta} onChange={(e) => setHasta(e.target.value)} /></label>
        <label className="largo">Buscar
          <input placeholder="Habitación, número, e-NCF o nombre" value={texto} onChange={(e) => setTexto(e.target.value)} />
        </label>
        <button type="button" className="secundario" onClick={cargar} disabled={cargando}>{cargando ? 'Consultando la DGII...' : 'Actualizar'}</button>
      </div>
      {pendientes > 0 && <p className="alerta">{pendientes} {pendientes === 1 ? 'factura no está aceptada' : 'facturas no están aceptadas'} por la DGII: revise y reenvíe.</p>}
      {aviso && <p className="ok-texto" role="status">{aviso}</p>}
      {error && <p className="error" role="alert">{error}</p>}
      <div className="tabla-marco">
        <table className="tabla catalogo">
          <thead>
            <tr><th className="num">Factura</th><th>Fecha</th><th className="num">Hab.</th><th>Cliente / huésped</th><th>e-NCF</th><th className="num">Total</th><th>DGII</th><th></th></tr>
          </thead>
          <tbody>
            {lista?.map((f) => (
              <tr key={`${f.walkIn ? "w" : "h"}${f.numero}`}>
                <td className="num">{f.numero}</td>
                <td>{fecha(f.fecha, true)}</td>
                <td className="num">{f.walkIn ? <span className="tenue">Walk-in</span> : f.habitacion}</td>
                <td><strong>{f.cliente}</strong>{f.huesped && f.huesped !== f.cliente && <span className="tenue fila-sub">{f.huesped}</span>}</td>
                <td>{f.encf || <span className="tenue">—</span>}</td>
                <td className="num">{dinero(f.total)}</td>
                <td>
                  {f.transferida ? <span className="tenue">Transferida (no va)</span>
                    : !f.dgii ? <span className="tenue">Sin e-NCF</span>
                    : <span className={f.dgii.aceptado ? 'ok-texto' : 'error'} title={f.dgii.mensaje ?? ''}>{f.dgii.estado}</span>}
                  {f.dgii?.mensaje && !f.dgii.aceptado && <span className="tenue fila-sub">{f.dgii.mensaje}</span>}
                </td>
                <td className="acciones-fila">
                  {!f.transferida && f.encf && <button type="button" className="secundario chico" onClick={() => window.open(f.walkIn ? `/walkin/${f.numero}` : `/factura/${f.numero}`, '_blank')}>Imprimir</button>}
                  {f.dgii && !f.dgii.aceptado && f.dgii.estado !== 'Desconocido' && (
                    <button type="button" className="chico" disabled={enviando !== null} onClick={() => reenviar(f)}>Reenviar</button>
                  )}
                </td>
              </tr>
            ))}
            {lista?.length === 0 && <tr><td colSpan={8} className="tenue">No hay facturas cerradas en esas fechas.</td></tr>}
            {!lista && cargando && <tr><td colSpan={8} className="tenue">Consultando las facturas y la DGII...</td></tr>}
          </tbody>
        </table>
      </div>
    </section>
  )
}
