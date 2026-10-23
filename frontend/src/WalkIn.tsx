import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import Espera from './Espera'
import {
  crearWalkIn, dinero, obtenerServicios, opcionesReserva, reenviarWalkIn, SesionVencida,
  type EstadoEcf, type Opcion, type RespuestaWalkIn, type Servicio,
} from './api'

// los mismos de la API (ReglasCheckout.Tipos)
const TIPOS = [
  { id: 2, codigo: 'E32', nombre: 'Consumo' },
  { id: 1, codigo: 'E31', nombre: 'Crédito fiscal' },
  { id: 13, codigo: 'E44', nombre: 'Regímenes especiales' },
  { id: 14, codigo: 'E45', nombre: 'Gubernamental' },
]
// servicios que pone el sistema (pagos, PAY-OUT, adelantos): no se venden
const DEL_SISTEMA = new Set([3, 7, 11])
const PAY_OUT = 7
const DOLARES = 2

interface Linea { servicio: number; descripcion: string; cantidad: string; precio: string }
interface Pago { forma: number; descripcion: string; monto: string; us: string; referencia: string }

const num = (s: string) => Number(s.replace(/,/g, '')) || 0
// como la API: ITBIS y propina truncados a 2 decimales
const truncar = (v: number) => Math.trunc(Math.round(v * 10000) / 100) / 100

const clienteVacio = { nombre: '', cedula: '', rnc: '', telefono: '', direccion: '' }

/**
 * Walk-in (etapa 6): venta sin reserva (piscina, salon, hospedaje de paso) con e-CF, como UfrmWalking de Delphi.
 * ITBIS 18 % salvo E44; propina solo si se marca. Se cobra todo junto; la devuelta solo sale del efectivo.
 */
