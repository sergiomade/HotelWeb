import { useEffect, useState } from 'react'
import { obtenerDiagnostico, SesionVencida, type Diagnostico, type EstadoBase } from './api'

/** Pantalla inicial de la etapa 0: datos del hotel y estado de la conexion a las dos bases. */
export default function Inicio({ alVencer }: { alVencer: () => void }) {
  const [d, setD] = useState<Diagnostico | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    obtenerDiagnostico()
      .then(setD)
      .catch((e) => (e instanceof SesionVencida ? alVencer() : setError(e instanceof Error ? e.message : String(e))))
  }, [alVencer])

  if (error) return <p className="error">{error}</p>
  if (!d) return <p className="tenue">Cargando...</p>

  return (
    <section className="inicio">
      <h2>{d.empresa || 'Hotel'}</h2>
      <p className="tenue">RNC {d.rnc} · version {d.version}</p>

      <div className="cifras">
        <div><strong>{d.habitaciones}</strong><span>habitaciones</span></div>
        <div><strong>{d.ocupadas}</strong><span>ocupadas ahora</span></div>
      </div>

      <h3>Conexion</h3>
      <ul className="bases">
        <Base titulo="Base del hotel" b={d.hotel} />
        <Base titulo="Usuarios" b={d.seguridad} />
      </ul>
      <p className="tenue">Proximo paso: el rack de habitaciones.</p>
    </section>
  )
}

function Base({ titulo, b }: { titulo: string; b: EstadoBase }) {
  return (
    <li className={b.conecta ? 'ok' : 'mal'}>
      <span>{b.conecta ? 'Conectada' : 'Sin conexion'}</span> {titulo}: {b.base} en {b.servidor}
      {b.error && <div className="error">{b.error}</div>}
    </li>
  )
}
