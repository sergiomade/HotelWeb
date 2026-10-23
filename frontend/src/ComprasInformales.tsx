import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import Espera from './Espera'
import Fondo from './Fondo'
import {
  crearCompra, dinero, fecha, hoyIso, listarCompras, reenviarCompra, SesionVencida,
  type CompraLista, type EstadoEcf, type PantallaCompras,
} from './api'

const num = (s: string) => Number(s.replace(/,/g, '')) || 0
// como Stp_CrearCompraInformal: por linea, redondeado a 2 decimales
const r2 = (v: number) => Math.sign(v) * Math.round(Math.abs(v) * 100 + 1e-9) / 100
const PORCENTAJE: Record<string, number> = { N: 0, P: 15, A: 10, T: 3 }

interface Linea { descripcion: string; bienServicio: number; cantidad: string; precio: string; gravado: boolean }
const lineaVacia = (): Linea => ({ descripcion: '', bienServicio: 1, cantidad: '1', precio: '', gravado: false })

/**
 * Compras a proveedores informales (E41, etapa 9d), como UFrmCompraInformal de Delphi: el comprobante que emite el hotel
 * cuando compra a una persona fisica no registrada como contribuyente (Norma 05-19, Art. 7). Se retiene el 100 % del ITBIS
 * y, en los servicios, el ISR; al proveedor se le paga el neto. Lo pagado en efectivo resta en el cuadre de quien lo hizo.
 */
