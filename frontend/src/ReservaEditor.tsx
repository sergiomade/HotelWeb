import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import Fondo from './Fondo'
import {
  anularAdelanto, buscarClientes, cancelarReserva, crearReserva, dinero, disponibilidad, fecha, FACTOR_IMPUESTOS, hoyIso,
  modificarReserva, obtenerPrecios, obtenerReserva, opcionesReserva, PideConfirmar, registrarAdelanto, SesionVencida,
  type ClienteResumen, type DatosReserva, type Disponibilidad, type LineaReserva, type OpcionesReserva, type PantallaPrecios,
  type Reserva,
} from './api'

const NUEVA: DatosReserva = {
  nombreCliente: '', clienteId: null, contacto: '', telefono: '', huesped: '', cedula: '', pasaporte: '', rnc: '',
  entrada: hoyIso(), salida: hoyIso(1), comentario: '', formaPago: 1, categoria: 'A', lineas: [],
}

/** Una reserva: datos, tipos de habitacion y, si ya existe, sus adelantos. Solo se modifica si esta activa. */
export default function ReservaEditor({ id, alCerrar, alVencer }: {
  id: number | null
  alCerrar: (mensaje?: string) => void
  alVencer: () => void
}) {
  const [reserva, setReserva] = useState<Reserva | null>(null)
  const [d, setD] = useState<DatosReserva>(NUEVA)
  const [precios, setPrecios] = useState<PantallaPrecios | null>(null)
  const [opciones, setOpciones] = useState<OpcionesReserva | null>(null)
  const [libres, setLibres] = useState<Disponibilidad[]>([])
  const [error, setError] = useState('')
  const [avisos, setAvisos] = useState<string[]>([])
  const [guardando, setGuardando] = useState(false)

  const manejar = useCallback((e: unknown) => {
    if (e instanceof SesionVencida) alVencer()
    else setError(e instanceof Error ? e.message : String(e))
  }, [alVencer])

  useEffect(() => {
    obtenerPrecios().then(setPrecios).catch(manejar)
    opcionesReserva().then(setOpciones).catch(manejar)
    if (id) {
      obtenerReserva(id).then((r) => {
        setReserva(r)
        setD(datosDe(r))
      }).catch(manejar)
    }
  }, [id, manejar])

  useEffect(() => {
    const tecla = (k: KeyboardEvent) => k.key === 'Escape' && alCerrar()
    window.addEventListener('keydown', tecla)
    return () => window.removeEventListener('keydown', tecla)
  }, [alCerrar])

  // libres por tipo en esas fechas (sin contar esta misma reserva)
  useEffect(() => {
    if (!d.entrada || !d.salida || d.salida <= d.entrada) return
    const t = setTimeout(() => disponibilidad(d.entrada, d.salida, id ?? undefined).then(setLibres).catch(() => setLibres([])), 300)
    return () => clearTimeout(t)
  }, [d.entrada, d.salida, id])

  const editable = !reserva || reserva.estado === 1
  const noches = d.entrada && d.salida ? Math.round((Date.parse(d.salida) - Date.parse(d.entrada)) / 86_400_000) : 0
  const tipos = useMemo(() => (precios?.tabla.tipos ?? []).filter((t) => t.habitaciones > 0), [precios])
  const categorias = precios?.tabla.categorias ?? []

  function tarifaDe(tipo: number, categoria: string): number {
    return precios?.tabla.precios.find((p) => p.tipo === tipo && p.categoria === categoria)?.sinImpuestos ?? 0
  }

  function agregarLinea() {
    const tipo = tipos[0]?.id ?? 0
    const categoria = d.categoria || 'A'
    const linea: LineaReserva = { tipo, cantidad: 1, adultos: tipos[0]?.maximo || 1, ninos: 0, bebes: 0, credito: true, categoria, tarifa: tarifaDe(tipo, categoria) }
    setD({ ...d, lineas: [...d.lineas, linea] })
  }

  function cambiarLinea(i: number, cambio: Partial<LineaReserva>) {
    setD((x) => {
      const lineas = x.lineas.map((l, j) => {
        if (j !== i) return l
        const n = { ...l, ...cambio }
        // al cambiar el tipo o la categoria, la tarifa vuelve a la de la tabla de precios
        if (cambio.tipo !== undefined || cambio.categoria !== undefined) n.tarifa = tarifaDe(n.tipo, n.categoria)
        if (cambio.tipo !== undefined) n.adultos = tipos.find((t) => t.id === n.tipo)?.maximo || 1
        return n
      })
      return { ...x, lineas }
    })
  }

  const total = d.lineas.reduce((s, l) => s + l.cantidad * l.tarifa * Math.max(0, noches), 0)

  async function enviar(ev: FormEvent) {
    ev.preventDefault()
    setGuardando(true)
    setError('')
    try {
      const r = id ? await modificarReserva(id, d) : await crearReserva(d)
      if (r.avisos.length > 0 && !id) {
        // nueva con sobreventa: se queda abierta para que vean el aviso y los adelantos
        setReserva(r.reserva)
        setD(datosDe(r.reserva))
        setAvisos(r.avisos)
      } else if (r.avisos.length > 0) {
        setReserva(r.reserva)
        setAvisos(r.avisos)
      } else alCerrar(`${id ? 'Guardada' : 'Creada'} la reserva ${r.reserva.id} de ${r.reserva.cliente}.`)
    } catch (x) {
      manejar(x)
    } finally {
      setGuardando(false)
    }
  }

  async function cancelar() {
    if (!reserva) return
    if (!window.confirm(`¿Cancelar la reserva ${reserva.id} de ${reserva.cliente}?`)) return
    try {
      await cancelarReserva(reserva.id, false)
      alCerrar(`Cancelada la reserva ${reserva.id}.`)
    } catch (x) {
      if (x instanceof PideConfirmar && window.confirm(`${x.message}\n\n¿La cancela de todos modos?`)) {
        try {
          await cancelarReserva(reserva.id, true)
          alCerrar(`Cancelada la reserva ${reserva.id} (tenía adelantos activos).`)
        } catch (y) {
          manejar(y)
        }
      } else if (!(x instanceof PideConfirmar)) manejar(x)
    }
  }

  const cambiar = <K extends keyof DatosReserva>(k: K, v: DatosReserva[K]) => setD((x) => ({ ...x, [k]: v }))
  const cargando = (id && !reserva) || !precios || !opciones

  return (
    <Fondo centro>
      <form className="dialogo reserva" role="dialog" aria-modal="true" aria-label="Reserva" onSubmit={enviar}>
        <div className="rack-cabecera">
          <h2>{id ? `Reserva ${id}` : 'Nueva reserva'}</h2>
          {reserva && <span className={`estado e${reserva.estado}`}>{reserva.estadoNombre}</span>}
        </div>
        {cargando && <p className="tenue">Cargando...</p>}
        {reserva && !editable && <p className="tenue">La reserva está {reserva.estadoNombre.toLowerCase()}: solo se puede consultar.</p>}

        {!cargando && (
          <>
            <fieldset disabled={!editable} className="grupo">
              <legend>Cliente y huésped</legend>
              <BuscadorCliente nombre={d.nombreCliente} alElegir={(c) => setD((x) => ({ ...x, nombreCliente: c.nombre, clienteId: c.numero, rnc: c.rnc ?? x.rnc, telefono: x.telefono || c.telefono }))}
                               alEscribir={(n) => setD((x) => ({ ...x, nombreCliente: n, clienteId: null }))} alError={manejar} />
              <div className="tres">
                <label>Huésped<input maxLength={60} value={d.huesped ?? ''} onChange={(x) => cambiar('huesped', x.target.value)} /></label>
                <label>Contacto<input maxLength={60} value={d.contacto ?? ''} onChange={(x) => cambiar('contacto', x.target.value)} /></label>
                <label>Teléfono<input maxLength={13} value={d.telefono ?? ''} onChange={(x) => cambiar('telefono', x.target.value)} /></label>
              </div>
              <div className="tres">
                <label>Cédula<input maxLength={13} value={d.cedula ?? ''} onChange={(x) => cambiar('cedula', x.target.value)} /></label>
                <label>Pasaporte<input maxLength={15} value={d.pasaporte ?? ''} onChange={(x) => cambiar('pasaporte', x.target.value)} /></label>
                <label>RNC<input maxLength={13} value={d.rnc ?? ''} onChange={(x) => cambiar('rnc', x.target.value)} /></label>
              </div>
            </fieldset>

            <fieldset disabled={!editable} className="grupo">
              <legend>Estadía</legend>
              <div className="cuatro">
                <label>Entrada<input type="date" value={d.entrada} onChange={(x) => cambiar('entrada', x.target.value)} /></label>
                <label>Salida<input type="date" value={d.salida} onChange={(x) => cambiar('salida', x.target.value)} /></label>
                <label>Forma de pago
                  <select value={d.formaPago} onChange={(x) => cambiar('formaPago', Number(x.target.value))}>
                    {opciones!.formasPago.map((f) => <option key={f.id} value={f.id}>{f.nombre}</option>)}
                  </select>
                </label>
                <label>Categoría de precio
                  <select value={d.categoria ?? 'A'} onChange={(x) => cambiar('categoria', x.target.value)}>
                    {categorias.map((c) => <option key={c.id} value={c.id}>{c.id}{c.descripcion !== c.id ? ` ${c.descripcion}` : ''}</option>)}
                  </select>
                </label>
              </div>
              <p className="tenue">{noches > 0 ? `${noches} ${noches === 1 ? 'noche' : 'noches'}` : 'La salida tiene que ser después de la entrada.'}</p>
            </fieldset>

            <fieldset disabled={!editable} className="grupo">
              <legend>Habitaciones (por tipo: el número se asigna en el check-in)</legend>
              <div className="tabla-marco">
                <table className="tabla lineas">
                  <thead>
                    <tr><th>Tipo</th><th>Cat.</th><th className="num">Cant.</th><th className="num">Adultos</th><th className="num">Niños</th>
                      <th className="num">Bebés</th><th>Crédito</th><th className="num">Tarifa</th><th className="num">Importe</th><th className="num">Libres</th><th></th></tr>
                  </thead>
                  <tbody>
                    {d.lineas.map((l, i) => {
                      const lib = libres.find((x) => x.tipo === l.tipo)
                      const pedidas = d.lineas.filter((x) => x.tipo === l.tipo).reduce((s, x) => s + x.cantidad, 0)
                      return (
                        <tr key={i}>
                          <td><select value={l.tipo} onChange={(x) => cambiarLinea(i, { tipo: Number(x.target.value) })}>
                            {tipos.map((t) => <option key={t.id} value={t.id}>{t.descripcion}</option>)}
                          </select></td>
                          <td><select value={l.categoria} onChange={(x) => cambiarLinea(i, { categoria: x.target.value })}>
                            {categorias.map((c) => <option key={c.id} value={c.id}>{c.id}</option>)}
                          </select></td>
                          <td><input className="numero" inputMode="numeric" value={l.cantidad} onChange={(x) => cambiarLinea(i, { cantidad: entero(x.target.value) })} /></td>
                          <td><input className="numero" inputMode="numeric" value={l.adultos} onChange={(x) => cambiarLinea(i, { adultos: entero(x.target.value) })} /></td>
                          <td><input className="numero" inputMode="numeric" value={l.ninos} onChange={(x) => cambiarLinea(i, { ninos: entero(x.target.value) })} /></td>
                          <td><input className="numero" inputMode="numeric" value={l.bebes} onChange={(x) => cambiarLinea(i, { bebes: entero(x.target.value) })} /></td>
                          <td><input type="checkbox" checked={l.credito} onChange={(x) => cambiarLinea(i, { credito: x.target.checked })} title="Crédito para la cafetería" /></td>
                          <td><input className="numero ancho" inputMode="decimal" value={l.tarifa}
                                     title={`Sin impuestos. Con ITBIS y propina: ${dinero(Math.round(l.tarifa * FACTOR_IMPUESTOS * 100) / 100)}`}
                                     onChange={(x) => cambiarLinea(i, { tarifa: Number(x.target.value.replace(/,/g, '')) || 0 })} /></td>
                          <td className="num">{dinero(l.cantidad * l.tarifa * Math.max(0, noches))}</td>
                          <td className={`num${lib && pedidas > lib.libres ? ' mal' : ''}`}>{lib ? Math.max(0, lib.libres) : ''}</td>
                          <td><button type="button" className="secundario" onClick={() => setD({ ...d, lineas: d.lineas.filter((_, j) => j !== i) })} aria-label="Quitar">Quitar</button></td>
                        </tr>
                      )
                    })}
                    {d.lineas.length === 0 && <tr><td colSpan={11} className="tenue">Agregue el tipo de habitación.</td></tr>}
                  </tbody>
                </table>
              </div>
              <div className="rack-cabecera">
                <button type="button" className="secundario" onClick={agregarLinea}>Agregar habitación</button>
                <strong>Total {dinero(total)} <span className="tenue">(sin impuestos)</span></strong>
              </div>
              <label>Comentario<textarea rows={2} maxLength={1000} value={d.comentario ?? ''} onChange={(x) => cambiar('comentario', x.target.value)} /></label>
            </fieldset>

            {avisos.map((a) => <p key={a} className="alerta" role="status">{a}</p>)}
            {error && <p className="error" role="alert">{error}</p>}

            {reserva && opciones && (
              <Adelantos reserva={reserva} opciones={opciones} alCambiar={(r) => { setReserva(r); setError('') }} alError={manejar} />
            )}
          </>
        )}

        <div className="botones">
          {reserva && editable && <button type="button" className="peligro" onClick={cancelar}>Cancelar la reserva</button>}
          <span className="espacio" />
          <button type="button" className="secundario" onClick={() => alCerrar(avisos.length ? `Guardada la reserva ${reserva?.id}.` : undefined)}>Cerrar</button>
          {editable && <button type="submit" disabled={guardando || !!cargando || d.lineas.length === 0 || noches < 1 || !d.nombreCliente.trim()}>
            {guardando ? 'Guardando...' : 'Guardar'}
          </button>}
        </div>
      </form>
    </Fondo>
  )
}

