import { useCallback, useState } from 'react'
import CargarNoche from './CargarNoche'
import Caja from './Caja'
import CheckIn from './CheckIn'
import Facturas from './Facturas'
import SinOpciones from './SinOpciones'
import { pestanasVisibles } from './api'

type Pestana = 'checkin' | 'noche' | 'facturas' | 'caja'
const NOMBRES: Record<Pestana, string> = { checkin: 'Check-in', noche: 'Cargar noche', facturas: 'Facturas', caja: 'Caja' }

/** Recepcion: check-in de las llegadas, carga del hospedaje de la noche, facturas cerradas (e-CF) y el cuadre de caja. */
export default function Recepcion({ alVencer }: { alVencer: () => void }) {
  const [guardada, setP] = useState<Pestana>(() => {
    try {
      const v = localStorage.getItem('hotelweb.recepcion')
      return v === 'noche' || v === 'facturas' || v === 'caja' ? v : 'checkin'
    } catch {
      return 'checkin'
    }
  })
  const elegir = useCallback((x: Pestana) => {
    setP(x)
    try {
      localStorage.setItem('hotelweb.recepcion', x)
    } catch {
      // no importa
    }
  }, [])

  // solo las pestanas que permite la seguridad; si la guardada no se puede, la primera que si
  const visibles = pestanasVisibles('recepcion', Object.keys(NOMBRES) as Pestana[])
  if (visibles.length === 0) return <SinOpciones area="Recepción" />
  const p = visibles.includes(guardada) ? guardada : visibles[0]

  return (
    <section className="catalogos">
      <h2>Recepción</h2>
      <div className="pestanas" role="tablist">
        {visibles.map((x) => (
          <button key={x} type="button" role="tab" aria-selected={p === x} className={p === x ? 'activo' : ''}
                  onClick={() => elegir(x)}>{NOMBRES[x]}</button>
        ))}
      </div>
      {p === 'checkin' && <CheckIn alVencer={alVencer} />}
      {p === 'noche' && <CargarNoche alVencer={alVencer} />}
      {p === 'facturas' && <Facturas alVencer={alVencer} />}
      {p === 'caja' && <Caja ambito="hotel" alVencer={alVencer} />}
    </section>
  )
}
