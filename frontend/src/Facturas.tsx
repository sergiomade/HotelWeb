import { useCallback, useEffect, useState, type FormEvent } from 'react'
import Espera from './Espera'
import Fondo from './Fondo'
import {
  crearNota, dinero, fecha, hoyIso, listarFacturas, prepararNota, reenviarEcf, reenviarNota, reenviarWalkIn, SesionVencida,
  type FacturaCerrada, type PreparacionNota, type ResultadoNota,
} from './api'

const esNota = (f: FacturaCerrada) => f.origen.startsWith('Nota')
const origenRuta = (f: FacturaCerrada) => (f.walkIn ? 'walkin' : 'hospedaje')
const clave = (f: FacturaCerrada) => `${f.origen}-${f.numero}`

/**
 * Las facturas cerradas (check-out y walk-in) y sus notas de credito, con su estado en la DGII: reimprimir, reenviar
 * las que no llegaron y hacer una nota de credito (etapa 7).
 */
export default function Facturas({ alVencer }: { alVencer: () => void }) {
  const [desde, setDesde] = useState(() => hoyIso(-7))
  const [hasta, setHasta] = useState(() => hoyIso())
  const [texto, setTexto] = useState('')
  const [lista, setLista] = useState<FacturaCerrada[] | null>(null)
  const [cargando, setCargando] = useState(false)
  const [enviando, setEnviando] = useState<string | null>(null)
  const [nota, setNota] = useState<FacturaCerrada | null>(null)
  const [error, setError] = useState('')
  const [aviso, setAviso] = useState('')

  const manejar = useCallback(
    (e: unknown) => (e instanceof SesionVencida ? alVencer() : setError(e instanceof Error ? e.message : String(e))),
    [alVencer],
  )

  const cargar = useCallback(() => {
    setCargando(true)
    setError('')
    listarFacturas(desde, hasta, texto.trim()).then(setLista).catch(manejar).finally(() => setCargando(false))
  }, [desde, hasta, texto, manejar])

  // al cambiar las fechas se busca solo; el texto, al dejar de escribir
  useEffect(() => {
    const t = setTimeout(cargar, texto ? 400 : 0)
    return () => clearTimeout(t)
  }, [cargar, texto])

  async function reenviar(f: FacturaCerrada) {
    const que = esNota(f) ? 'nota de crédito' : f.walkIn ? 'walk-in' : 'factura'
    if (!window.confirm(`¿Reenviar el ${f.encf} (${que} #${f.numero}) a la DGII?`)) return
    setEnviando(clave(f))
    setAviso('')
    setError('')
    try {
      const e = await (esNota(f) ? reenviarNota(origenRuta(f), f.numero) : f.walkIn ? reenviarWalkIn(f.numero) : reenviarEcf(f.numero))
      setLista((l) => l?.map((x) => (clave(x) === clave(f) ? { ...x, dgii: e } : x)) ?? null)
      setAviso(`${e.encf}: ${e.estado}${e.mensaje ? ` · ${e.mensaje}` : ''}`)
    } catch (e) {
      manejar(e)
    } finally {
      setEnviando(null)
    }
  }

  function imprimir(f: FacturaCerrada) {
    const ruta = esNota(f) ? `/nota/${origenRuta(f)}/${f.numero}` : f.walkIn ? `/walkin/${f.numero}` : `/factura/${f.numero}`
    window.open(ruta, '_blank')
  }

  const pendientes = lista?.filter((f) => f.dgii && !f.dgii.aceptado).length ?? 0

  return (
    <section>
      {enviando !== null && <Espera texto="Conectando con la DGII, espere..." />}
      <div className="filtros">
        <label className="corto">Desde<input type="date" value={desde} onChange={(e) => setDesde(e.target.value)} /></label>
        <label className="corto">hasta<input type="date" value={hasta} onChange={(e) => setHasta(e.target.value)} /></label>
        <label className="largo">Buscar
          <input placeholder="Habitación, número, e-NCF o nombre" value={texto} onChange={(e) => setTexto(e.target.value)} />
        </label>
        <button type="button" className="secundario" onClick={cargar} disabled={cargando}>{cargando ? 'Consultando la DGII...' : 'Actualizar'}</button>
      </div>
      {pendientes > 0 && <p className="alerta">{pendientes} {pendientes === 1 ? 'documento no está aceptado' : 'documentos no están aceptados'} por la DGII: revise y reenvíe.</p>}
      {aviso && <p className="ok-texto" role="status">{aviso}</p>}
      {error && <p className="error" role="alert">{error}</p>}
      <div className="tabla-marco">
        <table className="tabla catalogo">
          <thead>
            <tr><th className="num">Número</th><th>Fecha</th><th className="num">Hab.</th><th>Cliente / huésped</th><th>e-NCF</th><th className="num">Total</th><th>DGII</th><th></th></tr>
          </thead>
          <tbody>
            {lista?.map((f) => (
              <tr key={clave(f)} className={esNota(f) ? 'fila-nota' : ''}>
                <td className="num">{f.numero}</td>
                <td>{fecha(f.fecha, true)}</td>
                <td className="num">{esNota(f) ? <span className="tenue">N/C</span> : f.walkIn ? <span className="tenue">Walk-in</span> : f.habitacion}</td>
                <td><strong>{f.cliente}</strong>{f.huesped && f.huesped !== f.cliente && <span className="tenue fila-sub">{f.huesped}</span>}</td>
                <td>{f.encf || <span className="tenue">—</span>}{f.modifica && <span className="tenue fila-sub">Modifica {f.modifica}</span>}</td>
                <td className="num">{esNota(f) ? `-${dinero(f.total)}` : dinero(f.total)}</td>
                <td>
                  {f.transferida ? <span className="tenue">Transferida (no va)</span>
                    : !f.dgii ? <span className="tenue">Sin e-NCF</span>
                    : <span className={f.dgii.aceptado ? 'ok-texto' : 'error'} title={f.dgii.mensaje ?? ''}>{f.dgii.estado}</span>}
                  {f.dgii?.mensaje && !f.dgii.aceptado && <span className="tenue fila-sub">{f.dgii.mensaje}</span>}
                </td>
                <td className="acciones-fila">
                  {!f.transferida && f.encf && <button type="button" className="secundario chico" onClick={() => imprimir(f)}>Imprimir</button>}
                  {f.dgii && !f.dgii.aceptado && f.dgii.estado !== 'Desconocido' && (
                    <button type="button" className="chico" disabled={enviando !== null} onClick={() => reenviar(f)}>Reenviar</button>
                  )}
                  {!esNota(f) && f.dgii?.aceptado && (
                    <button type="button" className="secundario chico" onClick={() => setNota(f)}>Nota de crédito</button>
                  )}
                </td>
              </tr>
            ))}
            {lista?.length === 0 && <tr><td colSpan={8} className="tenue">No hay facturas cerradas en esas fechas.</td></tr>}
            {!lista && cargando && <tr><td colSpan={8} className="tenue">Consultando las facturas y la DGII...</td></tr>}
          </tbody>
        </table>
      </div>
      {nota && (
        <NotaCredito factura={nota} alVencer={alVencer}
                     alCerrar={(hecha) => {
                       setNota(null)
                       if (hecha) {
                         setAviso(`${hecha.mensaje} DGII: ${hecha.dgii.estado}${hecha.dgii.mensaje ? ` · ${hecha.dgii.mensaje}` : ''}`)
                         cargar()
                       }
                     }} />
      )}
    </section>
  )
}