export default function ComprasInformales({ alVencer }: { alVencer: () => void }) {
  const [desde, setDesde] = useState(() => hoyIso(-30))
  const [hasta, setHasta] = useState(() => hoyIso())
  const [d, setD] = useState<PantallaCompras | null>(null)
  const [error, setError] = useState('')
  const [aviso, setAviso] = useState('')
  const [nueva, setNueva] = useState(false)
  const [enviando, setEnviando] = useState(false)

  const manejar = useCallback(
    (e: unknown) => (e instanceof SesionVencida ? alVencer() : setError(e instanceof Error ? e.message : String(e))),
    [alVencer],
  )
  const cargar = useCallback(() => {
    setError('')
    listarCompras(desde, hasta).then(setD).catch(manejar)
  }, [desde, hasta, manejar])
  useEffect(cargar, [cargar])

  async function reenviar(c: CompraLista) {
    if (!window.confirm(`¿Reenviar el ${c.encf} a la DGII?`)) return
    setEnviando(true)
    try {
      const e = await reenviarCompra(c.numero)
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
      <p className="tenue">Cuando el hotel le compra a una persona sin RNC (proveedor informal). Antes de emitirlo, verifique en la consulta de RNC de la
        DGII que la persona no está registrada. Se retiene todo el ITBIS y, en los servicios, el ISR; al proveedor se le paga el neto.</p>
      <div className="filtros">
        <label className="corto">Desde<input type="date" value={desde} onChange={(e) => setDesde(e.target.value)} /></label>
        <label className="corto">hasta<input type="date" value={hasta} onChange={(e) => setHasta(e.target.value)} /></label>
        {d?.puedeCrear && <button type="button" onClick={() => setNueva(true)}>Nueva compra a informal</button>}
      </div>
      {aviso && <p className="ok-texto" role="status">{aviso}</p>}
      {error && <p className="error" role="alert">{error}</p>}
      {!d ? <p className="tenue">Cargando las compras...</p> : (
        <div className="tabla-marco">
          <table className="tabla catalogo">
            <thead><tr><th className="num">#</th><th>Fecha</th><th>e-NCF</th><th>Proveedor</th><th>Cédula</th><th>Pago</th><th className="num">Total</th><th className="num">Retenido</th><th className="num">Neto pagado</th><th>DGII</th><th></th></tr></thead>
            <tbody>
              {d.compras.map((c) => (
                <tr key={c.numero}>
                  <td className="num">{c.numero}</td>
                  <td>{fecha(c.fecha, true)}</td>
                  <td>{c.encf}</td>
                  <td>{c.proveedor}</td>
                  <td>{c.cedula}</td>
                  <td>{c.formaPago}</td>
                  <td className="num">{dinero(c.total)}</td>
                  <td className="num">{dinero(c.retenido)}</td>
                  <td className="num"><strong>{dinero(c.netoPagar)}</strong></td>
                  <td>{c.dgii ? <span className={c.dgii.aceptado ? 'ok-texto' : 'error'} title={c.dgii.mensaje ?? ''}>{c.dgii.estado}</span> : <span className="tenue">—</span>}</td>
                  <td className="acciones-fila">
                    <button type="button" className="secundario chico" onClick={() => window.open(`/compra/${c.numero}`, '_blank')}>Imprimir</button>
                    {c.dgii && !c.dgii.aceptado && c.dgii.estado !== 'Desconocido' && (
                      <button type="button" className="chico" disabled={enviando} onClick={() => reenviar(c)}>Reenviar</button>
                    )}
                  </td>
                </tr>
              ))}
              {d.compras.length === 0 && <tr><td colSpan={11} className="tenue">No hay compras a informales en esas fechas.</td></tr>}
            </tbody>
          </table>
        </div>
      )}
      {!d?.puedeCrear && d && <p className="tenue">Solo los usuarios autorizados pueden emitir compras a informales.</p>}
      {nueva && d && (
        <NuevaCompraInformal d={d} alVencer={alVencer}
                             alCerrar={(r) => {
                               setNueva(false)
                               if (r) {
                                 setAviso(`Compra ${r.encf} creada. DGII: ${r.dgii.estado}${r.dgii.mensaje ? ` · ${r.dgii.mensaje}` : ''}`)
                                 cargar()
                                 window.open(`/compra/${r.numero}`, '_blank')
                               }
                             }} />
      )}
    </section>
  )
}

function NuevaCompraInformal({ d, alCerrar, alVencer }: {
  d: PantallaCompras
  alCerrar: (r?: { numero: number; encf: string; dgii: EstadoEcf }) => void
  alVencer: () => void
}) {
  const [c, setC] = useState({ fecha: hoyIso(), fechaPago: hoyIso(), cedula: '', proveedor: '', direccion: '', verificado: false, tipoIsr: 'N', formaPago: '01', tipoGasto: '02', observacion: '' })
  const [lineas, setLineas] = useState<Linea[]>([lineaVacia()])
  const [error, setError] = useState('')
  const [enviando, setEnviando] = useState(false)
  const cambiar = (x: Partial<typeof c>) => setC({ ...c, ...x })

  useEffect(() => {
    const tecla = (k: KeyboardEvent) => k.key === 'Escape' && !enviando && alCerrar()
    window.addEventListener('keydown', tecla)
    return () => window.removeEventListener('keydown', tecla)
  }, [alCerrar, enviando])

  // los mismos calculos que el SP, para ver las retenciones y el neto antes de emitir
  const calc = useMemo(() => {
    const p = PORCENTAJE[c.tipoIsr] ?? 0
    const l = lineas.map((x) => {
      const monto = r2(num(x.cantidad) * num(x.precio))
      const itbis = x.gravado ? r2(monto * 0.18) : 0
      return { monto, itbis, isr: x.bienServicio === 2 ? r2(monto * p / 100) : 0 }
    })
    const total = r2(l.reduce((s, x) => s + x.monto + x.itbis, 0))
    const itbis = r2(l.reduce((s, x) => s + x.itbis, 0))
    const isr = r2(l.reduce((s, x) => s + x.isr, 0))
    return { l, total, itbis, isr, neto: r2(total - itbis - isr) }
  }, [lineas, c.tipoIsr])

  const servicios = lineas.some((l) => l.bienServicio === 2)
  const cedula = c.cedula.replace(/\D/g, '')
  const lineaMala = lineas.findIndex((l) => l.descripcion.trim().length < 3 || num(l.cantidad) <= 0 || num(l.precio) <= 0)
  const motivo = !c.verificado ? 'confirmar que verificó en la DGII que el proveedor no está registrado'
    : cedula.length !== 11 ? 'la cédula del proveedor (11 dígitos)'
    : c.proveedor.trim().length < 3 ? 'el nombre del proveedor'
    : lineaMala >= 0 ? `completar la línea ${lineaMala + 1} (descripción, cantidad y precio)`
    : c.tipoIsr !== 'N' && !servicios ? 'quitar la retención de ISR: solo va en servicios y no hay ninguno'
    : c.tipoIsr === 'N' && servicios ? 'elegir la retención de ISR: los servicios la llevan'
    : c.fecha > hoyIso() ? 'una fecha que no sea futura'
    : c.fechaPago < c.fecha ? 'una fecha de pago no anterior al comprobante'
    : ''

  function cambiarLinea(i: number, x: Partial<Linea>) {
    setLineas(lineas.map((l, k) => (k === i ? { ...l, ...x } : l)))
  }

  async function guardar(ev: FormEvent) {
    ev.preventDefault()
    if (motivo || enviando) return
    if (!window.confirm(`¿Emitir la compra (E41) a ${c.proveedor.trim().toUpperCase()}? Total ${dinero(calc.total)}, se retienen ${dinero(calc.itbis + calc.isr)} y se le paga ${dinero(calc.neto)}. Se envía a la DGII y no se puede anular.`)) return
    setEnviando(true)
    setError('')
    try {
      alCerrar(await crearCompra({
        ...c, cedula,
        lineas: lineas.map((l) => ({ descripcion: l.descripcion.trim(), bienServicio: l.bienServicio, cantidad: num(l.cantidad), precio: num(l.precio), gravado: l.gravado })),
      }))
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
      <form className="dialogo ancho cobro" role="dialog" aria-modal="true" aria-label="Nueva compra a informal" onSubmit={guardar}>
        <h2>Nueva compra a informal (E41)</h2>
        <fieldset className="grupo-cargo">
          <legend>Proveedor</legend>
          <div className="tres">
            <label>Cédula<input value={c.cedula} maxLength={13} inputMode="numeric" onChange={(e) => cambiar({ cedula: e.target.value })} autoFocus /></label>
            <label>Nombre<input value={c.proveedor} maxLength={150} onChange={(e) => cambiar({ proveedor: e.target.value })} /></label>
            <label>Dirección (opcional)<input value={c.direccion} maxLength={100} onChange={(e) => cambiar({ direccion: e.target.value })} /></label>
          </div>
          <label className="check"><input type="checkbox" checked={c.verificado} onChange={(e) => cambiar({ verificado: e.target.checked })} />
            Verifiqué en la consulta de RNC de la DGII que NO está registrado como contribuyente</label>
        </fieldset>

        <fieldset className="grupo-cargo">
          <legend>Lo que se compra (precios sin ITBIS)</legend>
          <table className="tabla lineas">
            <thead><tr><th>Descripción</th><th>Es</th><th className="num">Cant.</th><th className="num">Precio</th><th>ITBIS</th><th className="num">Monto</th><th className="num">Retenido</th><th></th></tr></thead>
            <tbody>
              {lineas.map((l, i) => (
                <tr key={i}>
                  <td><input value={l.descripcion} maxLength={80} onChange={(e) => cambiarLinea(i, { descripcion: e.target.value })} /></td>
                  <td><select value={l.bienServicio} onChange={(e) => cambiarLinea(i, { bienServicio: Number(e.target.value) })}><option value={1}>Bien</option><option value={2}>Servicio</option></select></td>
                  <td><input inputMode="decimal" value={l.cantidad} onChange={(e) => cambiarLinea(i, { cantidad: e.target.value })} /></td>
                  <td><input inputMode="decimal" value={l.precio} onChange={(e) => cambiarLinea(i, { precio: e.target.value })} /></td>
                  <td><label className="check"><input type="checkbox" checked={l.gravado} onChange={(e) => cambiarLinea(i, { gravado: e.target.checked })} /> 18 %</label></td>
                  <td className="num">{dinero(calc.l[i].monto)}</td>
                  <td className="num">{dinero(calc.l[i].itbis + calc.l[i].isr)}</td>
                  <td>{lineas.length > 1 && <button type="button" className="secundario chico" onClick={() => setLineas(lineas.filter((_, k) => k !== i))}>Quitar</button>}</td>
                </tr>
              ))}
            </tbody>
          </table>
          {lineas.length < 20 && <button type="button" className="secundario" onClick={() => setLineas([...lineas, lineaVacia()])}>+ Otra línea</button>}
        </fieldset>

        <div className="tres">
          <label>Retención de ISR
            <select value={c.tipoIsr} onChange={(e) => cambiar({ tipoIsr: e.target.value })}>
              {Object.entries(d.retencionesIsr).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
            </select>
          </label>
          <label>Forma de pago
            <select value={c.formaPago} onChange={(e) => cambiar({ formaPago: e.target.value })}>
              {Object.entries(d.formasPago).map(([k, v]) => <option key={k} value={k}>{k} · {v}</option>)}
            </select>
          </label>
          <label>Tipo de gasto (606)
            <select value={c.tipoGasto} onChange={(e) => cambiar({ tipoGasto: e.target.value })}>
              {Object.entries(d.tiposGasto).map(([k, v]) => <option key={k} value={k}>{k} · {v}</option>)}
            </select>
          </label>
          <label>Fecha<input type="date" value={c.fecha} max={hoyIso()} onChange={(e) => cambiar({ fecha: e.target.value, fechaPago: c.fechaPago < e.target.value ? e.target.value : c.fechaPago })} /></label>
          <label>{c.formaPago === '04' ? 'Fecha límite de pago' : 'Fecha de pago'}<input type="date" value={c.fechaPago} min={c.fecha} onChange={(e) => cambiar({ fechaPago: e.target.value })} /></label>
          <label>Observación (opcional)<input value={c.observacion} maxLength={500} onChange={(e) => cambiar({ observacion: e.target.value })} /></label>
        </div>

        <dl className="totales">
          <dt>Total (con ITBIS)</dt><dd>{dinero(calc.total)}</dd>
          {calc.itbis > 0 && <><dt>ITBIS retenido (100 %)</dt><dd>-{dinero(calc.itbis)}</dd></>}
          {calc.isr > 0 && <><dt>ISR retenido ({PORCENTAJE[c.tipoIsr]} %)</dt><dd>-{dinero(calc.isr)}</dd></>}
          <dt className="saldo">Se le paga al proveedor</dt><dd className="saldo">{dinero(calc.neto)}</dd>
        </dl>
        {c.formaPago === '01' && <p className="tenue">En efectivo: el neto resta en su cuadre de caja.</p>}
        {error && <p className="error" role="alert">{error}</p>}
        {!error && motivo && <p className="alerta">Falta: {motivo}.</p>}
        <div className="botones">
          <button type="button" className="secundario" onClick={() => alCerrar()} disabled={enviando}>Cancelar</button>
          <button type="submit" disabled={!!motivo || enviando}>Emitir</button>
        </div>
      </form>
    </Fondo>
  )
}
