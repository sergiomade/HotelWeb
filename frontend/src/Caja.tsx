import { useCallback, useEffect, useState, type FormEvent } from 'react'
import {
  cerrarCaja, cierresCaja, cuadreCaja, dinero, fecha, puede, SesionVencida,
  type AmbitoCaja, type CierreResumen, type CuadreCaja, type ResumenCaja,
} from './api'

const num = (s: string) => Number(s.replace(/,/g, '')) || 0
const r2 = (v: number) => Math.round(v * 100) / 100
const CLASE: Record<string, string> = {
  Efectivo: 'en la gaveta', Dolares: 'en la gaveta (US$)', OtroDinero: 'no va a la gaveta', SinDinero: 'no es dinero', Salida: 'sale de la gaveta',
}

/**
 * Cuadre y cierre de caja del cajero (etapa 9a): lo que tiene pendiente (hotel: recibos y adelantos sin postear;
 * cafeteria: lo cobrado en las mesas), el resumen por forma de pago, el efectivo contado con la diferencia y el cierre.
 * Cada cajero cierra solo lo suyo (decision del usuario).
 */
export default function Caja({ ambito, alVencer }: { ambito: AmbitoCaja; alVencer: () => void }) {
  const [c, setC] = useState<CuadreCaja | null>(null)
  const [cierres, setCierres] = useState<CierreResumen[]>([])
  const [efectivo, setEfectivo] = useState('')
  const [dolares, setDolares] = useState('')
  const [comentario, setComentario] = useState('')
  const [error, setError] = useState('')
  const [hecho, setHecho] = useState<CierreResumen | null>(null)
  const [enviando, setEnviando] = useState(false)
  const [verDetalle, setVerDetalle] = useState(false)

  const manejar = useCallback(
    (e: unknown) => (e instanceof SesionVencida ? alVencer() : setError(e instanceof Error ? e.message : String(e))),
    [alVencer],
  )

  const cargar = useCallback(() => {
    setError('')
    cuadreCaja(ambito).then(setC).catch(manejar)
    cierresCaja(ambito).then(setCierres).catch(manejar)
  }, [ambito, manejar])

  useEffect(cargar, [cargar])

  if (!c) return error ? <p className="error">{error}</p> : <p className="tenue">Calculando el cuadre...</p>
  const r = c.resumen
  const hotel = ambito === 'hotel'
  const difEf = efectivo === '' ? null : r2(num(efectivo) - r.efectivo)
  const difUs = !hotel || dolares === '' ? null : r2(num(dolares) - r.dolares)
  const hayDif = (difEf ?? 0) !== 0 || (difUs ?? 0) !== 0
  const faltaContar = efectivo === '' || (hotel && r.dolares > 0 && dolares === '')
  const motivo = c.movimientos.length === 0 ? 'No tiene nada pendiente.'
    : faltaContar ? 'Escriba lo que contó.'
    : hayDif && !comentario.trim() ? 'Hay diferencia: escriba el motivo.'
    : ''

  async function cerrar(ev: FormEvent) {
    ev.preventDefault()
    if (motivo || enviando || !c) return
    if (!window.confirm(`¿Cerrar su turno de ${hotel ? 'la caja del hotel' : 'la cafetería'}? Total ${dinero(r.ingresos)}, efectivo contado ${dinero(num(efectivo))}${hayDif ? ' (con diferencia)' : ''}.`)) return
    // la pestana del reporte se abre antes de esperar a la API: si no, el navegador la bloquea
    const w = window.open('', '_blank')
    setEnviando(true)
    setError('')
    try {
      const h = await cerrarCaja(ambito, {
        efectivo: num(efectivo), dolares: hotel ? num(dolares) : 0, comentario: comentario.trim(),
        movimientos: c.movimientos.length, ingresos: r.ingresos,
      })
      setHecho(h)
      setEfectivo('')
      setDolares('')
      setComentario('')
      if (w) w.location.href = `/cierre/${h.cierre}`
      cargar()
    } catch (e) {
      w?.close()
      manejar(e)
      cargar()
    } finally {
      setEnviando(false)
    }
  }

  return (
    <section className="caja">
      <div className="filtros">
        <p>Cajero: <strong>{c.usuario}</strong>{c.desde && <span className="tenue"> · cobros desde el {fecha(c.desde, true)}</span>}</p>
        <button type="button" className="secundario" onClick={cargar}>Actualizar</button>
      </div>
      {hecho && <p className="ok-texto" role="status">Cierre #{hecho.cierre} hecho. Se abrió el reporte para imprimir.</p>}

      <div className="caja-cuerpo">
        <div>
          <h3>Pendiente de cerrar ({c.movimientos.length} {c.movimientos.length === 1 ? 'pago' : 'pagos'})</h3>
          <Resumen r={r} hotel={hotel} />
          {c.movimientos.length > 0 && (
            <details open={verDetalle} onToggle={(e) => setVerDetalle((e.target as HTMLDetailsElement).open)}>
              <summary>Ver el detalle</summary>
              <div className="tabla-marco">
                <table className="tabla catalogo">
                  <thead><tr><th>{hotel ? 'Recibo' : 'Factura'}</th><th>Fecha</th><th>{hotel ? 'Hab. / reserva' : 'Mesa'}</th><th>Nombre</th><th>Forma</th><th className="num">Monto</th></tr></thead>
                  <tbody>
                    {c.movimientos.map((m, i) => (
                      <tr key={i}>
                        <td>{m.tipo === 'A' ? `Adelanto ${m.numero}` : m.numero}</td>
                        <td>{fecha(m.fecha, true)}</td>
                        <td>{m.referencia}</td>
                        <td>{m.nombre}</td>
                        <td>{m.formaNombre}</td>
                        <td className="num">{dinero(m.monto)}{m.montoUs ? <span className="tenue fila-sub">US$ {dinero(m.montoUs)}</span> : null}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </details>
          )}
        </div>

        {!puede(hotel ? 'cerrarCajaHotel' : 'cerrarCajaCafeteria') ? (
          <div className="grupo-cargo caja-cierre">
            <h3>Cerrar el turno</h3>
            <p className="tenue">Su usuario puede ver el cuadre, pero no cerrar el turno. Pídale el cierre a quien tenga ese permiso.</p>
          </div>
        ) : (
        <form className="grupo-cargo caja-cierre" onSubmit={cerrar}>
          <h3>Cerrar el turno</h3>
          <label>Efectivo contado (RD$)
            <input inputMode="decimal" value={efectivo} onChange={(e) => setEfectivo(e.target.value)} placeholder={`Esperado ${dinero(r.efectivo)}`} />
          </label>
          {difEf !== null && <p className={difEf === 0 ? 'ok-texto' : 'error'}>{difEf === 0 ? 'Cuadra.' : difEf > 0 ? `Sobran ${dinero(difEf)}` : `Faltan ${dinero(-difEf)}`}</p>}
          {hotel && (
            <>
              <label>Dólares contados (US$)
                <input inputMode="decimal" value={dolares} onChange={(e) => setDolares(e.target.value)} placeholder={`Esperado ${dinero(r.dolares)}`} />
              </label>
              {difUs !== null && <p className={difUs === 0 ? 'ok-texto' : 'error'}>{difUs === 0 ? 'Cuadra.' : difUs > 0 ? `Sobran US$ ${dinero(difUs)}` : `Faltan US$ ${dinero(-difUs)}`}</p>}
            </>
          )}
          <label>Comentario{hayDif ? ' (obligatorio: explique la diferencia)' : ''}
            <input value={comentario} maxLength={200} onChange={(e) => setComentario(e.target.value)} />
          </label>
          {error && <p className="error" role="alert">{error}</p>}
          {!error && motivo && c.movimientos.length > 0 && <p className="alerta">Falta: {motivo.replace(/\.$/, '')}.</p>}
          <div className="botones"><button type="submit" disabled={!!motivo || enviando}>Cerrar el turno</button></div>
          <p className="tenue">Cierra solo lo suyo. {hotel ? 'Los recibos y adelantos quedan posteados, como en el cuadre de Delphi.' : ''}</p>
        </form>
        )}
      </div>

      {cierres.length > 0 && (
        <>
          <h3>Mis cierres</h3>
          <div className="tabla-marco">
            <table className="tabla catalogo">
              <thead><tr><th>#</th><th>Fecha</th><th className="num">Total</th><th className="num">Efectivo esperado</th><th className="num">Contado</th><th className="num">Diferencia</th><th></th></tr></thead>
              <tbody>
                {cierres.map((x) => {
                  const d = r2(x.contadoEfectivo - x.esperadoEfectivo) + r2(x.contadoDolares - x.esperadoDolares)
                  return (
                    <tr key={x.cierre}>
                      <td>{x.cierre}</td>
                      <td>{new Date(x.hasta).toLocaleString('es-DO', { dateStyle: 'short', timeStyle: 'short' })}</td>
                      <td className="num">{dinero(x.total)}</td>
                      <td className="num">{dinero(x.esperadoEfectivo)}</td>
                      <td className="num">{dinero(x.contadoEfectivo)}</td>
                      <td className={`num ${d === 0 ? '' : 'mal'}`}>{d === 0 ? '—' : dinero(r2(x.contadoEfectivo - x.esperadoEfectivo))}{x.contadoDolares !== x.esperadoDolares && <span className="fila-sub">US$ {dinero(r2(x.contadoDolares - x.esperadoDolares))}</span>}</td>
                      <td><button type="button" className="secundario chico" onClick={() => window.open(`/cierre/${x.cierre}`, '_blank')}>Imprimir</button></td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        </>
      )}
    </section>
  )
}

export function Resumen({ r, hotel }: { r: ResumenCaja; hotel: boolean }) {
  return (
    <>
      <table className="tabla">
        <thead><tr><th>Forma de pago</th><th className="num">Pagos</th><th className="num">Monto RD$</th>{hotel && <th className="num">US$</th>}<th></th></tr></thead>
        <tbody>
          {r.formas.map((f) => (
            <tr key={f.forma} className={f.clase === 'SinDinero' ? 'apagada' : ''}>
              <td>{f.nombre}</td>
              <td className="num">{f.cantidad}</td>
              <td className="num">{f.clase === 'Salida' ? `-${dinero(f.monto)}` : dinero(f.monto)}</td>
              {hotel && <td className="num">{f.montoUs ? dinero(f.montoUs) : ''}</td>}
              <td className="tenue">{CLASE[f.clase]}</td>
            </tr>
          ))}
          {r.formas.length === 0 && <tr><td colSpan={5} className="tenue">Nada pendiente.</td></tr>}
        </tbody>
      </table>
      <dl className="totales">
        <dt>Total cobrado (dinero)</dt><dd>{dinero(r.ingresos)}</dd>
        {r.sinDinero > 0 && <><dt className="tenue">Crédito / sin dinero</dt><dd className="tenue">{dinero(r.sinDinero)}</dd></>}
        <dt className="saldo">Efectivo esperado</dt><dd className="saldo">{dinero(r.efectivo)}</dd>
        {hotel && r.dolares > 0 && <><dt className="saldo">Dólares esperados</dt><dd className="saldo">US$ {dinero(r.dolares)}</dd></>}
      </dl>
    </>
  )
}
