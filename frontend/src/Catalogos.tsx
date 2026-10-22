import { useState } from 'react'
import Habitaciones from './Habitaciones'
import Precios from './Precios'
import Tipos from './Tipos'

type Pestana = 'habitaciones' | 'tipos' | 'precios'

const NOMBRES: Record<Pestana, string> = { habitaciones: 'Habitaciones', tipos: 'Tipos', precios: 'Precios' }

function inicial(): Pestana {
  try {
    const v = localStorage.getItem('hotelweb.catalogos')
    if (v === 'habitaciones' || v === 'tipos' || v === 'precios') return v
  } catch {
    // sin almacenamiento
  }
  return 'habitaciones'
}

/** Catalogos del hotel (etapa 2), en pestanas. */
export default function Catalogos({ alVencer }: { alVencer: () => void }) {
  const [p, setP] = useState<Pestana>(inicial)

  function elegir(x: Pestana) {
    setP(x)
    try {
      localStorage.setItem('hotelweb.catalogos', x)
    } catch {
      // no importa
    }
  }

  return (
    <section className="catalogos">
      <h2>Catalogos</h2>
      <div className="pestanas" role="tablist">
        {(Object.keys(NOMBRES) as Pestana[]).map((x) => (
          <button key={x} type="button" role="tab" aria-selected={p === x} className={p === x ? 'activo' : ''}
                  onClick={() => elegir(x)}>{NOMBRES[x]}</button>
        ))}
      </div>
      {p === 'habitaciones' && <Habitaciones alVencer={alVencer} />}
      {p === 'tipos' && <Tipos alVencer={alVencer} />}
      {p === 'precios' && <Precios alVencer={alVencer} />}
    </section>
  )
}
