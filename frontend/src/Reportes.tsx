import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import {
  dinero, fecha, hoyIso, reporteCanceladas, reporteComprobantes, reporteVentasCafeteria, reporteVentasHotel, SesionVencida,
  type Comprobante, type FacturaCancelada, type ReporteComprobantes, type VentaCafeteria, type VentaHotel,
} from './api'

type Pestana = 'hotel' | 'cafeteria' | 'comprobantes' | 'canceladas'
const NOMBRES: Record<Pestana, string> = {
  hotel: 'Ventas del hotel', cafeteria: 'Ventas de la cafetería', comprobantes: 'Comprobantes (NCF)', canceladas: 'Canceladas sin cobro',
}

/** Una columna de un reporte: el titulo, como se ve, el valor para Excel y si se suma. */
interface Columna<T> {
  titulo: string
  ver: (f: T) => ReactNode
  csv: (f: T) => string | number
  num?: boolean
  suma?: (f: T) => number
}

const t = (s: string | null | undefined) => s ?? ''

/**
 * Reportes de ventas y comprobantes (etapa 9b), como los de Delphi: ventas del periodo (Stp_ListadodeFacturas), ventas
 * de la cafeteria (Stp_ListadoVentasDiarias), comprobantes por tipo (Stp_ListaFacturasxComprobante, con las notas E34) y
 * las facturas canceladas sin cobro. Cada uno se imprime o se pasa a Excel.
 */
export default function Reportes({ alVencer }: { alVencer: () => void }) {
  const [p, setP] = useState<Pestana>('hotel')
  const [desde, setDesde] = useState(() => hoyIso(-7))
  const [hasta, setHasta] = useState(() => hoyIso())

  return (
    <section className="catalogos reportes">
      <h2 className="no-imprimir">Reportes</h2>
      <div className="pestanas no-imprimir" role="tablist">
        {(Object.keys(NOMBRES) as Pestana[]).map((x) => (
          <button key={x} type="button" role="tab" aria-selected={p === x} className={p === x ? 'activo' : ''} onClick={() => setP(x)}>{NOMBRES[x]}</button>
        ))}
      </div>
      <div className="filtros no-imprimir">
        <label className="corto">Desde<input type="date" value={desde} onChange={(e) => setDesde(e.target.value)} /></label>
        <label className="corto">hasta<input type="date" value={hasta} onChange={(e) => setHasta(e.target.value)} /></label>
        <button type="button" className="secundario" onClick={() => setDesde(hasta)}>Solo un día</button>
      </div>
      {p === 'hotel' && <VentasHotel desde={desde} hasta={hasta} alVencer={alVencer} />}
      {p === 'cafeteria' && <VentasCafeteria desde={desde} hasta={hasta} alVencer={alVencer} />}
      {p === 'comprobantes' && <Comprobantes desde={desde} hasta={hasta} alVencer={alVencer} />}
      {p === 'canceladas' && <Canceladas desde={desde} hasta={hasta} alVencer={alVencer} />}
    </section>
  )
}

/** Carga el reporte al cambiar las fechas. */
function useReporte<T>(cargar: (d: string, h: string) => Promise<T>, desde: string, hasta: string, alVencer: () => void) {
  const [datos, setDatos] = useState<T | null>(null)
  const [error, setError] = useState('')
  const [cargando, setCargando] = useState(false)
  const leer = useCallback(() => {
    if (!desde || !hasta) return
    setCargando(true)
    setError('')
    cargar(desde, hasta).then(setDatos)
      .catch((e) => (e instanceof SesionVencida ? alVencer() : setError(e instanceof Error ? e.message : String(e))))
      .finally(() => setCargando(false))
  }, [cargar, desde, hasta, alVencer])
  useEffect(leer, [leer])
  return { datos, error, cargando, leer }
}

