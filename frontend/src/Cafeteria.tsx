import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent } from 'react'
import CobroCafeteria from './CobroCafeteria'
import Fondo from './Fondo'
import {
  agregarArticulo, agregarEspecial, articulosCafeteria, borrarLineaMesa, cambiarLineaMesa, cuentaMesa, dinero, mesasCafeteria,
  moverMesa, preCuentaMesa, SesionVencida,
  type ArticuloCafeteria, type CuentaMesa, type EstadoMesa, type LineaMesa, type MesaCafeteria,
} from './api'

const CADA = 30_000 // Delphi y otras cajas cambian las mismas mesas
const NOMBRE: Record<EstadoMesa, string> = { Libre: 'Libre', Abierta: 'Abierta', PreCuenta: 'Pre-cuenta' }

const hora = (iso: string | null) => (iso ? new Date(iso).toLocaleTimeString('es-DO', { hour: 'numeric', minute: '2-digit' }) : '')

/**
 * Cafeteria (etapa 8a), como PrjCaja de Delphi: las mesas y la cuenta abierta de cada una. Se agregan articulos del
 * menu (por codigo o por familia), se imprime la pre-cuenta (lo impreso queda fijo) y se mueve la cuenta de mesa.
 * El cobro con e-CF es la parte 8b.
 */
export default function Cafeteria({ alVencer }: { alVencer: () => void }) {
  const [mesas, setMesas] = useState<MesaCafeteria[] | null>(null)
  const [articulos, setArticulos] = useState<ArticuloCafeteria[]>([])
  const [abierta, setAbierta] = useState<number | null>(null)
  const [error, setError] = useState('')

  const manejar = useCallback(
    (e: unknown) => (e instanceof SesionVencida ? alVencer() : setError(e instanceof Error ? e.message : String(e))),
    [alVencer],
  )

  const cargar = useCallback(() => {
    mesasCafeteria().then((m) => { setMesas(m); setError('') }).catch(manejar)
  }, [manejar])

  useEffect(() => {
    cargar()
    articulosCafeteria().then(setArticulos).catch(manejar)
    const t = setInterval(cargar, CADA)
    return () => clearInterval(t)
  }, [cargar, manejar])

  const conteo = useMemo(() => {
    const c: Record<EstadoMesa, number> = { Libre: 0, Abierta: 0, PreCuenta: 0 }
    mesas?.forEach((m) => c[m.estado]++)
    return c
  }, [mesas])

  if (!mesas) return error ? <p className="error">{error}</p> : <p className="tenue">Cargando las mesas...</p>

  return (
    <section className="cafeteria">
      <div className="rack-cabecera">
        <h2>Cafetería</h2>
        <div className="leyenda">
          {(['Abierta', 'PreCuenta', 'Libre'] as EstadoMesa[]).map((e) => (
            <span key={e} className={`chip m-${e}`}><i aria-hidden="true" /> {NOMBRE[e]} {conteo[e]}</span>
          ))}
          <button type="button" className="secundario" onClick={cargar}>Actualizar</button>
        </div>
      </div>
      {error && <p className="error" role="alert">{error}</p>}
      <div className={abierta === null ? 'cafe-cuerpo' : 'cafe-cuerpo con-cuenta'}>
        <div className="cafe-mesas" role="list" aria-label="Mesas">
          {mesas.map((m) => (
            <button key={m.mesa} type="button" role="listitem" className={`mesa m-${m.estado}${abierta === m.mesa ? ' elegida' : ''}`}
                    onClick={() => setAbierta(m.mesa)} aria-label={`Mesa ${m.mesa}: ${NOMBRE[m.estado]}${m.factura ? `, ${dinero(m.total)}` : ''}`}>
              <span className="num">{m.mesa}</span>
              {m.estado === 'Libre' ? <span className="tenue">libre</span> : <>
                <span>{dinero(m.total)}</span>
                <span className="tenue">{m.estado === 'PreCuenta' ? 'pre-cuenta' : `desde ${hora(m.abierta)}`}</span>
              </>}
            </button>
          ))}
        </div>
        {abierta !== null && (
          <Comanda key={abierta} mesa={abierta} mesas={mesas} articulos={articulos} alCambiar={cargar} alError={manejar}
                   alCerrar={() => setAbierta(null)} alMover={(d) => { setAbierta(d); cargar() }} />
        )}
      </div>
    </section>
  )
}