/**
 * Nota de credito (E34) a una factura aceptada, como Delphi: anulacion total (sin notas previas) o por un monto (hasta
 * lo pendiente). Autoriza un responsable con su usuario y clave (permiso de cancelar facturas) y el motivo es obligatorio.
 */
function NotaCredito({ factura, alCerrar, alVencer }: {
  factura: FacturaCerrada
  alCerrar: (hecha?: ResultadoNota) => void
  alVencer: () => void
}) {
  const [p, setP] = useState<PreparacionNota | null>(null)
  const [codigo, setCodigo] = useState(1)
  const [monto, setMonto] = useState('')
  const [motivo, setMotivo] = useState('')
  const [responsable, setResponsable] = useState('')
  const [claveResp, setClaveResp] = useState('')
  const [error, setError] = useState('')
  const [enviando, setEnviando] = useState(false)
  const origen = factura.walkIn ? 'walkin' : 'hospedaje'

  const manejar = useCallback((e: unknown) => {
    if (e instanceof SesionVencida) alVencer()
    else setError(e instanceof Error ? e.message : String(e))
  }, [alVencer])

  useEffect(() => {
    prepararNota(origen, factura.numero).then((r) => {
      setP(r)
      setCodigo(r.puedeAnular ? 1 : 3)
    }).catch(manejar)
  }, [origen, factura.numero, manejar])

  useEffect(() => {
    const tecla = (k: KeyboardEvent) => k.key === 'Escape' && !enviando && alCerrar()
    window.addEventListener('keydown', tecla)
    return () => window.removeEventListener('keydown', tecla)
  }, [alCerrar, enviando])

  const m = Number(monto.replace(/,/g, '')) || 0
  const acredita = codigo === 1 ? p?.total ?? 0 : m
  const falta = !p ? 'Cargando la factura'
    : codigo === 1 && !p.puedeAnular ? 'La factura ya tiene notas: use "Por un monto"'
    : codigo === 3 && !(m > 0) ? 'Escriba el monto a acreditar'
    : codigo === 3 && m > p.pendiente ? `El monto pasa de lo pendiente (${dinero(p.pendiente)})`
    : motivo.trim().length < 10 ? `El motivo necesita al menos 10 letras (lleva ${motivo.trim().length})`
    : responsable.trim() === '' ? 'Escriba el usuario que autoriza'
    : claveResp === '' ? 'Escriba la clave de quien autoriza'
    : ''
  const listo = falta === ''

  async function enviar(ev: FormEvent) {
    ev.preventDefault()
    if (!p || !listo) return
    const que = codigo === 1 ? `anular el ${p.encf} con una nota por ${dinero(p.total)}` : `hacer una nota de crédito por ${dinero(m)} al ${p.encf}`
    if (!window.confirm(`¿Seguro que desea ${que}? Se envía a la DGII y no se puede deshacer.`)) return
    setEnviando(true)
    setError('')
    try {
      alCerrar(await crearNota(origen, factura.numero, { codigo, monto: codigo === 1 ? p.total : m, motivo: motivo.trim(), responsable: responsable.trim(), clave: claveResp }))
    } catch (e) {
      manejar(e)
      setClaveResp('')
    } finally {
      setEnviando(false)
    }
  }

  return (
    <Fondo centro>
      {enviando && <Espera texto="Conectando con la DGII, espere..." />}
      <div className="dialogo" role="dialog" aria-modal="true" aria-label="Nota de crédito">
        <h2>Nota de crédito · {factura.encf}</h2>
        {!p && !error && <p className="tenue">Consultando la factura en la DGII...</p>}
        {p && (
          <form onSubmit={enviar} className="grupo-cargo">
            <dl className="datos-lista">
              <dt>Factura</dt><dd>{factura.walkIn ? 'Walk-in' : 'Check-out'} #{p.factura} · {fecha(p.fecha, true)} · hace {p.dias} {p.dias === 1 ? 'día' : 'días'}</dd>
              <dt>Cliente</dt><dd>{p.cliente}{p.rnc && p.rnc.replace(/0/g, '') ? ` · ${p.rnc}` : ''}</dd>
              <dt>Total</dt><dd>{dinero(p.total)} <span className="tenue">({dinero(p.subTotal)} + ITBIS {dinero(p.itbis)}{p.propina ? ` + propina ${dinero(p.propina)}` : ''})</span></dd>
              {p.notas.length > 0 && <><dt>Notas previas</dt><dd>{p.notas.map((n) => `${n.encf ?? '#' + n.numero} por ${dinero(n.total)}`).join(', ')}</dd></>}
              <dt>Pendiente</dt><dd><strong>{dinero(p.pendiente)}</strong></dd>
            </dl>
            {p.sinItbis && <p className="alerta">Pasaron más de 30 días: la nota va sin rebajar el ITBIS (no se le devuelve el ITBIS al cliente).</p>}
            <div className="modo-mover" role="radiogroup" aria-label="Tipo de nota">
              <label className="check" title={p.puedeAnular ? '' : 'Solo si la factura no tiene notas previas'}>
                <input type="radio" checked={codigo === 1} disabled={!p.puedeAnular} onChange={() => setCodigo(1)} /> Anulación total ({dinero(p.total)})
              </label>
              <label className="check">
                <input type="radio" checked={codigo === 3} disabled={!p.puedeMonto} onChange={() => setCodigo(3)} /> Por un monto (corrección)
              </label>
            </div>
            {codigo === 3 && (
              <label>Monto a acreditar (con impuestos)
                <input inputMode="decimal" value={monto} onChange={(e) => setMonto(e.target.value)} placeholder={`Hasta ${dinero(p.pendiente)}`} autoFocus />
              </label>
            )}
            <label>Motivo (va en la nota a la DGII)
              <input value={motivo} maxLength={90} onChange={(e) => setMotivo(e.target.value)} placeholder="Mínimo 10 letras" />
            </label>
            <div className="dos">
              <label>Autoriza (usuario)<input value={responsable} onChange={(e) => setResponsable(e.target.value)} autoComplete="off" /></label>
              <label>Clave<input type="password" value={claveResp} maxLength={10} onChange={(e) => setClaveResp(e.target.value)} autoComplete="new-password" /></label>
            </div>
            <p className="tenue">Autoriza un responsable con permiso de cancelar facturas. Se acreditan {dinero(acredita)}.</p>
            {error && <p className="error" role="alert">{error}</p>}
            {!error && falta && <p className="alerta">Falta: {falta}.</p>}
            <div className="botones">
              <button type="button" className="secundario" onClick={() => alCerrar()} disabled={enviando}>Cancelar</button>
              <button type="submit" disabled={enviando || !listo}>{codigo === 1 ? 'Anular la factura' : 'Hacer la nota'}</button>
            </div>
          </form>
        )}
        {!p && error && (
          <>
            <p className="error" role="alert">{error}</p>
            <div className="botones"><button type="button" onClick={() => alCerrar()}>Cerrar</button></div>
          </>
        )}
      </div>
    </Fondo>
  )
}