function Tabla<T>({ titulo, desde, hasta, filas, columnas, archivo, clase, pie }: {
  titulo: string
  desde: string
  hasta: string
  filas: T[]
  columnas: Columna<T>[]
  archivo: string
  clase?: (f: T) => string
  pie?: ReactNode
}) {
  const totales = useMemo(() => columnas.map((c) => (c.suma ? filas.reduce((s, f) => s + c.suma!(f), 0) : null)), [columnas, filas])

  function excel() {
    const celda = (v: string | number) => (typeof v === 'number' ? v.toFixed(2) : `"${v.replace(/"/g, '""')}"`)
    const lineas = [
      columnas.map((c) => celda(c.titulo)).join(','),
      ...filas.map((f) => columnas.map((c) => celda(c.csv(f))).join(',')),
      columnas.map((_, i) => (totales[i] !== null ? celda(totales[i]!) : i === 0 ? celda('TOTAL') : '')).join(','),
    ]
    // con BOM para que Excel lea los acentos
    const blob = new Blob(['﻿' + lineas.join('\r\n')], { type: 'text/csv;charset=utf-8' })
    const a = document.createElement('a')
    a.href = URL.createObjectURL(blob)
    a.download = `${archivo}_${desde}_${hasta}.csv`
    a.click()
    URL.revokeObjectURL(a.href)
  }

  return (
    <>
      <div className="reporte-cabeza">
        <h3>{titulo} · {desde === hasta ? fecha(desde, true) : `del ${fecha(desde, true)} al ${fecha(hasta, true)}`} · {filas.length} {filas.length === 1 ? 'línea' : 'líneas'}</h3>
        <div className="botones no-imprimir">
          <button type="button" className="secundario" onClick={() => window.print()} disabled={!filas.length}>Imprimir</button>
          <button type="button" className="secundario" onClick={excel} disabled={!filas.length}>Excel</button>
        </div>
      </div>
      {pie}
      <div className="tabla-marco">
        <table className="tabla catalogo reporte">
          <thead><tr>{columnas.map((c, i) => <th key={i} className={c.num ? 'num' : ''}>{c.titulo}</th>)}</tr></thead>
          <tbody>
            {filas.map((f, k) => (
              <tr key={k} className={clase?.(f) ?? ''}>{columnas.map((c, i) => <td key={i} className={c.num ? 'num' : ''}>{c.ver(f)}</td>)}</tr>
            ))}
            {filas.length === 0 && <tr><td colSpan={columnas.length} className="tenue">No hay datos en esas fechas.</td></tr>}
          </tbody>
          {filas.length > 0 && (
            <tfoot>
              <tr>{columnas.map((c, i) => <td key={i} className={c.num ? 'num' : ''}>{totales[i] !== null ? <strong>{dinero(totales[i])}</strong> : i === 0 ? <strong>TOTAL</strong> : ''}</td>)}</tr>
            </tfoot>
          )}
        </table>
      </div>
    </>
  )
}

const m = <T,>(titulo: string, v: (f: T) => number, sumar = true): Columna<T> =>
  ({ titulo, ver: (f) => dinero(v(f)), csv: v, num: true, suma: sumar ? v : undefined })

function Estado({ e, d }: { e: string | null; d: { error: string; cargando: boolean } }) {
  if (d.error) return <p className="error" role="alert">{d.error}</p>
  if (d.cargando && !e) return <p className="tenue">Cargando el reporte...</p>
  return null
}

