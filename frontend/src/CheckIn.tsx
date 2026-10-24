import { useCallback, useEffect, useMemo, useState } from 'react'
import Fondo from './Fondo'
import FotoTipo from './FotoTipo'
import { useFotosTipos } from './useFotosTipos'
import {
  buscarClientes, dinero, FACTOR_IMPUESTOS, fecha, hacerCheckIn, llegadasHoy, obtenerPrecios, prepararCheckIn, SesionVencida,
  type Asignacion, type ClienteResumen, type LlegadaHoy, type LugarCheckIn, type PantallaPrecios, type PreparacionCheckIn,
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

export function Asignar({ reserva, alCerrar, alVencer }: {
  reserva: number
  alCerrar: (r?: { texto: string; avisos: string[] }) => void
  alVencer: () => void
}) {
  const [p, setP] = useState<PreparacionCheckIn | null>(null)
  const fotos = useFotosTipos()
  const [elegidas, setElegidas] = useState<Record<string, number>>({})
  // categoria y tarifa (sin impuestos) de cada lugar; la tarifa null = ese tipo no tiene precio en esa categoria
  // respetar: el hotel no cumplio con el tipo reservado y le mantiene al cliente el precio acordado
  const [precio, setPrecio] = useState<Record<string, { categoria: string; tarifa: number | null; respetar?: boolean }>>({})
  const [tabla, setTabla] = useState<PantallaPrecios['tabla'] | null>(null)
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
      const pr: Record<string, { categoria: string; tarifa: number | null }> = {}
      r.lugares.forEach((l) => { pr[`${l.secuencia}-${l.indice}`] = { categoria: l.categoria || 'A', tarifa: l.tarifa } })
      setPrecio(pr)
    }).catch(manejar)
    obtenerPrecios().then((x) => setTabla(x.tabla)).catch(manejar)
  }, [reserva, manejar])

  /** La tarifa de un lugar: la reservada si no cambia el tipo ni la categoria; si cambia, la de la tabla de precios. */
  function tarifaPara(l: LugarCheckIn, numero: number | undefined, categoria: string): number | null {
    const tipo = p?.libres.find((x) => x.numero === numero)?.tipo ?? l.tipo
    if (tipo === l.tipo && categoria === (l.categoria || 'A')) return l.tarifa
    return tabla?.precios.find((x) => x.tipo === tipo && x.categoria === categoria)?.sinImpuestos ?? null
  }

  /**
   * Respetar el precio reservado protege al cliente: solo se ofrece si la habitacion que se da cuesta MAS que lo
   * reservado (su precio en la categoria reservada). Si cuesta menos, se cobra la mas barata. Sin precio en la tabla,
   * se ofrece (no se sabe si es mas cara).
   */
  function puedeRespetar(l: LugarCheckIn, numero: number | undefined): boolean {
    const tipo = p?.libres.find((x) => x.numero === numero)?.tipo
    if (tipo == null || tipo === l.tipo) return false
    const nuevo = tarifaPara(l, numero, l.categoria || 'A')
    return nuevo == null || nuevo > l.tarifa
  }

  function elegir(l: LugarCheckIn, numero: number) {
    const clave = `${l.secuencia}-${l.indice}`
    setElegidas({ ...elegidas, [clave]: numero })
    const actual = precio[clave]
    if (actual?.respetar && puedeRespetar(l, numero)) return   // sigue con el precio reservado
    const cat = actual?.categoria ?? (l.categoria || 'A')
    setPrecio({ ...precio, [clave]: { categoria: cat, tarifa: tarifaPara(l, numero, cat) } })
  }

  function cambiarCategoria(l: LugarCheckIn, categoria: string) {
    const clave = `${l.secuencia}-${l.indice}`
    setPrecio({ ...precio, [clave]: { categoria, tarifa: tarifaPara(l, elegidas[clave], categoria) } })
  }

  /** Mantener (o dejar de mantener) la categoria y la tarifa reservadas aunque la habitacion sea de otro tipo. */
  function respetarReservado(l: LugarCheckIn, si: boolean) {
    const clave = `${l.secuencia}-${l.indice}`
    const cat = l.categoria || 'A'
    setPrecio({ ...precio, [clave]: si ? { categoria: cat, tarifa: l.tarifa, respetar: true } : { categoria: cat, tarifa: tarifaPara(l, elegidas[clave], cat) } })
  }

  useEffect(() => {
    const tecla = (k: KeyboardEvent) => k.key === 'Escape' && alCerrar()
    window.addEventListener('keydown', tecla)
    return () => window.removeEventListener('keydown', tecla)
  }, [alCerrar])

  const usadas = useMemo(() => new Set(Object.values(elegidas)), [elegidas])
  const completas = p ? p.lugares.every((l) => elegidas[`${l.secuencia}-${l.indice}`]) : false
  const sinTarifa = p ? p.lugares.some((l) => precio[`${l.secuencia}-${l.indice}`]?.tarifa == null) : false
  const faltaCliente = p ? !p.clienteId && !cliente : false

  async function confirmar() {
    if (!p) return
    const asignaciones: Asignacion[] = p.lugares.map((l) => {
      const clave = `${l.secuencia}-${l.indice}`
      return { secuencia: l.secuencia, indice: l.indice, habitacion: elegidas[clave], categoria: precio[clave]?.categoria, tarifa: precio[clave]?.tarifa ?? undefined }
    })
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
              <thead><tr><th>Reservado</th><th>Personas</th><th>Habitación</th><th>Categoría</th><th className="num">Tarifa</th></tr></thead>
              <tbody>
                {p.lugares.map((l) => {
                  const clave = `${l.secuencia}-${l.indice}`
                  const elegida = elegidas[clave]
                  const libre = p.libres.find((x) => x.numero === elegida)
                  const pr = precio[clave] ?? { categoria: l.categoria || 'A', tarifa: l.tarifa }
                  return (
                    <tr key={clave}>
                      <td className="con-foto">
                        <FotoTipo tipo={libre?.tipo ?? l.tipo} fotos={fotos} className="foto-tipo mini" />
                        <span>{l.tipoNombre}{l.categoria ? ` (${l.categoria})` : ''}<span className="tenue fila-sub">{dinero(l.tarifa)} reservada</span></span>
                      </td>
                      <td>{l.adultos} adultos{l.ninos ? `, ${l.ninos} niños` : ''}{l.bebes ? `, ${l.bebes} bebés` : ''}{l.credito ? ' · crédito' : ''}</td>
                      <td>
                        <select value={elegida ?? ''} onChange={(e) => elegir(l, Number(e.target.value))}>
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
                        {libre && libre.tipo !== l.tipo && (
                          <div className="aviso-tipo">
                            <span className="alerta">
                              Es {libre.tipoNombre}: {pr.respetar ? `se le respeta el precio de ${l.tipoNombre.toLowerCase()}.` : 'se cobra su precio.'}
                            </span>
                            {puedeRespetar(l, elegida) && <label className="check" title="La habitacion cuesta mas que lo reservado: el hotel le mantiene al cliente el precio acordado">
                              <input type="checkbox" checked={!!pr.respetar} onChange={(e) => respetarReservado(l, e.target.checked)} />
                              Respetar precio reservado
                            </label>}
                          </div>
                        )}
                        {!elegida && !l.sugerida && <span className="alerta fila-sub">No hay {l.tipoNombre.toLowerCase()} libre: escoja otra.</span>}
                      </td>
                      <td>
                        <select value={pr.categoria} disabled={pr.respetar} onChange={(e) => cambiarCategoria(l, e.target.value)}>
                          {(tabla?.categorias ?? [{ id: pr.categoria, descripcion: pr.categoria }]).map((c) =>
                            <option key={c.id} value={c.id}>{c.id}{c.descripcion && c.descripcion !== c.id ? ` · ${c.descripcion}` : ''}</option>)}
                        </select>
                      </td>
                      <td className="num">
                        <input className="numero ancho" inputMode="decimal" value={pr.tarifa ?? ''} disabled={pr.respetar}
                               title={pr.tarifa != null ? `Sin impuestos. Con ITBIS y propina: ${dinero(Math.round(pr.tarifa * FACTOR_IMPUESTOS * 100) / 100)}` : ''}
                               onChange={(e) => {
                                 const v = e.target.value.replace(/,/g, '').trim()
                                 setPrecio({ ...precio, [clave]: { ...pr, tarifa: v === '' || isNaN(Number(v)) ? null : Number(v) } })
                               }} />
                        {pr.tarifa == null && <span className="alerta fila-sub">Sin precio en esta categoría: escriba la tarifa.</span>}
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
          <button type="button" onClick={confirmar} disabled={!p || !completas || sinTarifa || faltaCliente || enviando}>
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