/** Nombre del cliente con busqueda en la lista de clientes (o escrito a mano, como permite Delphi). */
function BuscadorCliente({ nombre, alElegir, alEscribir, alError }: {
  nombre: string
  alElegir: (c: ClienteResumen) => void
  alEscribir: (n: string) => void
  alError: (e: unknown) => void
}) {
  const [opciones, setOpciones] = useState<ClienteResumen[]>([])
  const [abierto, setAbierto] = useState(false)

  useEffect(() => {
    if (!abierto || nombre.trim().length < 3) {
      setOpciones([])
      return
    }
    const t = setTimeout(() => buscarClientes(nombre).then((r) => setOpciones(r.pagina.clientes.slice(0, 8))).catch(alError), 350)
    return () => clearTimeout(t)
  }, [nombre, abierto, alError])

  return (
    <label className="buscador">
      Cliente (busque en la lista o escriba el nombre)
      <input maxLength={60} value={nombre} autoFocus onChange={(x) => { alEscribir(x.target.value); setAbierto(true) }}
             onBlur={() => setTimeout(() => setAbierto(false), 200)} />
      {abierto && opciones.length > 0 && (
        <ul className="sugerencias">
          {opciones.map((c) => (
            <li key={c.numero}><button type="button" onMouseDown={(e) => e.preventDefault()} onClick={() => { alElegir(c); setAbierto(false) }}>
              <strong>{c.nombre}</strong> <span className="tenue">#{c.numero}{c.rnc ? ` · RNC ${c.rnc}` : ''}</span>
            </button></li>
          ))}
        </ul>
      )}
    </label>
  )
}

