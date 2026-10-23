import { useEffect, useMemo, useState, type FormEvent } from 'react'
import Espera from './Espera'
import Fondo from './Fondo'
import {
  bancosTarjeta, buscarClientes, cargarAHabitacion, clienteCobro, cobrarMesa, dinero, fecha, habitacionesCafeteria, nombreDgii,
  reenviarCafeteria, SesionVencida,
  type BancoTarjeta, type ClienteCobro, type ClienteResumen, type CuentaMesa, type EstadoEcf, type HabitacionCafeteria,
  type RespuestaCobro, type ResultadoCargo,
} from './api'

// los mismos de la API (ReglasCheckout.Tipos)
const TIPOS = [
  { id: 2, codigo: 'E32', nombre: 'Consumo' },
  { id: 1, codigo: 'E31', nombre: 'Crédito fiscal' },
  { id: 13, codigo: 'E44', nombre: 'Regímenes especiales' },
  { id: 14, codigo: 'E45', nombre: 'Gubernamental' },
]
const EFECTIVO = 1
const TARJETA = 2
const CREDITO = 3
const FORMAS = [{ id: EFECTIVO, nombre: 'Efectivo' }, { id: TARJETA, nombre: 'Tarjeta' }, { id: CREDITO, nombre: 'Crédito (CxC)' }]

interface Pago { forma: number; monto: string; banco: number | null }

const num = (s: string) => Number(s.replace(/,/g, '')) || 0
const r2 = (v: number) => Math.round(v * 100) / 100
// como la API: ITBIS y propina truncados; el total sale de los montos sin truncar
const truncar = (v: number) => Math.trunc(Math.round(v * 10000) / 100) / 100
const sinId = (s: string) => s.replace(/[-\s]/g, '').replace(/0/g, '') === ''

/**
 * Cobro de la mesa con e-CF (etapa 8b), como UFrmPagar de Delphi: comprobante, cliente (de contado o de CxC), pagos
 * (efectivo, tarjeta, credito solo a un cliente de CxC) y despues el envio a la DGII y el ticket.
 */
