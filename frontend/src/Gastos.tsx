import { useCallback, useEffect, useState, type FormEvent } from 'react'
import Espera from './Espera'
import Fondo from './Fondo'
import {
  crearGasto, dinero, fecha, hoyIso, listarGastos, reenviarGasto, SesionVencida,
  type EstadoEcf, type GastoLista, type NuevoGasto, type PantallaGastos,
} from './api'

const num = (s: string) => Number(s.replace(/,/g, '')) || 0

/**
 * Gastos menores (E43, etapa 9d), como UFrmGastoMenor de Delphi: el comprobante que el hotel emite para sustentar pagos de su
 * personal por consumibles, pasajes, estacionamiento, peajes... (Norma 05-19, Art. 8). No es para pagarle a alguien sin
 * RNC (eso es la compra a informal, E41). Lo pagado en efectivo resta en el cuadre de quien lo hizo.
 */
export default function Gastos({ alVencer }: { alVencer: () => void }) {
  const [desde, setDesde] = useState(() => hoyIso(-30))
  const [hasta, setHasta] = useState(() => hoyIso())
  const [d, setD] = useState<PantallaGastos | null>(null)
  const [error, setError] = useState('')
  const [aviso, setAviso] = useState('')
  const [nuevo, setNuevo] = useState(false)
  const [enviando, setEnviando] = useState(false)

  const manejar = useCallback(
    (e: unknown) => (e instanceof SesionVencida ? alVencer() : setError(e instanceof Error ? e.message : String(e))),
    [alVencer],
  )
  const cargar = useCallback(() => {
    setError('')
    listarGastos(desde, hasta).then(setD).catch(manejar)
  }, [desde, hasta, manejar])
  useEffect(cargar, [cargar])

  async function reenviar(g: GastoLista) {
    if (!window.confirm(`¿Reenviar el ${g.encf} a la DGII?`)) return
    setEnviando(true)
    try {
      const e = await reenviarGasto(g.numero)
      setAviso(`${e.encf}: ${e.estado}${e.mensaje ? ` · ${e.mensaje}` : ''}`)
      cargar()
    } catch (e) {
      manejar(e)
    } finally {
      setEnviando(false)
    }
  }

  return (
    <section>
      {enviando && <Espera texto="Conectando con la DGII, espere..." />}
      <p className="tenue">El comprobante para sustentar lo que paga el personal por el trabajo: consumibles, pasajes y transporte, estacionamiento,
        peajes. Para pagarle a una persona sin RNC se usa la compra a informal (E41). Lo pagado en efectivo resta en su cuadre de caja.</p>
      <div className="filtros">
        <label className="corto">Desde<input type="date" value={desde} onChange={(e) => setDesde(e.target.value)} /></label>
        <label className="corto">hasta<input type="date" value={hasta} onChange={(e) => setHasta(e.target.value)} /></label>
        {d?.puedeCrear && <button type="button" onClick={() => setNuevo(true)}>Nuevo gasto menor</button>}
      </div>
      {aviso && <p className="ok-texto" role="status">{aviso}</p>}
      {error && <p className="error" role="alert">{error}</p>}
      {!d ? <p className="tenue">Cargando los gastos...</p> : (
        <div className="tabla-marco">
          <table className="tabla catalogo">
            <thead><tr><th className="num">#</th><th>Fecha</th><th>e-NCF</th><th>Descripción</th><th>Pagado a</th><th>Forma de pago</th><th className="num">Total</th><th>DGII</th><th></th></tr></thead>
            <tbody>
              {d.gastos.map((g) => (
                <tr key={g.numero}>
                  <td className="num">{g.numero}</td>
                  <td>{fecha(g.fecha, true)}</td>
                  <td>{g.encf}</td>
                  <td>{g.descripcion}</td>
                  <td>{g.beneficiario}</td>
                  <td>{g.formaPago}</td>
                  <td className="num">{dinero(g.total)}</td>
                  <td>{g.dgii ? <span className={g.dgii.aceptado ? 'ok-texto' : 'error'} title={g.dgii.mensaje ?? ''}>{g.dgii.estado}</span> : <span className="tenue">—</span>}</td>
                  <td className="acciones-fila">
                    <button type="button" className="secundario chico" onClick={() => window.open(`/gasto/${g.numero}`, '_blank')}>Imprimir</button>
                    {g.dgii && !g.dgii.aceptado && g.dgii.estado !== 'Desconocido' && (
                      <button type="button" className="chico" disabled={enviando} onClick={() => reenviar(g)}>Reenviar</button>
                    )}
                  </td>
                </tr>
              ))}
              {d.gastos.length === 0 && <tr><td colSpan={9} className="tenue">No hay gastos menores en esas fechas.</td></tr>}
            </tbody>
          </table>
        </div>
      )}
      {!d?.puedeCrear && d && <p className="tenue">Solo los usuarios autorizados pueden emitir gastos menores.</p>}
      {nuevo && d && (
        <NuevoGastoMenor formas={d.formasPago} tipos={d.tiposGasto} alVencer={alVencer}
                         alCerrar={(r) => {
                           setNuevo(false)
                           if (r) {
                             setAviso(`Gasto ${r.encf} creado. DGII: ${r.dgii.estado}${r.dgii.mensaje ? ` · ${r.dgii.mensaje}` : ''}`)
                             cargar()
                             window.open(`/gasto/${r.numero}`, '_blank')
                           }
                         }} />
      )}
    </section>
  )
}

