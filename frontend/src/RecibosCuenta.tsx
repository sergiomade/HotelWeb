import { useCallback, useEffect, useState, type FormEvent } from 'react'
import {
  anularRecibo, dinero, fecha, hacerRecibo, opcionesReserva, recibosDeCuenta, SesionVencida,
  type Opcion, type PagoRecibo, type ReciboPago,
} from './api'

const DOLARES = 2
const CHEQUE = 3
const TARJETA = 4
const PAYOUT = 7

interface Linea { forma: number; descripcion: string; monto: string; us: string; cheque: string; banco: string }

const nueva = (formas: Opcion[], forma = 1): Linea =>
  ({ forma, descripcion: formas.find((f) => f.id === forma)?.nombre ?? '', monto: '', us: '', cheque: '', banco: '' })

/** Recibos de pago de la estadia: registrar uno (varias formas de pago) y anular uno antes del cuadre. */
export default function RecibosCuenta({ habitacion, saldo, anula, abierto, alCerrar, alCambiar, alError }: {
  habitacion: number
  saldo: number
  anula: boolean
  /** el formulario lo abre la barra de la cuenta */
  abierto: boolean
  alCerrar: () => void
  alCambiar: () => void
  alError: (e: unknown) => void
}) {
  const [recibos, setRecibos] = useState<ReciboPago[]>([])
  const [formas, setFormas] = useState<Opcion[]>([])
  const [concepto, setConcepto] = useState('Abono o Saldo a Cuenta')
  const [lineas, setLineas] = useState<Linea[]>([])
  const [error, setError] = useState('')
  const [enviando, setEnviando] = useState(false)

  const cargar = useCallback(() => {
    recibosDeCuenta(habitacion).then(setRecibos).catch(alError)
  }, [habitacion, alError])

  useEffect(() => {
    cargar()
    opcionesReserva().then((o) => setFormas(o.formasPagoAdelanto)).catch(alError)
  }, [cargar, alError])

  // al abrirse: como sugerencia, el saldo pendiente en efectivo
  useEffect(() => {
    if (!abierto || formas.length === 0) return
    setLineas([{ ...nueva(formas), monto: saldo > 0 ? String(saldo) : '' }])
    setConcepto('Abono o Saldo a Cuenta')
    setError('')
    // solo al abrir (el saldo cambia despues de registrar)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [abierto, formas])

  const cambiar = (i: number, c: Partial<Linea>) => setLineas((l) => l.map((x, j) => {
    if (j !== i) return x
    const n = { ...x, ...c }
    // al cambiar la forma, la descripcion sigue a la forma (si no la habian escrito)
    if (c.forma !== undefined && (x.descripcion === '' || x.descripcion === formas.find((f) => f.id === x.forma)?.nombre))
      n.descripcion = formas.find((f) => f.id === c.forma)?.nombre ?? ''
    return n
  }))

  const num = (s: string) => Number(s.replace(/,/g, '')) || 0
  // el PAY-OUT se escribe en positivo en la pantalla y se manda en negativo, como lo guarda Delphi
  const monto = (l: Linea) => (l.forma === PAYOUT ? -Math.abs(num(l.monto)) : num(l.monto))
  const total = lineas.reduce((s, l) => s + monto(l), 0)

  async function enviar(ev: FormEvent) {
    ev.preventDefault()
    const pagos: PagoRecibo[] = lineas.map((l) => ({
      formaPago: l.forma, descripcion: l.descripcion, monto: Math.round(monto(l) * 100) / 100,
      montoUs: l.forma === DOLARES ? num(l.us) || null : null, noCheque: l.cheque || null, banco: l.banco || null,
    }))
    if (!window.confirm(`¿Registrar un recibo de ${dinero(total)} a la habitación ${habitacion}?`)) return
    setEnviando(true)
    setError('')
    try {
      await hacerRecibo(habitacion, concepto, pagos)
      alCerrar()
      cargar()
      alCambiar()
    } catch (e) {
      if (e instanceof SesionVencida) alError(e)
      else setError(e instanceof Error ? e.message : String(e))
    } finally {
      setEnviando(false)
    }
  }

  async function anular(r: ReciboPago) {
    if (!window.confirm(`¿Anular el recibo ${r.numero} de ${dinero(r.monto)}? El pago sale de la cuenta y el recibo queda anulado (no se borra).`)) return
    try {
      await anularRecibo(r.numero)
      cargar()
      alCambiar()
    } catch (e) {
      alError(e)
    }
  }

  return (
    <section className="recibos-cuenta">
      {abierto && lineas.length > 0 && (
        <form className="grupo-cargo" onSubmit={enviar}>
          <label>Concepto<input maxLength={200} value={concepto} onChange={(e) => setConcepto(e.target.value)} /></label>
          {lineas.map((l, i) => (
            <div key={i} className="pago-linea">
              <label>Forma
                <select value={l.forma} onChange={(e) => cambiar(i, { forma: Number(e.target.value) })}>
                  {formas.map((f) => <option key={f.id} value={f.id}>{f.nombre}</option>)}
                </select>
              </label>
              <label>Descripción<input maxLength={40} value={l.descripcion} onChange={(e) => cambiar(i, { descripcion: e.target.value })} /></label>
              <label>{l.forma === PAYOUT ? 'PAY-OUT (sale)' : 'Monto RD$'}<input inputMode="decimal" value={l.monto} onChange={(e) => cambiar(i, { monto: e.target.value })} /></label>
              {l.forma === DOLARES && <label>US$<input inputMode="decimal" value={l.us} onChange={(e) => cambiar(i, { us: e.target.value })} /></label>}
              {l.forma === CHEQUE && <label>No. cheque<input maxLength={15} value={l.cheque} onChange={(e) => cambiar(i, { cheque: e.target.value })} /></label>}
              {(l.forma === TARJETA || l.forma === CHEQUE) && <label>Banco / tarjeta<input maxLength={50} value={l.banco} onChange={(e) => cambiar(i, { banco: e.target.value })} /></label>}
              {lineas.length > 1 && <button type="button" className="secundario chico" onClick={() => setLineas(lineas.filter((_, j) => j !== i))}>Quitar</button>}
            </div>
          ))}
          <div className="rack-cabecera">
            <button type="button" className="secundario" onClick={() => setLineas([...lineas, nueva(formas, 4)])}>Otra forma de pago</button>
            <strong>Total {dinero(total)}</strong>
          </div>
          <p className="tenue">El PAY-OUT es dinero que el hotel da por el huésped: entra a la cuenta como cargo.</p>
          {error && <p className="error" role="alert">{error}</p>}
          <div className="botones">
            <button type="button" className="secundario" onClick={alCerrar}>Cancelar</button>
            <button type="submit" disabled={enviando || total <= 0 || lineas.some((l) => !num(l.monto) || !l.descripcion.trim())}>
              {enviando ? 'Registrando...' : 'Registrar recibo'}
            </button>
          </div>
        </form>
      )}

      {recibos.length > 0 && (
        <>
          <h3>Recibos de esta estadía</h3>
          <table className="tabla">
            <thead><tr><th>No.</th><th>Fecha</th><th>Formas de pago</th><th className="num">Monto</th><th>Estado</th>{anula && <th></th>}</tr></thead>
            <tbody>
              {recibos.map((r) => (
                <tr key={r.numero} className={r.estatus === 'Activo' ? '' : 'inactiva'}>
                  <td>{r.numero}</td>
                  <td>{fecha(r.fecha)}</td>
                  <td>{r.pagos.map((p) => `${p.formaPagoNombre} ${dinero(p.monto)}`).join(' · ')}</td>
                  <td className="num">{dinero(r.monto)}</td>
                  <td>{r.estatus}{r.estatus === 'Activo' && r.posteado ? ' (en cuadre)' : ''}</td>
                  {anula && <td>{r.estatus === 'Activo' && !r.posteado && <button type="button" className="secundario chico" onClick={() => anular(r)}>Anular</button>}</td>}
                </tr>
              ))}
            </tbody>
          </table>
        </>
      )}
    </section>
  )
}