function Adelantos({ reserva, opciones, alCambiar, alError }: {
  reserva: Reserva
  opciones: OpcionesReserva
  alCambiar: (r: Reserva) => void
  alError: (e: unknown) => void
}) {
  const [monto, setMonto] = useState('')
  const [forma, setForma] = useState(1)
  const [us, setUs] = useState('')
  const [enviando, setEnviando] = useState(false)
  const activa = reserva.estado === 1
  const DOLARES = 2

  async function registrar() {
    const m = Number(monto.replace(/,/g, ''))
    if (!(m > 0)) return
    if (!window.confirm(`¿Registrar un adelanto de ${dinero(m)} (${opciones.formasPagoAdelanto.find((f) => f.id === forma)?.nombre}) en la reserva ${reserva.id}?`)) return
    setEnviando(true)
    try {
      alCambiar(await registrarAdelanto(reserva.id, Math.round(m * 100) / 100, forma, forma === DOLARES ? Number(us.replace(/,/g, '')) || null : null))
      setMonto('')
      setUs('')
    } catch (e) {
      alError(e)
    } finally {
      setEnviando(false)
    }
  }

  async function anular(a: Reserva['adelantos'][number]) {
    if (!window.confirm(`¿Anular el adelanto ${a.id} de ${dinero(a.monto)}? Queda como anulado (no se borra).`)) return
    try {
      alCambiar(await anularAdelanto(reserva.id, a.id))
    } catch (e) {
      alError(e)
    }
  }

  return (
    <fieldset className="grupo">
      <legend>Adelantos {reserva.totalAdelantos > 0 && <span className="tenue">· activos {dinero(reserva.totalAdelantos)}</span>}</legend>
      {reserva.adelantos.length > 0 && (
        <table className="tabla">
          <thead><tr><th>No.</th><th>Fecha</th><th>Forma de pago</th><th className="num">Monto</th><th className="num">US$</th><th>Estado</th><th></th></tr></thead>
          <tbody>
            {reserva.adelantos.map((a) => (
              <tr key={a.id} className={a.estatus === 'Activa' ? '' : 'inactiva'}>
                <td>{a.id}</td><td>{fecha(a.fecha, true)}</td><td>{a.formaPagoNombre}</td>
                <td className="num">{dinero(a.monto)}</td><td className="num">{a.montoUs ? dinero(a.montoUs) : ''}</td>
                <td>{a.estatus}</td>
                <td>{activa && opciones.anulaAdelantos && a.estatus === 'Activa' &&
                  <button type="button" className="secundario" onClick={() => anular(a)}>Anular</button>}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {activa ? (
        <div className="cuatro alineado">
          <label>Monto (RD$)<input inputMode="decimal" value={monto} onChange={(x) => setMonto(x.target.value)} /></label>
          <label>Forma de pago
            <select value={forma} onChange={(x) => setForma(Number(x.target.value))}>
              {opciones.formasPagoAdelanto.map((f) => <option key={f.id} value={f.id}>{f.nombre}</option>)}
            </select>
          </label>
          {forma === DOLARES
            ? <label>Recibido en US$<input inputMode="decimal" value={us} onChange={(x) => setUs(x.target.value)} /></label>
            : <span />}
          <button type="button" onClick={registrar} disabled={enviando || !(Number(monto.replace(/,/g, '')) > 0)}>Registrar adelanto</button>
        </div>
      ) : <p className="tenue">Los adelantos se registran mientras la reserva está activa.</p>}
      <p className="tenue">El check-in aplica todos los adelantos activos a la cuenta de la habitación.</p>
    </fieldset>
  )
}

function entero(v: string): number {
  return Math.max(0, Math.min(99, Number(v.replace(/\D/g, '')) || 0))
}

function datosDe(r: Reserva): DatosReserva {
  return {
    nombreCliente: r.cliente, clienteId: r.clienteId, contacto: r.contacto ?? '', telefono: r.telefono ?? '',
    huesped: r.huesped ?? '', cedula: r.cedula ?? '', pasaporte: r.pasaporte ?? '', rnc: r.rnc ?? '',
    entrada: r.entrada.slice(0, 10), salida: r.salida.slice(0, 10), comentario: r.comentario ?? '',
    formaPago: r.formaPago ?? 1, categoria: r.categoria ?? 'A',
    lineas: r.lineas.map((l) => ({ tipo: l.tipo, cantidad: l.cantidad, adultos: l.adultos, ninos: l.ninos, bebes: l.bebes,
      credito: l.credito, categoria: l.categoria ?? 'A', tarifa: l.tarifa })),
  }
}
