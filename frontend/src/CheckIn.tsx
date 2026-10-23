import { useCallback, useEffect, useMemo, useState } from 'react'
import Fondo from './Fondo'
import {
  buscarClientes, dinero, fecha, hacerCheckIn, llegadasHoy, prepararCheckIn, SesionVencida,
  type Asignacion, type ClienteResumen, type LlegadaHoy, type PreparacionCheckIn,
} from './api'

/** Llegadas de hoy (y atrasadas) y el check-in de cada una. */
export default function CheckIn({ alVencer }: { alVencer: () => void }) {
  const [lista, setLista] = useState<LlegadaHoy[] | null>(null)
  const [error, setError] = useState('')
  const [abierta, setAbierta] = useState<number | null>(null)
  const [aviso, setAviso] = useState<{ texto: string; avisos: string[] } | null>(null)

  const manejar = useCallback(
    (e: unknown) => (e instanceof SesionVencida ? alVencer() : setError(e instanceof Error ? e.message : String(e))),
    [alVencer],
  )
  const cargar = useCallback(() => {
    llegadasHoy().then((r) => { setLista(r); setError('') }).catch(manejar)
  }, [manejar])
  useEffect(cargar, [cargar])

  return (
    <section>
      <p className="tenue">Reservas activas que entran hoy (y las atrasadas que todavía no salen). El número de habitación se escoge aquí.</p>
      {aviso && <div role="status"><p className="ok-texto">{aviso.texto}</p>{aviso.avisos.map((a) => <p key={a} className="alerta">{a}</p>)}</div>}
      {error && <p className="error">{error}</p>}
      <div className="tabla-marco">
        <table className="tabla catalogo">
          <thead>
            <tr><th className="num">Reserva</th><th>Entrada</th><th>Salida</th><th>Cliente</th><th>Huésped</th><th>Habitaciones</th><th className="num">Adelantos</th><th></th></tr>
          </thead>
          <tbody>
            {lista?.map((l) => (
              <tr key={l.reserva} className="fila-editable" tabIndex={0} onClick={() => setAbierta(l.reserva)}
                  onKeyDown={(e) => e.key === 'Enter' && setAbierta(l.reserva)}>
                <td className="num">{l.reserva}</td>
                <td className={l.atrasada ? 'mal' : ''}>{fecha(l.entrada, true)}{l.atrasada ? ' (atrasada)' : ''}</td>
                <td>{fecha(l.salida, true)}</td>
                <td><strong>{l.cliente}</strong>{!l.clienteId && <span className="alerta fila-sub">sin cliente de la lista</span>}</td>
                <td>{l.huesped}</td>
                <td>{l.detalle}</td>
                <td className="num">{l.adelantos ? dinero(l.adelantos) : ''}</td>
                <td><button type="button" onClick={(e) => { e.stopPropagation(); setAbierta(l.reserva) }}>Check-in</button></td>
              </tr>
            ))}
            {lista?.length === 0 && <tr><td colSpan={8} className="tenue">No hay llegadas pendientes para hoy.</td></tr>}
          </tbody>
        </table>
      </div>

      {abierta && (
        <Asignar reserva={abierta} alVencer={alVencer}
                 alCerrar={(r) => {
                   setAbierta(null)
                   if (r) setAviso(r)
                   cargar()
                 }} />
      )}
    </section>
  )
}

