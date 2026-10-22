import { useCallback, useEffect, useState } from 'react'
import { buscarReservas, dinero, fecha, hoyIso, SesionVencida, type ReservaResumen } from './api'
import ReservaEditor from './ReservaEditor'

const ESTADOS = [{ id: 0, nombre: 'Todas' }, { id: 1, nombre: 'Activas' }, { id: 3, nombre: 'Hospedados' }, { id: 2, nombre: 'Canceladas' }]

type Abierta = { nueva: true } | { nueva: false; id: number }

/** Reservaciones (etapa 3): buscar por entrada o salida (la proyeccion de Delphi), crear, modificar y cancelar. */
export default function Reservas({ alVencer }: { alVencer: () => void }) {
  const [desde, setDesde] = useState(hoyIso())
  const [hasta, setHasta] = useState(hoyIso(30))
  const [porSalida, setPorSalida] = useState(false)
  const [texto, setTexto] = useState('')
  const [estado, setEstado] = useState(1)
  const [lista, setLista] = useState<ReservaResumen[] | null>(null)
  const [error, setError] = useState('')
  const [abierta, setAbierta] = useState<Abierta | null>(null)
  const [aviso, setAviso] = useState('')

  const manejar = useCallback(
    (e: unknown) => (e instanceof SesionVencida ? alVencer() : setError(e instanceof Error ? e.message : String(e))),
    [alVencer],
  )

  const cargar = useCallback(() => {
    if (!desde || !hasta) return
    buscarReservas(desde, hasta, porSalida, texto, estado)
      .then((r) => {
        setLista(r)
        setError('')
      })
      .catch(manejar)
  }, [desde, hasta, porSalida, texto, estado, manejar])

  useEffect(() => {
    const t = setTimeout(cargar, 300)
    return () => clearTimeout(t)
  }, [cargar])

  const totalHab = lista?.reduce((s, r) => s + (r.estado === 2 ? 0 : r.cantidadHabitaciones), 0) ?? 0

  return (
    <section className="reservas">
      <div className="rack-cabecera">
        <h2>Reservaciones</h2>
        <button type="button" onClick={() => setAbierta({ nueva: true })}>Nueva reserva</button>
      </div>

      <div className="filtros">
        <label className="corto">
          {porSalida ? 'Salen desde' : 'Entran desde'}
          <input type="date" value={desde} onChange={(e) => setDesde(e.target.value)} />
        </label>
        <label className="corto">
          hasta
          <input type="date" value={hasta} onChange={(e) => setHasta(e.target.value)} />
        </label>
        <label className="corto">
          Por
          <select value={porSalida ? 's' : 'e'} onChange={(e) => setPorSalida(e.target.value === 's')}>
            <option value="e">fecha de entrada</option>
            <option value="s">fecha de salida</option>
          </select>
        </label>
        <label className="corto">
          Estado
          <select value={estado} onChange={(e) => setEstado(Number(e.target.value))}>
            {ESTADOS.map((s) => <option key={s.id} value={s.id}>{s.nombre}</option>)}
          </select>
        </label>
        <label className="largo">
          Buscar
          <input placeholder="Cliente, huésped, contacto o número" value={texto} onChange={(e) => setTexto(e.target.value)} />
        </label>
      </div>
      {aviso && <p className="ok-texto" role="status">{aviso}</p>}
      {error && <p className="error">{error}</p>}
      {lista && <p className="tenue">{lista.length} reservas{lista.length === 300 ? ' (se muestran 300: acorte las fechas)' : ''} · {totalHab} habitaciones sin contar canceladas</p>}

      <div className="tabla-marco">
        <table className="tabla catalogo">
          <thead>
            <tr>
              <th className="num">No.</th><th>Entrada</th><th>Salida</th><th className="num">Noches</th><th>Cliente</th>
              <th>Huésped</th><th>Habitaciones</th><th className="num">Adelantos</th><th>Estado</th>
            </tr>
          </thead>
          <tbody>
            {lista?.map((r) => (
              <tr key={r.id} className={`fila-editable${r.estado === 2 ? ' apagada' : ''}`} tabIndex={0}
                  onClick={() => setAbierta({ nueva: false, id: r.id })}
                  onKeyDown={(e) => e.key === 'Enter' && setAbierta({ nueva: false, id: r.id })}>
                <td className="num">{r.id}</td>
                <td>{fecha(r.entrada, true)}</td>
                <td>{fecha(r.salida, true)}</td>
                <td className="num">{r.noches}</td>
                <td><strong>{r.cliente}</strong>{r.telefono && <span className="tenue fila-sub">{r.telefono}</span>}</td>
                <td>{r.huesped}</td>
                <td>{r.habitaciones}</td>
                <td className="num">{r.adelantos ? dinero(r.adelantos) : ''}</td>
                <td><span className={`estado e${r.estado}`}>{r.estadoNombre}</span></td>
              </tr>
            ))}
            {lista?.length === 0 && <tr><td colSpan={9} className="tenue">Ninguna reserva con esos filtros.</td></tr>}
          </tbody>
        </table>
      </div>

      {abierta && (
        <ReservaEditor id={abierta.nueva ? null : abierta.id} alVencer={alVencer}
                       alCerrar={(mensaje) => { setAbierta(null); if (mensaje) setAviso(mensaje); cargar() }} />
      )}
    </section>
  )
}