function NuevoGastoMenor({ formas, tipos, alCerrar, alVencer }: {
  formas: Record<string, string>
  tipos: Record<string, string>
  alCerrar: (r?: { numero: number; encf: string; dgii: EstadoEcf }) => void
  alVencer: () => void
}) {
  const [g, setG] = useState({ fecha: hoyIso(), descripcion: '', bienServicio: 2, cantidad: '1', precio: '', formaPago: '01', tipoGasto: '02', beneficiario: '', cedula: '', detalle: '' })
  const [error, setError] = useState('')
  const [enviando, setEnviando] = useState(false)
  const total = Math.round(num(g.cantidad) * num(g.precio) * 100) / 100
  const cambiar = (c: Partial<typeof g>) => setG({ ...g, ...c })

  useEffect(() => {
    const tecla = (k: KeyboardEvent) => k.key === 'Escape' && !enviando && alCerrar()
    window.addEventListener('keydown', tecla)
    return () => window.removeEventListener('keydown', tecla)
  }, [alCerrar, enviando])

  const motivo = g.descripcion.trim().length < 3 ? 'la descripción'
    : num(g.cantidad) <= 0 ? 'la cantidad'
    : num(g.precio) <= 0 ? 'el precio'
    : g.beneficiario.trim().length < 3 ? 'a nombre de quién se paga'
    : g.detalle.trim().length < 10 ? 'el detalle: qué se recibió y por qué (10 letras o más)'
    : g.fecha > hoyIso() ? 'una fecha que no sea futura'
    : ''

  async function guardar(ev: FormEvent) {
    ev.preventDefault()
    if (motivo || enviando) return
    if (!window.confirm(`¿Emitir el gasto menor (E43) de ${dinero(total)} a nombre de ${g.beneficiario.trim().toUpperCase()}? Se envía a la DGII y no se puede anular.`)) return
    setEnviando(true)
    setError('')
    try {
      const n: NuevoGasto = { ...g, cantidad: num(g.cantidad), precio: num(g.precio) }
      alCerrar(await crearGasto(n))
    } catch (e) {
      if (e instanceof SesionVencida) alVencer()
      else setError(e instanceof Error ? e.message : String(e))
    } finally {
      setEnviando(false)
    }
  }

  return (
    <Fondo centro>
      {enviando && <Espera texto="Conectando con la DGII, espere..." />}
      <form className="dialogo ancho" role="dialog" aria-modal="true" aria-label="Nuevo gasto menor" onSubmit={guardar}>
        <h2>Nuevo gasto menor (E43)</h2>
        <div className="tres">
          <label>Fecha<input type="date" value={g.fecha} max={hoyIso()} onChange={(e) => cambiar({ fecha: e.target.value })} /></label>
          <label>Descripción (va en el e-CF)<input value={g.descripcion} maxLength={80} onChange={(e) => cambiar({ descripcion: e.target.value })} autoFocus
                                                  placeholder="Ej.: peaje autopista Duarte" /></label>
          <label>Es
            <select value={g.bienServicio} onChange={(e) => cambiar({ bienServicio: Number(e.target.value) })}>
              <option value={2}>Servicio</option><option value={1}>Bien</option>
            </select>
          </label>
          <label>Cantidad<input inputMode="decimal" value={g.cantidad} onChange={(e) => cambiar({ cantidad: e.target.value })} /></label>
          <label>Precio<input inputMode="decimal" value={g.precio} onChange={(e) => cambiar({ precio: e.target.value })} /></label>
          <label>Total (exento)<input value={dinero(total)} disabled /></label>
          <label>Pagado a<input value={g.beneficiario} maxLength={80} onChange={(e) => cambiar({ beneficiario: e.target.value })} placeholder="Empleado o persona" /></label>
          <label>Cédula (opcional)<input value={g.cedula} maxLength={20} onChange={(e) => cambiar({ cedula: e.target.value })} /></label>
          <label>Forma de pago
            <select value={g.formaPago} onChange={(e) => cambiar({ formaPago: e.target.value })}>
              {Object.entries(formas).map(([k, v]) => <option key={k} value={k}>{k} · {v}</option>)}
            </select>
          </label>
        </div>
        <label>Tipo de gasto (606)
          <select value={g.tipoGasto} onChange={(e) => cambiar({ tipoGasto: e.target.value })}>
            {Object.entries(tipos).map(([k, v]) => <option key={k} value={k}>{k} · {v}</option>)}
          </select>
        </label>
        <label>Detalle: qué se recibió y por qué se paga
          <textarea value={g.detalle} maxLength={500} rows={3} onChange={(e) => cambiar({ detalle: e.target.value })} />
        </label>
        {g.formaPago === '01' && <p className="tenue">En efectivo: resta en su cuadre de caja.</p>}
        {error && <p className="error" role="alert">{error}</p>}
        {!error && motivo && <p className="alerta">Falta: {motivo}.</p>}
        <div className="botones">
          <button type="button" className="secundario" onClick={() => alCerrar()} disabled={enviando}>Cancelar</button>
          <button type="submit" disabled={!!motivo || enviando}>Emitir {total > 0 ? dinero(total) : ''}</button>
        </div>
      </form>
    </Fondo>
  )
}
