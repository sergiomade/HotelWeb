import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import SinOpciones from './SinOpciones'
import {
  pestanasVisibles, buscarReservas, dinero, fecha, hoyIso, obtenerRack, reporteCanceladas, reporteClientes, reporteComprobantes, reporteVentasCafeteria,
  reporteVentasHotel, SesionVencida,
  type ClienteReporte, type Comprobante, type FacturaCancelada, type HabitacionRack, type ReporteComprobantes, type ReservaResumen,
  type VentaCafeteria, type VentaHotel,
} from './api'

type Pestana = 'hotel' | 'cafeteria' | 'comprobantes' | 'canceladas' | 'ocupadas' | 'proyeccion' | 'clientes'
const NOMBRES: Record<Pestana, string> = {
  hotel: 'Ventas del hotel', cafeteria: 'Ventas de la cafetería', comprobantes: 'Comprobantes (NCF)', canceladas: 'Canceladas sin cobro',
  ocupadas: 'Habitaciones ocupadas', proyeccion: 'Proyección de reservas', clientes: 'Clientes',
}
// estos no van por rango de fechas
const SIN_FECHAS: Pestana[] = ['ocupadas', 'clientes']

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
  const [elegida, setP] = useState<Pestana>('hotel')
  const [desde, setDesde] = useState(() => hoyIso(-7))
  const [hasta, setHasta] = useState(() => hoyIso())
  // solo los reportes que permite la seguridad
  const visibles = pestanasVisibles('reportes', Object.keys(NOMBRES) as Pestana[])
  if (visibles.length === 0) return <SinOpciones area="Reportes" />
  const p = visibles.includes(elegida) ? elegida : visibles[0]

  return (
    <section className="catalogos reportes">
      <h2 className="no-imprimir">Reportes</h2>
      <div className="pestanas no-imprimir" role="tablist">
        {visibles.map((x) => (
          <button key={x} type="button" role="tab" aria-selected={p === x} className={p === x ? 'activo' : ''} onClick={() => setP(x)}>{NOMBRES[x]}</button>
        ))}
      </div>
      {!SIN_FECHAS.includes(p) && <div className="filtros no-imprimir">
        <label className="corto">Desde<input type="date" value={desde} onChange={(e) => setDesde(e.target.value)} /></label>
        <label className="corto">hasta<input type="date" value={hasta} onChange={(e) => setHasta(e.target.value)} /></label>
        <button type="button" className="secundario" onClick={() => setDesde(hasta)}>Solo un día</button>
      </div>}
      {p === 'hotel' && <VentasHotel desde={desde} hasta={hasta} alVencer={alVencer} />}
      {p === 'cafeteria' && <VentasCafeteria desde={desde} hasta={hasta} alVencer={alVencer} />}
      {p === 'comprobantes' && <Comprobantes desde={desde} hasta={hasta} alVencer={alVencer} />}
      {p === 'canceladas' && <Canceladas desde={desde} hasta={hasta} alVencer={alVencer} />}
      {p === 'ocupadas' && <Ocupadas alVencer={alVencer} />}
      {p === 'proyeccion' && <Proyeccion desde={desde} hasta={hasta} alVencer={alVencer} />}
      {p === 'clientes' && <Clientes alVencer={alVencer} />}
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

/**
 * Habitaciones ocupadas hoy, como Stp_ReportedeHabitacionesOcupadas: huesped, cliente, entrada, salida, personas y el
 * balance. El balance sale del Hospedaje (el calculo exacto de la cuenta; Delphi multiplicaba todo por 1.28).
 * Delphi preguntaba si incluir montos y nombres: aqui es una casilla.
 */
function Ocupadas({ alVencer }: { alVencer: () => void }) {
  const [conMontos, setConMontos] = useState(true)
  const cargar = useCallback(() => obtenerRack(), [])
  const hoy = hoyIso()
  const r = useReporte(cargar, hoy, hoy, alVencer)
  const filas = (r.datos?.habitaciones ?? []).filter((h) => h.situacion === 'Ocupada').sort((a, b) => a.numero - b.numero)
  const columnas: Columna<HabitacionRack>[] = [
    { titulo: 'Hab.', ver: (f) => f.numero, csv: (f) => String(f.numero) },
    { titulo: 'Tipo', ver: (f) => f.tipo, csv: (f) => t(f.tipo) },
    ...(conMontos ? [
      { titulo: 'Huésped', ver: (f: HabitacionRack) => f.huesped, csv: (f: HabitacionRack) => t(f.huesped) },
      { titulo: 'Cliente', ver: (f: HabitacionRack) => f.cliente, csv: (f: HabitacionRack) => t(f.cliente) },
    ] : []),
    { titulo: 'Entrada', ver: (f) => fecha(f.fechaEntrada, true), csv: (f) => t(f.fechaEntrada?.slice(0, 10)) },
    {
      titulo: 'Salida',
      ver: (f) => <span className={f.avisos.salidaVencida ? 'error' : f.avisos.saleHoy ? 'alerta' : ''}>{fecha(f.fechaSalida, true)}</span>,
      csv: (f) => t(f.fechaSalida?.slice(0, 10)),
    },
    { titulo: 'Personas', ver: (f) => f.personas, csv: (f) => String(f.personas ?? ''), num: true },
    { titulo: 'Crédito', ver: (f) => (f.credito ? 'Sí' : ''), csv: (f) => (f.credito ? 'Si' : '') },
    ...(conMontos ? [m<HabitacionRack>('Balance', (f) => f.saldo ?? 0)] : []),
  ]
  const salen = filas.filter((f) => f.avisos.saleHoy).length
  const vencidas = filas.filter((f) => f.avisos.salidaVencida).length
  return (
    <>
      <label className="check no-imprimir">
        <input type="checkbox" checked={conMontos} onChange={(e) => setConMontos(e.target.checked)} /> Con nombres y balances
      </label>
      <Estado e={r.datos ? 'ok' : null} d={r} />
      {r.datos && (
        <Tabla titulo="Habitaciones ocupadas" desde={hoy} hasta={hoy} filas={filas} columnas={columnas} archivo="ocupadas"
               pie={<p className="tenue">{filas.length} ocupadas · {salen} salen hoy{vencidas ? ` · ${vencidas} con la salida vencida` : ''}</p>} />
      )}
    </>
  )
}

const ESTADOS_RESERVA: [number, string][] = [[0, 'Todos'], [1, 'Activa'], [2, 'Cancelada'], [3, 'Hospedado']]   // EstadoReserva de la API

/** Proyeccion de reservaciones (Stp_ProyecciondeReservaciones): las que llegan, o las que salen, en el rango, con el resumen por dia. */
function Proyeccion({ desde, hasta, alVencer }: { desde: string; hasta: string; alVencer: () => void }) {
  const [porSalida, setPorSalida] = useState(false)
  const [estado, setEstado] = useState(0)
  const cargar = useCallback((d: string, h: string) => buscarReservas(d, h, porSalida, '', estado), [porSalida, estado])
  const r = useReporte(cargar, desde, hasta, alVencer)
  const filas = (r.datos ?? []) as ReservaResumen[]
  const dia = (f: ReservaResumen) => (porSalida ? f.salida : f.entrada)
  const otro = (f: ReservaResumen) => (porSalida ? f.entrada : f.salida)
  const porDia = useMemo(() => {
    const dias = new Map<string, { reservas: number; habitaciones: number }>()
    for (const f of filas) {
      const k = (porSalida ? f.salida : f.entrada).slice(0, 10)
      const x = dias.get(k) ?? { reservas: 0, habitaciones: 0 }
      x.reservas++
      x.habitaciones += f.cantidadHabitaciones
      dias.set(k, x)
    }
    return [...dias.entries()].sort(([a], [b]) => a.localeCompare(b))
  }, [filas, porSalida])
  const columnas: Columna<ReservaResumen>[] = [
    { titulo: porSalida ? 'Salida' : 'Llegada', ver: (f) => fecha(dia(f), true), csv: (f) => dia(f).slice(0, 10) },
    { titulo: 'Reserva', ver: (f) => f.id, csv: (f) => String(f.id) },
    {
      titulo: 'Cliente',
      ver: (f) => <>{f.cliente}{f.huesped && f.huesped !== f.cliente && <span className="tenue fila-sub">{f.huesped}</span>}</>,
      csv: (f) => f.cliente,
    },
    { titulo: 'Habitaciones', ver: (f) => f.habitaciones, csv: (f) => t(f.habitaciones) },
    { titulo: 'Cant.', ver: (f) => f.cantidadHabitaciones, csv: (f) => String(f.cantidadHabitaciones), num: true },
    { titulo: porSalida ? 'Llegada' : 'Salida', ver: (f) => fecha(otro(f), true), csv: (f) => otro(f).slice(0, 10) },
    { titulo: 'Noches', ver: (f) => f.noches, csv: (f) => String(f.noches), num: true },
    { titulo: 'Teléfono', ver: (f) => f.telefono, csv: (f) => t(f.telefono) },
    { titulo: 'Estado', ver: (f) => f.estadoNombre, csv: (f) => f.estadoNombre },
    m('Adelantos', (f) => f.adelantos),
  ]
  const resumen = porDia.length > 0 && (
    <table className="tabla reporte-resumen">
      <thead><tr><th>Día</th><th className="num">Reservas</th><th className="num">Habitaciones</th></tr></thead>
      <tbody>
        {porDia.map(([d, x]) => <tr key={d}><td>{fecha(d, true)}</td><td className="num">{x.reservas}</td><td className="num">{x.habitaciones}</td></tr>)}
      </tbody>
    </table>
  )
  return (
    <>
      <div className="filtros no-imprimir">
        <div className="segmentado" role="group" aria-label="Por">
          <button type="button" className={!porSalida ? 'activo' : ''} onClick={() => setPorSalida(false)}>Por llegada</button>
          <button type="button" className={porSalida ? 'activo' : ''} onClick={() => setPorSalida(true)}>Por salida</button>
        </div>
        <label className="corto">Estado
          <select value={estado} onChange={(e) => setEstado(Number(e.target.value))}>
            {ESTADOS_RESERVA.map(([id, n]) => <option key={id} value={id}>{n}</option>)}
          </select>
        </label>
      </div>
      <Estado e={r.datos ? 'ok' : null} d={r} />
      {r.datos && (
        <Tabla titulo={porSalida ? 'Salidas programadas' : 'Reservaciones por llegada'} desde={desde} hasta={hasta} filas={filas}
               columnas={columnas} archivo={porSalida ? 'salidas' : 'llegadas'} pie={resumen} />
      )}
    </>
  )
}

const COMPROBANTE: Record<number, string> = { 1: 'E31', 2: 'E32', 13: 'E44', 14: 'E45' }

/** El maestro de clientes (CxcCliente): por nombre, RNC o numero, solo con RNC o con credito. */
function Clientes({ alVencer }: { alVencer: () => void }) {
  const [texto, setTexto] = useState('')
  const [conRnc, setConRnc] = useState(false)
  const [conCredito, setConCredito] = useState(false)
  const [inactivos, setInactivos] = useState(false)
  const [buscar, setBuscar] = useState('')
  useEffect(() => {
    const h = setTimeout(() => setBuscar(texto.trim()), 400)
    return () => clearTimeout(h)
  }, [texto])
  const cargar = useCallback(() => reporteClientes(buscar, conRnc, conCredito, inactivos), [buscar, conRnc, conCredito, inactivos])
  const hoy = hoyIso()
  const r = useReporte(cargar, hoy, hoy, alVencer)
  const filas = (r.datos ?? []) as ClienteReporte[]
  const sn = (v: boolean) => (v ? 'Sí' : 'No')
  const columnas: Columna<ClienteReporte>[] = [
    { titulo: 'Número', ver: (f) => f.numero, csv: (f) => String(f.numero) },
    { titulo: 'Nombre', ver: (f) => f.nombre, csv: (f) => f.nombre },
    { titulo: 'RNC / cédula', ver: (f) => f.identificacion, csv: (f) => t(f.identificacion) },
    { titulo: 'Teléfono', ver: (f) => f.telefono, csv: (f) => t(f.telefono) },
    { titulo: 'Contacto', ver: (f) => f.contacto, csv: (f) => t(f.contacto) },
    { titulo: 'Correo', ver: (f) => f.correo, csv: (f) => t(f.correo) },
    { titulo: 'Dirección', ver: (f) => f.direccion, csv: (f) => t(f.direccion) },
    { titulo: 'e-CF', ver: (f) => COMPROBANTE[f.comprobante ?? 0] ?? '', csv: (f) => COMPROBANTE[f.comprobante ?? 0] ?? '' },
    { titulo: 'ITBIS / propina', ver: (f) => `${sn(f.itbis)} / ${sn(f.propina)}`, csv: (f) => `${sn(f.itbis)} / ${sn(f.propina)}` },
    m('Límite de crédito', (f) => f.limiteCredito, false),
    { titulo: 'Estado', ver: (f) => f.estatus, csv: (f) => t(f.estatus) },
  ]
  return (
    <>
      <div className="filtros no-imprimir">
        <label className="largo">Buscar<input value={texto} onChange={(e) => setTexto(e.target.value)} placeholder="Nombre, RNC, cédula o número" /></label>
        <label className="check"><input type="checkbox" checked={conRnc} onChange={(e) => setConRnc(e.target.checked)} /> Solo con RNC</label>
        <label className="check"><input type="checkbox" checked={conCredito} onChange={(e) => setConCredito(e.target.checked)} /> Solo con crédito</label>
        <label className="check"><input type="checkbox" checked={inactivos} onChange={(e) => setInactivos(e.target.checked)} /> Con los inactivos</label>
      </div>
      <Estado e={r.datos ? 'ok' : null} d={r} />
      {r.datos && (
        <Tabla titulo="Maestro de clientes" desde={hoy} hasta={hoy} filas={filas} columnas={columnas} archivo="clientes"
               pie={filas.length >= 2000 && <p className="alerta no-imprimir">Se muestran los primeros 2,000: use los filtros para ver el resto.</p>} />
      )}
    </>
  )
}