export default function CobroCafeteria({ cuenta, alCerrar, alCobrar, alVencer }: {
  cuenta: CuentaMesa
  alCerrar: () => void
  alCobrar: () => void
  alVencer: () => void
}) {
  const [bancos, setBancos] = useState<BancoTarjeta[]>([])
  const [cliente, setCliente] = useState<ClienteCobro | null>(null)
  const [buscar, setBuscar] = useState('')
  const [hallados, setHallados] = useState<ClienteResumen[]>([])
  const [rnc, setRnc] = useState('')
  const [nombre, setNombre] = useState('')
  const [tipo, setTipo] = useState(2)
  const [pagos, setPagos] = useState<Pago[]>([])
  const [error, setError] = useState('')
  const [enviando, setEnviando] = useState(false)
  const [hecho, setHecho] = useState<RespuestaCobro | null>(null)
  const [modo, setModo] = useState<'cobrar' | 'habitacion'>('cobrar')
  const [cargado, setCargado] = useState<ResultadoCargo | null>(null)

  const manejar = (e: unknown) => (e instanceof SesionVencida ? alVencer() : setError(e instanceof Error ? e.message : String(e)))

  useEffect(() => {
    bancosTarjeta().then(setBancos).catch(manejar)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  useEffect(() => {
    const tecla = (k: KeyboardEvent) => k.key === 'Escape' && !enviando && !hecho && alCerrar()
    window.addEventListener('keydown', tecla)
    return () => window.removeEventListener('keydown', tecla)
  }, [alCerrar, enviando, hecho])

  // buscar clientes de CxC (por nombre, RNC o numero) a medida que se escribe
  useEffect(() => {
    const t = buscar.trim()
    if (t.length < 2) { setHallados([]); return }
    const h = setTimeout(() => buscarClientes(t).then((r) => setHallados(r.pagina.clientes.filter((c) => c.activo && c.numero > 2).slice(0, 8))).catch(manejar), 300)
    return () => clearTimeout(h)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [buscar])

  const tipos = sinId(rnc) ? TIPOS.filter((t) => t.id === 2) : TIPOS
  const tipoActual = tipos.find((t) => t.id === tipo) ?? tipos[0]

  const tot = useMemo(() => {
    const sub = cuenta.totales.subTotal
    const conItbis = (cliente ? cliente.itbis : cuenta.conItbis) && tipoActual.id !== 13
    const conPropina = cliente ? cliente.propina : cuenta.conPropina
    const i = conItbis ? sub * 0.18 : 0
    const p = conPropina ? sub * 0.1 : 0
    return { sub, itbis: truncar(i), propina: truncar(p), total: truncar(sub + i + p) }
  }, [cuenta, cliente, tipoActual.id])

  const recibido = r2(pagos.reduce((s, p) => s + num(p.monto), 0))
  const otros = r2(pagos.filter((p) => p.forma !== EFECTIVO).reduce((s, p) => s + num(p.monto), 0))
  const devuelta = Math.max(0, r2(recibido - tot.total))
  const falta = Math.max(0, r2(tot.total - recibido))
  const admiteCredito = !!cliente?.credito

  async function elegir(c: ClienteResumen) {
    setError('')
    try {
      const d = await clienteCobro(c.numero)
      setCliente(d)
      setRnc(d.rnc ?? '')
      setNombre(d.nombre)
      if (d.tipo && TIPOS.some((t) => t.id === d.tipo) && (d.tipo === 2 || d.rnc)) setTipo(d.tipo)
      setBuscar('')
      setHallados([])
    } catch (e) {
      manejar(e)
    }
  }

  function quitarCliente() {
    setCliente(null)
    setRnc('')
    setNombre('')
    setTipo(2)
    setPagos(pagos.filter((p) => p.forma !== CREDITO))
  }

  async function alSalirRnc() {
    const limpio = rnc.replace(/[-\s]/g, '')
    if (sinId(limpio) || nombre.trim()) return
    try {
      const n = await nombreDgii(limpio)
      if (n) setNombre(n.nombre)
    } catch {
      // sin el nombre de la DGII se escribe a mano
    }
  }

  function todo(forma: number) {
    setPagos([{ forma, monto: tot.total.toFixed(2), banco: forma === TARJETA ? bancos[0]?.id ?? null : null }])
  }

  function cambiarPago(i: number, c: Partial<Pago>) {
    setPagos(pagos.map((p, k) => (k === i ? { ...p, ...c, banco: (c.forma ?? p.forma) === TARJETA ? c.banco ?? p.banco ?? bancos[0]?.id ?? null : null } : p)))
  }

  const motivo =
    !sinId(rnc) && !nombre.trim() ? 'Falta el nombre o razón social del cliente.'
    : pagos.length === 0 ? 'Agregue el pago.'
    : pagos.some((p) => p.forma === TARJETA && !p.banco) ? 'Elija el banco de la tarjeta.'
    : pagos.some((p) => p.forma === CREDITO) && !admiteCredito ? 'El crédito es solo para un cliente de CxC.'
    : otros > tot.total ? 'La tarjeta y el crédito no pueden pasar del total: la devuelta solo sale del efectivo.'
    : falta > 0 ? `Faltan ${dinero(falta)}.`
    : ''

  async function cobrar(ev: FormEvent) {
    ev.preventDefault()
    if (motivo || enviando) return
    if (!window.confirm(`¿Cobrar la mesa ${cuenta.mesa} por ${dinero(tot.total)} con ${tipoActual.codigo}?${devuelta > 0 ? ` Devuelta: ${dinero(devuelta)}.` : ''} Se envía a la DGII.`)) return
    setEnviando(true)
    setError('')
    try {
      setHecho(await cobrarMesa(cuenta.mesa, {
        tipo: tipoActual.id, cliente: cliente?.numero ?? null, rnc: rnc.trim(), nombre: nombre.trim(),
        pagos: pagos.map((p) => ({ formaPago: p.forma, monto: num(p.monto), tipoTarjeta: p.forma === TARJETA ? p.banco : null })),
      }))
      alCobrar()
    } catch (e) {
      manejar(e)
    } finally {
      setEnviando(false)
    }
  }

  if (hecho) return <Cobrada r={hecho} alCerrar={alCerrar} alError={manejar} />
  if (cargado) return <Cargada r={cargado} alCerrar={alCerrar} />
  if (modo === 'habitacion')
    return <CargoHabitacion cuenta={cuenta} alVolver={() => setModo('cobrar')} alCerrar={alCerrar} alError={manejar}
                            alCargado={(r) => { setCargado(r); alCobrar() }} />

  return (
    <Fondo centro>
      {enviando && <Espera texto="Conectando con la DGII, espere..." />}
      <form className="dialogo ancho cobro" role="dialog" aria-modal="true" aria-label={`Cobrar la mesa ${cuenta.mesa}`} onSubmit={cobrar}>
        <div className="cobro-cabeza">
          <h2>Cobrar · {cuenta.nombreMesa} · cuenta #{cuenta.factura}</h2>
          <button type="button" className="secundario" onClick={() => setModo('habitacion')}>Cargar a habitación</button>
        </div>

        <fieldset className="grupo-cargo">
          <legend>Cliente</legend>
          {cliente ? (
            <p>Cliente de CxC: <strong>#{cliente.numero} {cliente.nombre}</strong>{' '}
              <button type="button" className="secundario" onClick={quitarCliente}>Quitar (de contado)</button></p>
          ) : (
            <div className="buscador">
              <label>Cliente de CxC (para crédito o su RNC) — opcional
                <input value={buscar} onChange={(e) => setBuscar(e.target.value)} placeholder="Nombre, RNC o número" />
              </label>
              {hallados.length > 0 && (
                <div className="sugerencias">
                  {hallados.map((c) => (
                    <button key={c.numero} type="button" onClick={() => elegir(c)}>#{c.numero} {c.nombre}{c.rnc ? ` · ${c.rnc}` : ''}</button>
                  ))}
                </div>
              )}
            </div>
          )}
          <div className="tres">
            <label>RNC / cédula<input value={rnc} maxLength={13} onChange={(e) => setRnc(e.target.value.trim())} onBlur={alSalirRnc}
                                      placeholder="En blanco: consumidor final" /></label>
            <label>Nombre o razón social<input value={nombre} maxLength={60} onChange={(e) => setNombre(e.target.value)}
                                               placeholder={sinId(rnc) ? 'CLIENTE DE CONTADO' : ''} /></label>
            <label>Comprobante
              <select value={tipoActual.id} onChange={(e) => setTipo(Number(e.target.value))}>
                {tipos.map((t) => <option key={t.id} value={t.id}>{t.codigo} · {t.nombre}</option>)}
              </select>
            </label>
          </div>
          {tipoActual.id === 13 && <p className="tenue">E44 va sin ITBIS.</p>}
          {tipoActual.id === 2 && tot.total >= 250000 && <p className="alerta">Consumo de 250,000 o más: la DGII pide la cédula o el RNC del cliente.</p>}
        </fieldset>

        <dl className="totales">
          <dt>Subtotal</dt><dd>{dinero(tot.sub)}</dd>
          {tot.itbis > 0 && <><dt>ITBIS 18 %</dt><dd>{dinero(tot.itbis)}</dd></>}
          {tot.propina > 0 && <><dt>Propina 10 %</dt><dd>{dinero(tot.propina)}</dd></>}
          <dt className="saldo">Total a cobrar</dt><dd className="saldo">{dinero(tot.total)}</dd>
        </dl>

        <fieldset className="grupo-cargo">
          <legend>Pago</legend>
          <div className="botones">
            <button type="button" className="secundario" onClick={() => todo(EFECTIVO)}>Todo en efectivo</button>
            <button type="button" className="secundario" onClick={() => todo(TARJETA)}>Todo con tarjeta</button>
            {admiteCredito && <button type="button" className="secundario" onClick={() => todo(CREDITO)}>Todo a crédito</button>}
            <button type="button" className="secundario" onClick={() => setPagos([...pagos, { forma: EFECTIVO, monto: falta ? falta.toFixed(2) : '', banco: null }])}>+ Otro pago</button>
          </div>
          {pagos.map((p, i) => (
            <div key={i} className="pago-linea">
              <label>Forma
                <select value={p.forma} onChange={(e) => cambiarPago(i, { forma: Number(e.target.value) })}>
                  {FORMAS.filter((f) => f.id !== CREDITO || admiteCredito || p.forma === CREDITO).map((f) => <option key={f.id} value={f.id}>{f.nombre}</option>)}
                </select>
              </label>
              <label>{p.forma === EFECTIVO ? 'Recibido' : 'Monto'}<input inputMode="decimal" value={p.monto} onChange={(e) => cambiarPago(i, { monto: e.target.value })} /></label>
              {p.forma === TARJETA && (
                <label>Banco
                  <select value={p.banco ?? ''} onChange={(e) => cambiarPago(i, { banco: Number(e.target.value) })}>
                    {bancos.map((b) => <option key={b.id} value={b.id}>{b.nombre}</option>)}
                  </select>
                </label>
              )}
              <button type="button" className="secundario" onClick={() => setPagos(pagos.filter((_, k) => k !== i))} aria-label="Quitar el pago">Quitar</button>
            </div>
          ))}
          {pagos.length > 0 && (
            <p>Recibido {dinero(recibido)}{devuelta > 0 && <> · <strong>Devuelta {dinero(devuelta)}</strong></>}{falta > 0 && <> · <span className="error">Faltan {dinero(falta)}</span></>}</p>
          )}
        </fieldset>

        {error && <p className="error" role="alert">{error}</p>}
        {!error && motivo && <p className="alerta">Falta: {motivo.replace(/\.$/, '')}.</p>}
        <div className="botones">
          <button type="button" className="secundario" onClick={alCerrar} disabled={enviando}>Cancelar</button>
          <button type="submit" disabled={enviando || !!motivo}>Cobrar {dinero(tot.total)}</button>
        </div>
      </form>
    </Fondo>
  )
}

function Cobrada({ r, alCerrar, alError }: { r: RespuestaCobro; alCerrar: () => void; alError: (e: unknown) => void }) {
  const [dgii, setDgii] = useState<EstadoEcf>(r.dgii)
  const [enviando, setEnviando] = useState(false)
  const c = r.cobro

  async function reenviar() {
    setEnviando(true)
    try { setDgii(await reenviarCafeteria(c.factura)) } catch (e) { alError(e) } finally { setEnviando(false) }
  }

  return (
    <Fondo centro>
      {enviando && <Espera texto="Conectando con la DGII, espere..." />}
      <div className="dialogo" role="dialog" aria-modal="true" aria-label="Mesa cobrada">
        <h2>Mesa {c.mesa} cobrada</h2>
        <dl className="datos-lista">
          <dt>Factura</dt><dd>#{c.factura} · <strong>{c.encf}</strong></dd>
          <dt>Total</dt><dd>{dinero(c.total)}{c.credito ? ' (a crédito)' : ''}</dd>
          <dt>DGII</dt><dd className={dgii.aceptado ? 'ok-texto' : 'error'}>{dgii.estado}{dgii.mensaje ? ` · ${dgii.mensaje}` : ''}</dd>
        </dl>
        {c.devuelta > 0 && <p className="devuelta">Devuelta: <strong>{dinero(c.devuelta)}</strong></p>}
        {!dgii.aceptado && <p className="alerta">La mesa ya está cobrada. Si la DGII no la tiene, reenvíela (también desde la lista de facturas).</p>}
        <div className="botones">
          {!dgii.aceptado && <button type="button" className="secundario" onClick={reenviar} disabled={enviando}>Reenviar a la DGII</button>}
          <button type="button" className="secundario" onClick={() => window.open(`/cafeteria/${c.factura}`, '_blank')}>Imprimir ticket</button>
          <button type="button" onClick={alCerrar}>Listo</button>
        </div>
      </div>
    </Fondo>
  )
}

/** Cargar la cuenta a una habitacion ocupada con credito (etapa 8c): sin e-CF; pasa el subtotal a la cuenta del hotel. */
function CargoHabitacion({ cuenta, alVolver, alCerrar, alCargado, alError }: {
  cuenta: CuentaMesa
  alVolver: () => void
  alCerrar: () => void
  alCargado: (r: ResultadoCargo) => void
  alError: (e: unknown) => void
}) {
  const [habs, setHabs] = useState<HabitacionCafeteria[] | null>(null)
  const [elegida, setElegida] = useState<HabitacionCafeteria | null>(null)
  const [enviando, setEnviando] = useState(false)
  const [aviso, setAviso] = useState('')

  useEffect(() => {
    habitacionesCafeteria().then(setHabs).catch(alError)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  useEffect(() => {
    const tecla = (k: KeyboardEvent) => k.key === 'Escape' && !enviando && alCerrar()
    window.addEventListener('keydown', tecla)
    return () => window.removeEventListener('keydown', tecla)
  }, [alCerrar, enviando])

  async function cargar() {
    if (!elegida || enviando) return
    if (!window.confirm(`¿Cargar la mesa ${cuenta.mesa} (${dinero(cuenta.totales.subTotal)} sin impuestos) a la habitación ${elegida.habitacion}${elegida.huesped ? `, ${elegida.huesped}` : ''}?`)) return
    setEnviando(true)
    setAviso('')
    try {
      alCargado(await cargarAHabitacion(cuenta.mesa, elegida.habitacion))
    } catch (e) {
      if (e instanceof SesionVencida) alError(e)
      else setAviso(e instanceof Error ? e.message : String(e))
    } finally {
      setEnviando(false)
    }
  }

  return (
    <Fondo centro>
      <div className="dialogo ancho cobro" role="dialog" aria-modal="true" aria-label={`Cargar la mesa ${cuenta.mesa} a una habitación`}>
        <div className="cobro-cabeza">
          <h2>Cargar a habitación · {cuenta.nombreMesa}</h2>
          <button type="button" className="secundario" onClick={alVolver} disabled={enviando}>Volver al cobro</button>
        </div>
        <p className="tenue">Sin comprobante: a la cuenta de la habitación pasa el subtotal <strong>{dinero(cuenta.totales.subTotal)}</strong>.
          El ITBIS y la propina van en la factura del hotel al check-out. Solo habitaciones con crédito en la cafetería.</p>
        {!habs ? <p className="tenue">Cargando las habitaciones...</p>
          : habs.length === 0 ? <p className="alerta">No hay habitaciones ocupadas.</p>
          : (
            <div className="cafe-mesas habitaciones-cargo" role="list" aria-label="Habitaciones ocupadas">
              {habs.map((h) => (
                <button key={h.habitacion} type="button" role="listitem" disabled={!h.credito || enviando}
                        className={`mesa ${h.credito ? 'm-Abierta' : 'm-Libre'}${elegida?.habitacion === h.habitacion ? ' elegida' : ''}`}
                        onClick={() => setElegida(h)} title={h.credito ? '' : 'Sin crédito en la cafetería'}>
                  <span className="num">{h.habitacion}</span>
                  <span>{h.huesped ?? ''}</span>
                  <span className="tenue">{h.credito ? h.salida ? `sale ${fecha(h.salida)}` : '' : 'sin crédito'}</span>
                </button>
              ))}
            </div>
          )}
        {aviso && <p className="error" role="alert">{aviso}</p>}
        <div className="botones">
          <button type="button" className="secundario" onClick={alCerrar} disabled={enviando}>Cancelar</button>
          <button type="button" onClick={cargar} disabled={!elegida || enviando}>
            {elegida ? `Cargar ${dinero(cuenta.totales.subTotal)} a la ${elegida.habitacion}` : 'Elija la habitación'}
          </button>
        </div>
      </div>
    </Fondo>
  )
}

function Cargada({ r, alCerrar }: { r: ResultadoCargo; alCerrar: () => void }) {
  return (
    <Fondo centro>
      <div className="dialogo" role="dialog" aria-modal="true" aria-label="Mesa cargada a la habitación">
        <h2>Mesa {r.mesa} cargada a la habitación {r.habitacion}</h2>
        <dl className="datos-lista">
          <dt>Huésped</dt><dd>{r.huesped ?? ''}</dd>
          <dt>Pasó a la cuenta</dt><dd><strong>{dinero(r.subTotal)}</strong> (línea "CAFETERIA - FACTURA # {r.factura}")</dd>
        </dl>
        <div className="botones"><button type="button" onClick={alCerrar} autoFocus>Listo</button></div>
      </div>
    </Fondo>
  )
}