function Asignar({ reserva, alCerrar, alVencer }: {
  reserva: number
  alCerrar: (r?: { texto: string; avisos: string[] }) => void
  alVencer: () => void
}) {
  const [p, setP] = useState<PreparacionCheckIn | null>(null)
  const [elegidas, setElegidas] = useState<Record<string, number>>({})
  const [cliente, setCliente] = useState<ClienteResumen | null>(null)
  const [error, setError] = useState('')
  const [enviando, setEnviando] = useState(false)

  const manejar = useCallback((e: unknown) => {
    if (e instanceof SesionVencida) alVencer()
    else setError(e instanceof Error ? e.message : String(e))
  }, [alVencer])

  useEffect(() => {
    prepararCheckIn(reserva).then((r) => {
      setP(r)
      const inicial: Record<string, number> = {}
      r.lugares.forEach((l) => { if (l.sugerida) inicial[`${l.secuencia}-${l.indice}`] = l.sugerida })
      setElegidas(inicial)
    }).catch(manejar)
  }, [reserva, manejar])

  useEffect(() => {
    const tecla = (k: KeyboardEvent) => k.key === 'Escape' && alCerrar()
    window.addEventListener('keydown', tecla)
    return () => window.removeEventListener('keydown', tecla)
  }, [alCerrar])

  const usadas = useMemo(() => new Set(Object.values(elegidas)), [elegidas])
  const completas = p ? p.lugares.every((l) => elegidas[`${l.secuencia}-${l.indice}`]) : false
  const faltaCliente = p ? !p.clienteId && !cliente : false

  async function confirmar() {
    if (!p) return
    const asignaciones: Asignacion[] = p.lugares.map((l) => ({ secuencia: l.secuencia, indice: l.indice, habitacion: elegidas[`${l.secuencia}-${l.indice}`] }))
    const nums = asignaciones.map((a) => a.habitacion).join(', ')
    if (!window.confirm(`¿Hacer el check-in de la reserva ${p.reserva} en ${asignaciones.length === 1 ? 'la habitación' : 'las habitaciones'} ${nums}?`)) return
    setEnviando(true)
    setError('')
    try {
      const r = await hacerCheckIn(p.reserva, asignaciones, cliente?.numero ?? null)
      alCerrar({ texto: `Check-in hecho: reserva ${p.reserva} en ${r.cuentas.map((c) => c.habitacion).join(', ')}.`, avisos: r.avisos })
    } catch (e) {
      manejar(e)
    } finally {
      setEnviando(false)
    }
  }

  return (
    <Fondo centro>
      <div className="dialogo reserva" role="dialog" aria-modal="true" aria-label="Check-in">
        <h2>Check-in · reserva {reserva}</h2>
        {!p && !error && <p className="tenue">Cargando...</p>}
        {p && (
          <>
            <dl className="datos-lista">
              <dt>Cliente</dt><dd>{p.cliente}</dd>
              <dt>Huésped</dt><dd>{p.huesped}</dd>
              <dt>Estadía</dt><dd>{fecha(p.entrada, true)} al {fecha(p.salida, true)} · {p.noches} {p.noches === 1 ? 'noche' : 'noches'}</dd>
              {p.adelantos > 0 && <><dt>Adelantos</dt><dd>{dinero(p.adelantos)} (se aplican a la cuenta)</dd></>}
            </dl>
            {!p.clienteId && (
              <ElegirCliente cliente={cliente} alElegir={setCliente} alError={manejar} />
            )}
            <table className="tabla">
              <thead><tr><th>Reservado</th><th className="num">Tarifa</th><th>Personas</th><th>Habitación</th></tr></thead>
              <tbody>
                {p.lugares.map((l) => {
                  const clave = `${l.secuencia}-${l.indice}`
                  const elegida = elegidas[clave]
                  const libre = p.libres.find((x) => x.numero === elegida)
                  return (
                    <tr key={clave}>
                      <td>{l.tipoNombre}{l.categoria ? ` (${l.categoria})` : ''}</td>
                      <td className="num">{dinero(l.tarifa)}</td>
                      <td>{l.adultos} adultos{l.ninos ? `, ${l.ninos} niños` : ''}{l.bebes ? `, ${l.bebes} bebés` : ''}{l.credito ? ' · crédito' : ''}</td>
                      <td>
                        <select value={elegida ?? ''} onChange={(e) => setElegidas({ ...elegidas, [clave]: Number(e.target.value) })}>
                          <option value="">Escoja...</option>
                          <optgroup label={`Libres ${l.tipoNombre}`}>
                            {p.libres.filter((x) => x.tipo === l.tipo && x.disponible && (x.numero === elegida || !usadas.has(x.numero)))
                              .map((x) => <option key={x.numero} value={x.numero}>{x.numero}</option>)}
                          </optgroup>
                          <optgroup label="Otras libres">
                            {p.libres.filter((x) => x.tipo !== l.tipo && x.disponible && (x.numero === elegida || !usadas.has(x.numero)))
                              .map((x) => <option key={x.numero} value={x.numero}>{x.numero} · {x.tipoNombre}</option>)}
                          </optgroup>
                        </select>
                        {libre && libre.tipo !== l.tipo && <span className="alerta fila-sub">Es {libre.tipoNombre}: se cobra la tarifa reservada.</span>}
                        {!elegida && !l.sugerida && <span className="alerta fila-sub">No hay {l.tipoNombre.toLowerCase()} libre: escoja otra.</span>}
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </>
        )}
        {error && <p className="error" role="alert">{error}</p>}
        <div className="botones">
          <button type="button" className="secundario" onClick={() => alCerrar()}>Cerrar</button>
          <button type="button" onClick={confirmar} disabled={!p || !completas || faltaCliente || enviando}>
            {enviando ? 'Haciendo el check-in...' : 'Hacer el check-in'}
          </button>
        </div>
      </div>
    </Fondo>
  )
}

/** Delphi no deja hacer el check-in sin un cliente de la lista (la factura lo necesita). */
function ElegirCliente({ cliente, alElegir, alError }: {
  cliente: ClienteResumen | null
  alElegir: (c: ClienteResumen | null) => void
  alError: (e: unknown) => void
}) {
  const [texto, setTexto] = useState('')
  const [opciones, setOpciones] = useState<ClienteResumen[]>([])

  useEffect(() => {
    if (texto.trim().length < 3) {
      setOpciones([])
      return
    }
    const t = setTimeout(() => buscarClientes(texto).then((r) => setOpciones(r.pagina.clientes.slice(0, 8))).catch(alError), 350)
    return () => clearTimeout(t)
  }, [texto, alError])

  if (cliente)
    return <p className="ok-texto">Cliente: {cliente.nombre} (#{cliente.numero}) <button type="button" className="secundario" onClick={() => alElegir(null)}>Cambiar</button></p>
  return (
    <label className="buscador">
      <span className="alerta">La reserva no tiene cliente de la lista: escójalo para poder hacer el check-in.</span>
      <input placeholder="Buscar cliente por nombre o RNC" value={texto} onChange={(e) => setTexto(e.target.value)} />
      {opciones.length > 0 && (
        <ul className="sugerencias">
          {opciones.map((c) => (
            <li key={c.numero}><button type="button" onClick={() => alElegir(c)}>
              <strong>{c.nombre}</strong> <span className="tenue">#{c.numero}{c.rnc ? ` · RNC ${c.rnc}` : ''}</span>
            </button></li>
          ))}
        </ul>
      )}
    </label>
  )
}