function VentasHotel({ desde, hasta, alVencer }: { desde: string; hasta: string; alVencer: () => void }) {
  const r = useReporte(reporteVentasHotel, desde, hasta, alVencer)
  const filas = (r.datos ?? []) as VentaHotel[]
  const cuenta = (x: VentaHotel) => (x.transferida ? 0 : 1)
  const columnas: Columna<VentaHotel>[] = [
    { titulo: 'Origen', ver: (f) => f.origen, csv: (f) => f.origen },
    { titulo: 'Número', ver: (f) => f.numero, csv: (f) => String(f.numero) },
    { titulo: 'Fecha', ver: (f) => fecha(f.fecha, true), csv: (f) => f.fecha.slice(0, 10) },
    { titulo: 'Hab.', ver: (f) => f.habitacion, csv: (f) => t(f.habitacion) },
    { titulo: 'Cliente', ver: (f) => f.cliente, csv: (f) => t(f.cliente) },
    { titulo: 'e-NCF', ver: (f) => (f.transferida ? <span className="tenue">Transferida</span> : f.encf), csv: (f) => (f.transferida ? 'Transferida' : t(f.encf)) },
    m('Subtotal', (f) => f.subTotal * cuenta(f)),
    m('ITBIS', (f) => f.itbis * cuenta(f)),
    m('Propina', (f) => f.propina * cuenta(f)),
    m('Total', (f) => f.total * cuenta(f)),
  ]
  return (
    <>
      <Estado e={r.datos ? 'ok' : null} d={r} />
      {r.datos && <Tabla titulo="Ventas del hotel (check-out y walk-in)" desde={desde} hasta={hasta} filas={filas} columnas={columnas} archivo="ventas_hotel"
                         clase={(f) => (f.transferida ? 'apagada' : '')}
                         pie={<p className="tenue no-imprimir">Las cuentas transferidas a otra habitación no suman: su consumo va en la factura de la otra.</p>} />}
    </>
  )
}

function VentasCafeteria({ desde, hasta, alVencer }: { desde: string; hasta: string; alVencer: () => void }) {
  const r = useReporte(reporteVentasCafeteria, desde, hasta, alVencer)
  const filas = (r.datos ?? []) as VentaCafeteria[]
  const excesos = filas.filter((f) => f.exceso > 0).length
  const columnas: Columna<VentaCafeteria>[] = [
    { titulo: 'Factura', ver: (f) => f.numero, csv: (f) => String(f.numero) },
    { titulo: 'Fecha', ver: (f) => fecha(f.fecha, true), csv: (f) => f.fecha.slice(0, 10) },
    { titulo: 'Mesa', ver: (f) => f.mesa, csv: (f) => String(f.mesa) },
    { titulo: 'Cliente', ver: (f) => f.cliente, csv: (f) => t(f.cliente) },
    { titulo: 'e-NCF', ver: (f) => f.encf ?? (f.habitacionId ? <span className="tenue">Hab. {f.habitacionId}</span> : ''), csv: (f) => f.encf ?? (f.habitacionId ? `Hab. ${f.habitacionId}` : '') },
    m('Alimentos', (f) => f.alimentos),
    m('Bebidas', (f) => f.bebidas),
    m('Otros', (f) => f.otros),
    m('Subtotal', (f) => f.subTotal),
    m('ITBIS', (f) => f.itbis),
    m('Propina', (f) => f.propina),
    m('Total', (f) => f.total),
    m('Efectivo', (f) => f.efectivo),
    m('Tarjeta', (f) => f.tarjeta),
    m('Crédito', (f) => f.credito),
    m('A habitación', (f) => f.habitacion),
    m('Pagado de más', (f) => f.exceso),
  ]
  return (
    <>
      <Estado e={r.datos ? 'ok' : null} d={r} />
      {r.datos && <Tabla titulo="Ventas de la cafetería" desde={desde} hasta={hasta} filas={filas} columnas={columnas} archivo="ventas_cafeteria"
                         clase={(f) => (f.exceso > 0 ? 'fila-alerta' : '')}
                         pie={excesos > 0 && <p className="alerta">{excesos} {excesos === 1 ? 'factura tiene' : 'facturas tienen'} un pago mayor que el total (cobradas en Delphi, que lo permitía: casi siempre la tarjeta mal digitada). Las formas de pago salen como se grabaron.</p>} />}
    </>
  )
}

