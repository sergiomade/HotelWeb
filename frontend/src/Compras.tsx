import { useState } from 'react'
import ComprasInformales from './ComprasInformales'
import Gastos from './Gastos'

type Pestana = 'gastos' | 'informales'
const NOMBRES: Record<Pestana, string> = { gastos: 'Gastos menores (E43)', informales: 'Compras a informales (E41)' }

/** Compras y gastos con e-CF (etapa 9d): gastos menores (E43) y compras a proveedores informales (E41). */
export default function Compras({ alVencer }: { alVencer: () => void }) {
  const [p, setP] = useState<Pestana>('gastos')
  return (
    <section className="catalogos">
      <h2>Compras y gastos</h2>
      <div className="pestanas" role="tablist">
        {(Object.keys(NOMBRES) as Pestana[]).map((x) => (
          <button key={x} type="button" role="tab" aria-selected={p === x} className={p === x ? 'activo' : ''} onClick={() => setP(x)}>{NOMBRES[x]}</button>
        ))}
      </div>
      {p === 'gastos' ? <Gastos alVencer={alVencer} /> : <ComprasInformales alVencer={alVencer} />}
    </section>
  )
}
