import { useState } from 'react'
import Clientes from './Clientes'
import Habitaciones from './Habitaciones'
import Precios from './Precios'
import Secuencias from './Secuencias'
import Servicios from './Servicios'
import Tipos from './Tipos'
import SinOpciones from './SinOpciones'
import { pestanasVisibles } from './api'

type Pestana = 'habitaciones' | 'tipos' | 'precios' | 'servicios' | 'clientes' | 'secuencias'

const NOMBRES: Record<Pestana, string> = {
  habitaciones: 'Habitaciones', tipos: 'Tipos', precios: 'Precios', servicios: 'Servicios', clientes: 'Clientes', secuencias: 'Secuencias e-CF',
}

function inicial(): Pestana {
  try {
    const v = localStorage.getItem('hotelweb.catalogos')
    if (v && v in NOMBRES) return v as Pestana
  } catch {
    // sin almacenamiento
  }
  return 'habitaciones'
}

/** Catalogos del hotel (etapa 2), en pestanas. */
export default function Catalogos({ alVencer }: { alVencer: () => void }) {
  const [guardada, setP] = useState<Pestana>(inicial)
  // solo las pestanas que permite la seguridad (p. ej. un recepcionista con solo Clientes)
  const visibles = pestanasVisibles('catalogos', Object.keys(NOMBRES) as Pestana[])
  const p = visibles.includes(guardada) ? guardada : visibles[0]

  function elegir(x: Pestana) {
    setP(x)
    try {
      localStorage.setItem('hotelweb.catalogos', x)
    } catch {
      // no importa
    }
  }

  if (!p) return <SinOpciones area="Catálogos" />

  return (
    <section className="catalogos">
      <h2>Catalogos</h2>
      <div className="pestanas" role="tablist">
        {visibles.map((x) => (
          <button key={x} type="button" role="tab" aria-selected={p === x} className={p === x ? 'activo' : ''}
                  onClick={() => elegir(x)}>{NOMBRES[x]}</button>
        ))}
      </div>
      {p === 'habitaciones' && <Habitaciones alVencer={alVencer} />}
      {p === 'tipos' && <Tipos alVencer={alVencer} />}
      {p === 'precios' && <Precios alVencer={alVencer} />}
      {p === 'servicios' && <Servicios alVencer={alVencer} />}
      {p === 'clientes' && <Clientes alVencer={alVencer} />}
      {p === 'secuencias' && <Secuencias alVencer={alVencer} />}
    </section>
  )
}