function Comprobantes({ desde, hasta, alVencer }: { desde: string; hasta: string; alVencer: () => void }) {
  const r = useReporte(reporteComprobantes, desde, hasta, alVencer)
  const d = r.datos as ReporteComprobantes | null
  const signo = (c: Comprobante) => (c.tipo === 'E34' || c.tipo === 'B04' ? -1 : 1)
  const columnas: Columna<Comprobante>[] = [
    { titulo: 'Tipo', ver: (f) => f.tipo, csv: (f) => f.tipo },
    { titulo: 'NCF', ver: (f) => f.ncf, csv: (f) => f.ncf },
    { titulo: 'Fecha', ver: (f) => fecha(f.fecha, true), csv: (f) => f.fecha.slice(0, 10) },
    { titulo: 'Origen', ver: (f) => `${f.origen} #${f.numero}`, csv: (f) => `${f.origen} #${f.numero}` },
    { titulo: 'RNC / cédula', ver: (f) => f.rnc, csv: (f) => t(f.rnc) },
    { titulo: 'Cliente', ver: (f) => f.cliente, csv: (f) => t(f.cliente) },
    m('Base', (f) => f.base * signo(f)),
    m('ITBIS', (f) => f.itbis * signo(f)),
    m('Propina', (f) => f.propina * signo(f)),
    m('Total', (f) => f.total * signo(f)),
    { titulo: 'DGII', ver: (f) => <span className={f.estado?.startsWith('Aceptado') ? 'ok-texto' : f.estado ? 'error' : 'tenue'}>{f.estado ?? '—'}</span>, csv: (f) => t(f.estado) },
    { titulo: 'Modifica', ver: (f) => f.modifica, csv: (f) => t(f.modifica) },
  ]
  const resumen = d && (
    <table className="tabla reporte-resumen">
      <thead><tr><th>Tipo</th><th className="num">Cantidad</th><th className="num">Base</th><th className="num">ITBIS</th><th className="num">Propina</th><th className="num">Total</th></tr></thead>
      <tbody>{d.porTipo.map((x) => (
        <tr key={x.tipo}><td>{x.tipo}</td><td className="num">{x.cantidad}</td><td className="num">{dinero(x.base)}</td><td className="num">{dinero(x.itbis)}</td><td className="num">{dinero(x.propina)}</td><td className="num">{dinero(x.total)}</td></tr>
      ))}</tbody>
    </table>
  )
  const malos = d?.lista.filter((c) => c.estado && !c.estado.startsWith('Aceptado')).length ?? 0
  return (
    <>
      <Estado e={d ? 'ok' : null} d={r} />
      {d && <Tabla titulo="Comprobantes fiscales" desde={desde} hasta={hasta} filas={d.lista} columnas={columnas} archivo="comprobantes"
                   pie={<>{resumen}{malos > 0 && <p className="alerta no-imprimir">{malos} {malos === 1 ? 'comprobante no está aceptado' : 'comprobantes no están aceptados'} (según la última consulta guardada): revíselos en la lista de facturas.</p>}
                     <p className="tenue no-imprimir">Las notas de crédito restan. El estado es el último que se guardó al enviar o consultar.</p></>} />}
    </>
  )
}

function Canceladas({ desde, hasta, alVencer }: { desde: string; hasta: string; alVencer: () => void }) {
  const r = useReporte(reporteCanceladas, desde, hasta, alVencer)
  const filas = (r.datos ?? []) as FacturaCancelada[]
  const columnas: Columna<FacturaCancelada>[] = [
    { titulo: 'Fecha', ver: (f) => new Date(f.fechaCancelacion).toLocaleString('es-DO', { dateStyle: 'short', timeStyle: 'short' }), csv: (f) => f.fechaCancelacion.slice(0, 16).replace('T', ' ') },
    { titulo: 'Cuenta', ver: (f) => f.factura, csv: (f) => String(f.factura) },
    { titulo: 'Hab.', ver: (f) => f.habitacion, csv: (f) => String(f.habitacion ?? '') },
    { titulo: 'Cliente', ver: (f) => f.cliente, csv: (f) => t(f.cliente) },
    m('Cargos', (f) => f.cargos),
    { titulo: 'Autorizó', ver: (f) => f.autoriza, csv: (f) => t(f.autoriza) },
    { titulo: 'Usuario', ver: (f) => f.usuario, csv: (f) => t(f.usuario) },
    { titulo: 'Motivo', ver: (f) => f.motivo, csv: (f) => t(f.motivo) },
  ]
  return (
    <>
      <Estado e={r.datos ? 'ok' : null} d={r} />
      {r.datos && <Tabla titulo="Facturas canceladas sin cobro" desde={desde} hasta={hasta} filas={filas} columnas={columnas} archivo="canceladas" />}
    </>
  )
}
