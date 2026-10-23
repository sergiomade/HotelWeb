import { useCallback, useState } from 'react'
import CargarNoche from './CargarNoche'
import Caja from './Caja'
import CheckIn from './CheckIn'
import Facturas from './Facturas'

type Pestana = 'checkin' | 'noche' | 'facturas' | 'caja'
const NOMBRES: Record<Pestana, string> = { checkin: 'Check-in', noche: 'Cargar noche', facturas: 'Facturas', caja: 'Caja' }

/** Recepcion: check-in de las llegadas, carga del hospedaje de la noche, facturas cerradas (e-CF) y el cuadre de caja. */
export default function Recepcion({ alVencer }: { alVencer: () => void }) {
  const [p, setP] = useState<Pestana>(() => {
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

  return (
    <section className="catalogos">
      <h2>Recepción</h2>
      <div className="pestanas" role="tablist">
        {(Object.keys(NOMBRES) as Pestana[]).map((x) => (
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