export default function WalkIn({ alVencer }: { alVencer: () => void }) {
  const [servicios, setServicios] = useState<Servicio[]>([])
  const [formas, setFormas] = useState<Opcion[]>([])
  const [cliente, setCliente] = useState(clienteVacio)
  const [tipo, setTipo] = useState(2)
  const [conPropina, setConPropina] = useState(false)
  const [lineas, setLineas] = useState<Linea[]>([])
  const [pagos, setPagos] = useState<Pago[]>([])
  const [error, setError] = useState('')
  const [enviando, setEnviando] = useState(false)
  const [hecho, setHecho] = useState<RespuestaWalkIn | null>(null)

  const manejar = useCallback(
    (e: unknown) => (e instanceof SesionVencida ? alVencer() : setError(e instanceof Error ? e.message : String(e))),
    [alVencer],
  )

  useEffect(() => {
    obtenerServicios().then((r) => setServicios(r.servicios.filter((s) => !DEL_SISTEMA.has(s.id)))).catch(manejar)
    opcionesReserva().then((o) => setFormas(o.formasPagoAdelanto.filter((f) => f.id !== PAY_OUT))).catch(manejar)
  }, [manejar])

  const nuevoPago = useCallback((monto = ''): Pago =>
    ({ forma: 1, descripcion: formas.find((f) => f.id === 1)?.nombre ?? 'EFECTIVO', monto, us: '', referencia: '' }), [formas])

  const sinId = cliente.rnc.replace(/0/g, '').trim() === '' && cliente.cedula.replace(/0/g, '').trim() === ''
  const tipos = sinId ? TIPOS.filter((t) => t.id === 2) : TIPOS
  const tipoActual = tipos.find((t) => t.id === tipo) ?? tipos[0]

  const tot = useMemo(() => {
    const sub = Math.round(lineas.reduce((s, l) => s + Math.round(num(l.cantidad) * num(l.precio) * 100) / 100, 0) * 100) / 100
    const itbis = tipoActual.id === 13 ? 0 : truncar(sub * 0.18)
    const propina = conPropina ? truncar(sub * 0.1) : 0
    return { sub, itbis, propina, total: Math.round((sub + itbis + propina) * 100) / 100 }
  }, [lineas, tipoActual.id, conPropina])

  const recibido = Math.round(pagos.reduce((s, p) => s + num(p.monto), 0) * 100) / 100
  const devuelta = Math.max(0, Math.round((recibido - tot.total) * 100) / 100)

  function agregarServicio(id: number) {
    const s = servicios.find((x) => x.id === id)
    if (!s) return
    setLineas([...lineas, { servicio: s.id, descripcion: s.descripcion.replace(/\s*->.*$/, ' ->').trim(), cantidad: '1', precio: s.precio ? String(s.precio) : '' }])
  }

  function cambiarLinea(i: number, c: Partial<Linea>) {
    setLineas(lineas.map((l, k) => (k === i ? { ...l, ...c } : l)))
  }

  function cambiarPago(i: number, c: Partial<Pago>) {
    setPagos(pagos.map((p, k) => {
      if (k !== i) return p
      const n = { ...p, ...c }
      if (c.forma !== undefined) n.descripcion = formas.find((f) => f.id === c.forma)?.nombre ?? ''
      return n
    }))
  }

  async function enviar(ev: FormEvent) {
    ev.preventDefault()
    setError('')
    if (!window.confirm(`¿Facturar el walk-in de ${cliente.nombre.trim().toUpperCase()} por ${dinero(tot.total)} con ${tipoActual.codigo}?${devuelta > 0 ? ` Devuelta: ${dinero(devuelta)}.` : ''} Se envía a la DGII.`)) return
    setEnviando(true)
    try {
      setHecho(await crearWalkIn({
        nombre: cliente.nombre, cedula: cliente.cedula, rnc: cliente.rnc, telefono: cliente.telefono, direccion: cliente.direccion,
        tipo: tipoActual.id, conPropina,
        lineas: lineas.map((l) => ({ servicio: l.servicio, descripcion: l.descripcion, cantidad: Math.trunc(num(l.cantidad)), precio: num(l.precio) })),
        pagos: pagos.map((p) => ({
          formaPago: p.forma, descripcion: p.descripcion, monto: num(p.monto), montoUs: p.forma === DOLARES ? num(p.us) : null,
          noCheque: p.forma === 3 ? p.referencia || null : null, banco: p.forma !== 3 && p.forma !== 1 ? p.referencia || null : null,
        })),
      }))
    } catch (e) {
      manejar(e)
    } finally {
      setEnviando(false)
    }
  }

  function nuevo() {
    setHecho(null)
    setCliente(clienteVacio)
    setTipo(2)
    setConPropina(false)
    setLineas([])
    setPagos([])
    setError('')
  }

  if (hecho) return <Hecho r={hecho} alNuevo={nuevo} alError={manejar} />

  const listo = cliente.nombre.trim() !== '' && lineas.length > 0 && pagos.length > 0 && recibido >= tot.total && tot.total > 0
  return (
    <section className="catalogos">
      {enviando && <Espera texto="Conectando con la DGII, espere..." />}
      <h2>Walk-in</h2>
      <form className="walkin" onSubmit={enviar}>
        <fieldset className="grupo-cargo">
          <legend>Cliente</legend>
          <div className="tres">
            <label>Nombre *<input value={cliente.nombre} maxLength={50} onChange={(e) => setCliente({ ...cliente, nombre: e.target.value })} autoFocus /></label>
            <label>Cédula<input value={cliente.cedula} maxLength={11} inputMode="numeric" placeholder="11 dígitos"
                                onChange={(e) => setCliente({ ...cliente, cedula: e.target.value.replace(/\D/g, '') })} /></label>
            <label>RNC<input value={cliente.rnc} maxLength={11} placeholder="En blanco: consumidor final"
                             onChange={(e) => setCliente({ ...cliente, rnc: e.target.value.trim() })} /></label>
            <label>Teléfono<input value={cliente.telefono} maxLength={13} onChange={(e) => setCliente({ ...cliente, telefono: e.target.value })} /></label>
            <label>Dirección<input value={cliente.direccion} maxLength={50} onChange={(e) => setCliente({ ...cliente, direccion: e.target.value })} /></label>
            <label>Comprobante
              <select value={tipoActual.id} onChange={(e) => setTipo(Number(e.target.value))}>
                {tipos.map((t) => <option key={t.id} value={t.id}>{t.codigo} · {t.nombre}</option>)}
              </select>
            </label>
          </div>
          <label className="check"><input type="checkbox" checked={conPropina} onChange={(e) => setConPropina(e.target.checked)} /> Con propina (10 %)</label>
          {tipoActual.id === 2 && tot.total >= 250000 && <p className="alerta">Consumo de 250,000 o más: la DGII pide la cédula o el RNC del cliente.</p>}
        </fieldset>

        <fieldset className="grupo-cargo">
          <legend>Servicios</legend>
          <label>Agregar
            <select value="" onChange={(e) => agregarServicio(Number(e.target.value))}>
              <option value="">Escoja un servicio...</option>
              {servicios.map((s) => <option key={s.id} value={s.id}>{s.id} · {s.descripcion}{s.precio ? ` (${dinero(s.precio)})` : ''}</option>)}
            </select>
          </label>
          {lineas.length > 0 && (
            <table className="tabla">
              <thead><tr><th>Descripción</th><th className="num">Cant.</th><th className="num">Precio (sin impuestos)</th><th className="num">Importe</th><th></th></tr></thead>
              <tbody>
                {lineas.map((l, i) => (
                  <tr key={i}>
                    <td><input value={l.descripcion} maxLength={50} onChange={(e) => cambiarLinea(i, { descripcion: e.target.value })} /></td>
                    <td className="num"><input className="numero corto" inputMode="numeric" value={l.cantidad}
                                               onChange={(e) => cambiarLinea(i, { cantidad: e.target.value.replace(/\D/g, '').slice(0, 3) })} /></td>
                    <td className="num"><input className="numero ancho" inputMode="decimal" value={l.precio} onChange={(e) => cambiarLinea(i, { precio: e.target.value })} /></td>
                    <td className="num">{dinero(Math.round(num(l.cantidad) * num(l.precio) * 100) / 100)}</td>
                    <td><button type="button" className="secundario chico" onClick={() => setLineas(lineas.filter((_, k) => k !== i))}>Quitar</button></td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
          <dl className="totales">
            <dt>Subtotal</dt><dd>{dinero(tot.sub)}</dd>
            <dt>ITBIS 18 %</dt><dd>{dinero(tot.itbis)}</dd>
            {conPropina && <><dt>Propina 10 %</dt><dd>{dinero(tot.propina)}</dd></>}
            <dt className="saldo">Total</dt><dd className="saldo">{dinero(tot.total)}</dd>
          </dl>
        </fieldset>

        <fieldset className="grupo-cargo">
          <legend>Pago</legend>
          {pagos.map((p, i) => (
            <div key={i} className="cuatro">
              <label>Forma
                <select value={p.forma} onChange={(e) => cambiarPago(i, { forma: Number(e.target.value) })}>
                  {formas.map((f) => <option key={f.id} value={f.id}>{f.nombre}</option>)}
                </select>
              </label>
              <label>Monto RD$<input inputMode="decimal" value={p.monto} onChange={(e) => cambiarPago(i, { monto: e.target.value })} /></label>
              {p.forma === DOLARES
                ? <label>Recibido US$<input inputMode="decimal" value={p.us} onChange={(e) => cambiarPago(i, { us: e.target.value })} /></label>
                : p.forma !== 1 ? <label>{p.forma === 3 ? 'No. cheque' : 'Banco / tarjeta'}<input value={p.referencia} maxLength={p.forma === 3 ? 15 : 50} onChange={(e) => cambiarPago(i, { referencia: e.target.value })} /></label>
                : <span />}
              <button type="button" className="secundario chico" onClick={() => setPagos(pagos.filter((_, k) => k !== i))}>Quitar</button>
            </div>
          ))}
          <div className="botones izquierda">
            <button type="button" className="secundario" onClick={() => setPagos([...pagos, nuevoPago(pagos.length === 0 && tot.total > 0 ? String(tot.total) : '')])}>Agregar forma de pago</button>
          </div>
          <dl className="totales">
            <dt>Recibido</dt><dd>{dinero(recibido)}</dd>
            {recibido < tot.total && <><dt>Falta</dt><dd className="mal">{dinero(tot.total - recibido)}</dd></>}
            {devuelta > 0 && <><dt className="saldo">Devuelta</dt><dd className="saldo">{dinero(devuelta)}</dd></>}
          </dl>
          {devuelta > 0 && <p className="tenue">La devuelta sale del efectivo; en el recibo se graba lo aplicado ({dinero(tot.total)}).</p>}
        </fieldset>

        {error && <p className="error" role="alert">{error}</p>}
        <div className="botones">
          <button type="button" className="secundario" onClick={nuevo}>Limpiar</button>
          <button type="submit" className="boton-pago" disabled={enviando || !listo}>Cobrar y facturar</button>
        </div>
      </form>
    </section>
  )
}

function Hecho({ r, alNuevo, alError }: { r: RespuestaWalkIn; alNuevo: () => void; alError: (e: unknown) => void }) {
  const [dgii, setDgii] = useState<EstadoEcf>(r.dgii)
  const [enviando, setEnviando] = useState(false)
  const w = r.walkIn

  async function reenviar() {
    setEnviando(true)
    try {
      setDgii(await reenviarWalkIn(w.numero))
    } catch (e) {
      alError(e)
    } finally {
      setEnviando(false)
    }
  }

  return (
    <section className="catalogos">
      {enviando && <Espera texto="Conectando con la DGII, espere..." />}
      <h2>Walk-in #{w.numero}</h2>
      <div role="status" className="grupo-cargo">
        <p className="ok-texto">Facturado: {w.encf} · {dinero(w.total)}</p>
        {w.devuelta > 0 && <p className="devuelta">Devuelta: <strong>{dinero(w.devuelta)}</strong> <span className="tenue">(recibido {dinero(w.recibido)})</span></p>}
        <p className={dgii.aceptado ? 'ok-texto' : 'error'}>DGII: {dgii.estado}{dgii.mensaje ? ` · ${dgii.mensaje}` : ''}</p>
        {!dgii.aceptado && <p className="tenue">El walk-in ya está grabado con su e-NCF: si no llegó o lo rechazaron, corrija lo que diga la DGII y reenvíelo.</p>}
        <div className="botones izquierda">
          <button type="button" onClick={() => window.open(`/walkin/${w.numero}`, '_blank')}>Imprimir ticket</button>
          {!dgii.aceptado && <button type="button" className="secundario" disabled={enviando} onClick={reenviar}>Reenviar a la DGII</button>}
          <button type="button" className="secundario" onClick={alNuevo}>Nuevo walk-in</button>
        </div>
      </div>
    </section>
  )
}