function Comanda({ mesa, mesas, articulos, alCambiar, alError, alCerrar, alMover }: {
  mesa: number
  mesas: MesaCafeteria[]
  articulos: ArticuloCafeteria[]
  alCambiar: () => void
  alError: (e: unknown) => void
  alCerrar: () => void
  alMover: (destino: number) => void
}) {
  const [cuenta, setCuenta] = useState<CuentaMesa | null | undefined>(undefined)
  const [ocupado, setOcupado] = useState(false)
  const [aviso, setAviso] = useState('')
  const [buscar, setBuscar] = useState('')
  const [familia, setFamilia] = useState<number | null>(null)
  const [dialogo, setDialogo] = useState<'mover' | 'especial' | 'cobrar' | null>(null)
  const [cobrada, setCobrada] = useState(false)
  const buscador = useRef<HTMLInputElement>(null)

  useEffect(() => {
    cuentaMesa(mesa).then(setCuenta).catch(alError)
  }, [mesa, alError])

  const familias = useMemo(() => {
    const f = new Map<number, string>()
    articulos.forEach((a) => f.set(a.familia, a.nombreFamilia))
    return [...f.entries()]
  }, [articulos])
  const porReferencia = useMemo(() => new Map(articulos.map((a) => [a.referencia, a])), [articulos])

  const t = buscar.trim().toLowerCase()
  const encontrados = t
    ? articulos.filter((a) => a.referencia.toLowerCase() === t || a.referencia.toLowerCase().startsWith(t) || a.descripcion.toLowerCase().includes(t)).slice(0, 24)
    : familia !== null ? articulos.filter((a) => a.familia === familia) : []

  async function hacer(accion: () => Promise<CuentaMesa | null>, despues?: () => void) {
    if (ocupado) return
    setOcupado(true)
    setAviso('')
    try {
      setCuenta(await accion())
      alCambiar()
      despues?.()
    } catch (e) {
      if (e instanceof SesionVencida) alError(e)
      else setAviso(e instanceof Error ? e.message : String(e))
    } finally {
      setOcupado(false)
    }
  }

  function agregar(a: ArticuloCafeteria) {
    hacer(() => agregarArticulo(mesa, a.referencia, 1), () => { setBuscar(''); buscador.current?.focus() })
  }

  function alBuscar(ev: FormEvent) {
    ev.preventDefault()
    const exacto = articulos.find((a) => a.referencia.toLowerCase() === t)
    const a = exacto ?? (encontrados.length === 1 ? encontrados[0] : null)
    if (a) agregar(a)
    else if (t) setAviso(encontrados.length ? 'Hay varios: toque el que quiere.' : 'No hay artículos con ese código o nombre.')
  }

  function cantidad(l: LineaMesa, n: number) {
    if (n < 1) return
    hacer(() => cambiarLineaMesa(mesa, l.secuencia, n))
  }

  function borrar(l: LineaMesa) {
    if (!window.confirm(`¿Borrar ${l.cantidad > 1 ? `${l.cantidad} x ` : ''}${l.descripcion} de la mesa ${mesa}?`)) return
    hacer(() => borrarLineaMesa(mesa, l.secuencia))
  }

  async function preCuenta() {
    if (!cuenta || ocupado) return
    // la pestana se abre antes de esperar a la API: si no, el navegador la bloquea
    const w = window.open('', '_blank')
    setOcupado(true)
    setAviso('')
    try {
      const p = await preCuentaMesa(mesa)
      setCuenta(p.cuenta)
      alCambiar()
      if (w) w.location.href = `/precuenta/${p.cuenta.factura}`
      else setAviso('El navegador bloqueó la pestaña de la pre-cuenta: permita las ventanas emergentes.')
    } catch (e) {
      w?.close()
      if (e instanceof SesionVencida) alError(e)
      else setAviso(e instanceof Error ? e.message : String(e))
    } finally {
      setOcupado(false)
    }
  }

  const tot = cuenta?.totales
  return (
    <aside className="comanda" aria-label={`Mesa ${mesa}`}>
      <header className="comanda-cabeza">
        <div>
          <h3>Mesa {mesa}</h3>
          <p className="tenue">{cuenta ? `Cuenta #${cuenta.factura} · desde ${hora(cuenta.abierta)}` : cuenta === null ? 'Libre: agregue algo para abrir la cuenta' : 'Cargando...'}</p>
        </div>
        <button type="button" className="secundario" onClick={alCerrar} aria-label="Cerrar la mesa">✕</button>
      </header>

      {cuenta && cuenta.lineas.length > 0 && (
        <table className="tabla comanda-lineas">
          <tbody>
            {cuenta.lineas.map((l) => {
              const a = porReferencia.get(l.referencia)
              const puedeTrago = !!a && a.fraccion > 1 && a.precio2 >= 1 && a.precio >= 1
              return (
                <tr key={l.secuencia} className={l.impreso ? 'impresa' : ''}>
                  <td>
                    {l.descripcion}
                    <span className="ticket-sub">
                      {dinero(l.precio)}{l.tragos === 2 ? ' · trago' : a && a.fraccion > 1 ? ' · botella' : ''}{l.impreso ? ' · impreso' : ''}
                    </span>
                  </td>
                  <td className="cant">
                    {l.impreso ? <span>{l.cantidad}</span> : <>
                      <button type="button" className="secundario" disabled={ocupado || l.cantidad <= 1} onClick={() => cantidad(l, l.cantidad - 1)} aria-label="Uno menos">−</button>
                      <span>{l.cantidad}</span>
                      <button type="button" className="secundario" disabled={ocupado} onClick={() => cantidad(l, l.cantidad + 1)} aria-label="Uno más">+</button>
                    </>}
                  </td>
                  <td className="num">{dinero(l.importe)}</td>
                  <td className="acciones">
                    {!l.impreso && puedeTrago && (
                      <button type="button" className="secundario" disabled={ocupado} title={l.tragos === 2 ? 'Cambiar a botella' : 'Cambiar a trago'}
                              onClick={() => hacer(() => cambiarLineaMesa(mesa, l.secuencia, l.cantidad, l.tragos !== 2))}>
                        {l.tragos === 2 ? 'Botella' : 'Trago'}
                      </button>
                    )}
                    {!l.impreso && <button type="button" className="secundario" disabled={ocupado} onClick={() => borrar(l)} aria-label={`Borrar ${l.descripcion}`}>Borrar</button>}
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
      )}

      {tot && (
        <dl className="totales comanda-totales">
          <dt>Subtotal</dt><dd>{dinero(tot.subTotal)}</dd>
          {cuenta.conItbis && <><dt>ITBIS 18 %</dt><dd>{dinero(tot.itbis)}</dd></>}
          {cuenta.conPropina && <><dt>Propina 10 %</dt><dd>{dinero(tot.propina)}</dd></>}
          <dt className="saldo">Total</dt><dd className="saldo">{dinero(tot.total)}</dd>
          {cuenta.tasaDolar > 0 && <><dt className="tenue">En dólares</dt><dd className="tenue">US$ {dinero(tot.total / cuenta.tasaDolar)}</dd></>}
        </dl>
      )}

      {aviso && <p className="error" role="alert">{aviso}</p>}
      {cuenta && cuenta.lineas.length > 0 && !cuenta.preCuentaAlDia && (
        <p className="alerta">{cuenta.lineas.some((l) => l.impreso) ? 'Hay cosas nuevas sin imprimir: saque otra pre-cuenta antes de cobrar.' : 'Falta la pre-cuenta para poder cobrar.'}</p>
      )}

      <div className="botones comanda-botones">
        <button type="button" className={cuenta?.preCuentaAlDia ? 'secundario' : ''} disabled={ocupado || !cuenta?.lineas.length} onClick={preCuenta}>Pre-cuenta</button>
        <button type="button" disabled={ocupado || !cuenta?.preCuentaAlDia} onClick={() => setDialogo('cobrar')}>Cobrar</button>
        <button type="button" className="secundario" disabled={ocupado || !cuenta} onClick={() => setDialogo('mover')}>Mover de mesa</button>
        <button type="button" className="secundario" disabled={ocupado} onClick={() => setDialogo('especial')}>Artículo especial</button>
      </div>

      <form className="comanda-buscar" onSubmit={alBuscar}>
        <label>Agregar (código o nombre, Enter agrega)
          <input ref={buscador} value={buscar} onChange={(e) => { setBuscar(e.target.value); setAviso('') }} placeholder="Ej.: 0704 o refresco"
                 autoFocus />
        </label>
      </form>
      {!t && (
        <div className="pestanas comanda-familias" role="tablist" aria-label="Familias">
          {familias.map(([id, nombre]) => (
            <button key={id} type="button" role="tab" aria-selected={familia === id} className={familia === id ? 'activo' : ''}
                    onClick={() => setFamilia(familia === id ? null : id)}>{nombre}</button>
          ))}
        </div>
      )}
      {encontrados.length > 0 && (
        <div className="comanda-articulos">
          {encontrados.map((a) => (
            <button key={a.referencia} type="button" className="secundario" disabled={ocupado} onClick={() => agregar(a)}>
              <span>{a.descripcion}</span>
              <span className="tenue">{a.referencia} · {a.fraccion > 1 && a.precio2 >= 1 ? `trago ${dinero(a.precio2)}` : dinero(a.precio)}</span>
            </button>
          ))}
        </div>
      )}

      {dialogo === 'mover' && cuenta && (
        <Mover mesa={mesa} mesas={mesas} alCerrar={() => setDialogo(null)}
               alElegir={(d) => hacer(() => moverMesa(mesa, d), () => { setDialogo(null); alMover(d) })} ocupado={ocupado} aviso={aviso} />
      )}
      {dialogo === 'cobrar' && cuenta && (
        <CobroCafeteria cuenta={cuenta} alCobrar={() => { setCobrada(true); alCambiar() }}
                        alCerrar={() => { setDialogo(null); if (cobrada) alCerrar() }}
                        alVencer={() => alError(new SesionVencida('La sesion vencio: ingrese de nuevo.'))} />
      )}
      {dialogo === 'especial' && (
        <Especial alCerrar={() => setDialogo(null)} ocupado={ocupado} aviso={aviso}
                  alGuardar={(bebida, d, p) => hacer(() => agregarEspecial(mesa, bebida, d, p), () => setDialogo(null))} />
      )}
    </aside>
  )
}

function Mover({ mesa, mesas, alCerrar, alElegir, ocupado, aviso }: {
  mesa: number
  mesas: MesaCafeteria[]
  alCerrar: () => void
  alElegir: (destino: number) => void
  ocupado: boolean
  aviso: string
}) {
  function elegir(m: MesaCafeteria) {
    if (m.estado !== 'Libre' && !window.confirm(`La mesa ${m.mesa} tiene cuenta abierta (${dinero(m.total)}). ¿Unir la cuenta de la mesa ${mesa} con la de la mesa ${m.mesa}?`)) return
    alElegir(m.mesa)
  }
  return (
    <Fondo centro alCerrar={ocupado ? undefined : alCerrar}>
      <div className="dialogo ancho" role="dialog" aria-modal="true" aria-label="Mover de mesa">
        <h2>Mover la mesa {mesa} a...</h2>
        <p className="tenue">Libre: la cuenta pasa tal cual. Ocupada: se une con la cuenta de esa mesa.</p>
        <div className="cafe-mesas chica">
          {mesas.filter((m) => m.mesa !== mesa).map((m) => (
            <button key={m.mesa} type="button" className={`mesa m-${m.estado}`} disabled={ocupado} onClick={() => elegir(m)}>
              <span className="num">{m.mesa}</span>
              <span className="tenue">{m.estado === 'Libre' ? 'libre' : dinero(m.total)}</span>
            </button>
          ))}
        </div>
        {aviso && <p className="error" role="alert">{aviso}</p>}
        <div className="botones"><button type="button" className="secundario" onClick={alCerrar} disabled={ocupado}>Cancelar</button></div>
      </div>
    </Fondo>
  )
}

function Especial({ alCerrar, alGuardar, ocupado, aviso }: {
  alCerrar: () => void
  alGuardar: (bebida: boolean, descripcion: string, precio: number) => void
  ocupado: boolean
  aviso: string
}) {
  const [bebida, setBebida] = useState(false)
  const [descripcion, setDescripcion] = useState('')
  const [precio, setPrecio] = useState('')
  const p = Number(precio.replace(/,/g, '')) || 0
  const listo = descripcion.trim().length >= 3 && p >= 1

  useEffect(() => {
    const tecla = (k: KeyboardEvent) => k.key === 'Escape' && !ocupado && alCerrar()
    window.addEventListener('keydown', tecla)
    return () => window.removeEventListener('keydown', tecla)
  }, [alCerrar, ocupado])

  return (
    <Fondo centro>
      <form className="dialogo" role="dialog" aria-modal="true" aria-label="Artículo especial"
            onSubmit={(e) => { e.preventDefault(); if (listo) alGuardar(bebida, descripcion.trim(), p) }}>
        <h2>Artículo especial</h2>
        <p className="tenue">Lo que no está en el menú: descripción y precio a mano.</p>
        <div className="modo-mover" role="radiogroup" aria-label="Tipo">
          <label className="check"><input type="radio" checked={!bebida} onChange={() => setBebida(false)} /> Comida</label>
          <label className="check"><input type="radio" checked={bebida} onChange={() => setBebida(true)} /> Bebida</label>
        </div>
        <label>Descripción<input value={descripcion} maxLength={25} onChange={(e) => setDescripcion(e.target.value)} autoFocus /></label>
        <label>Precio (sin ITBIS ni propina)<input inputMode="decimal" value={precio} onChange={(e) => setPrecio(e.target.value)} /></label>
        {p >= 1 && <p className="tenue">Con ITBIS y propina, el cliente paga unos {dinero(Math.trunc(p * 128) / 100)}.</p>}
        {aviso && <p className="error" role="alert">{aviso}</p>}
        <div className="botones">
          <button type="button" className="secundario" onClick={alCerrar} disabled={ocupado}>Cancelar</button>
          <button type="submit" disabled={ocupado || !listo}>Agregar</button>
        </div>
      </form>
    </Fondo>
  )
}
