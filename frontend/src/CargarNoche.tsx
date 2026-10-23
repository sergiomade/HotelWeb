import { useCallback, useEffect, useState } from 'react'
import { cargarNoche, dinero, fecha, hoyIso, SesionVencida, verNoche, type CargaNoche, type EstadoNoche } from './api'

const NOMBRES: Record<EstadoNoche, string> = {
  PorCargar: 'Por cargar',
  YaCargada: 'Ya cargada',
  Transferida: 'Transferida (no se carga)',
  SinTarifa: 'Sin tarifa (no se carga)',
}

/** Carga del hospedaje de una noche a las cuentas abiertas, sin repetir (en Delphi: "Cargar hospedaje"). */
export default function CargarNoche({ alVencer }: { alVencer: () => void }) {
  const [noche, setNoche] = useState(hoyIso())
  const [datos, setDatos] = useState<CargaNoche | null>(null)
  const [sinVencidas, setSinVencidas] = useState(false)
  const [error, setError] = useState('')
  const [aviso, setAviso] = useState('')
  const [cargando, setCargando] = useState(false)

  const manejar = useCallback(
    (e: unknown) => (e instanceof SesionVencida ? alVencer() : setError(e instanceof Error ? e.message : String(e))),
    [alVencer],
  )
  useEffect(() => {
    if (!noche) return
    verNoche(noche).then((r) => { setDatos(r); setError('') }).catch(manejar)
  }, [noche, manejar])

  const aCargar = datos?.cuentas.filter((c) => c.estado === 'PorCargar' && !(sinVencidas && c.salidaVencida)) ?? []
  const total = aCargar.reduce((s, c) => s + c.tarifa, 0)
  const vencidas = datos?.cuentas.filter((c) => c.estado === 'PorCargar' && c.salidaVencida).length ?? 0

  async function cargar() {
    if (aCargar.length === 0) return
    if (!window.confirm(`¿Cargar "${datos?.descripcion}" a ${aCargar.length} ${aCargar.length === 1 ? 'cuenta' : 'cuentas'} por ${dinero(total)}?`)) return
    setCargando(true)
    try {
      const r = await cargarNoche(noche, sinVencidas)
      setDatos(r.noche)
      setAviso(`Cargada la noche a ${r.cargadas} ${r.cargadas === 1 ? 'cuenta' : 'cuentas'}.`)
    } catch (e) {
      manejar(e)
    } finally {
      setCargando(false)
    }
  }

  return (
    <section>
      <p className="tenue">
        Carga el hospedaje de la noche a cada habitación ocupada, con la tarifa de su reserva. Las que ya tienen esa noche
        no se cargan otra vez.
      </p>
      <div className="filtros">
        <label className="corto">Noche<input type="date" value={noche} max={hoyIso()} onChange={(e) => { setNoche(e.target.value); setAviso('') }} /></label>
        {vencidas > 0 && (
          <label className="check">
            <input type="checkbox" checked={sinVencidas} onChange={(e) => setSinVencidas(e.target.checked)} />
            No cargar las {vencidas} con la salida vencida
          </label>
        )}
      </div>
      {aviso && <p className="ok-texto" role="status">{aviso}</p>}
      {error && <p className="error">{error}</p>}
      {datos && (
        <>
          <div className="tabla-marco">
            <table className="tabla catalogo">
              <thead><tr><th className="num">Habitación</th><th>Huésped</th><th>Salida</th><th className="num">Tarifa</th><th>Esta noche</th></tr></thead>
              <tbody>
                {datos.cuentas.map((c) => {
                  const salta = c.estado === 'PorCargar' && sinVencidas && c.salidaVencida
                  return (
                    <tr key={c.factura} className={c.estado === 'PorCargar' && !salta ? '' : 'apagada'}>
                      <td className="num"><strong>{c.habitacion}</strong></td>
                      <td>{c.huesped}</td>
                      <td className={c.salidaVencida ? 'mal' : ''}>{fecha(c.salida, true)}{c.salidaVencida ? ' (vencida)' : ''}</td>
                      <td className="num">{dinero(c.tarifa)}</td>
                      <td>{salta ? 'No se carga (salida vencida)' : NOMBRES[c.estado]}</td>
                    </tr>
                  )
                })}
                {datos.cuentas.length === 0 && <tr><td colSpan={5} className="tenue">No hay habitaciones ocupadas.</td></tr>}
              </tbody>
            </table>
          </div>
          <div className="rack-cabecera">
            <strong>{aCargar.length} por cargar · {dinero(total)} <span className="tenue">(sin impuestos)</span></strong>
            <button type="button" onClick={cargar} disabled={cargando || aCargar.length === 0}>
              {cargando ? 'Cargando...' : `Cargar ${datos.descripcion}`}
            </button>
          </div>
        </>
      )}
    </section>
  )
}
