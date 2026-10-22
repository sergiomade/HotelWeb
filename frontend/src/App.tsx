import { useCallback, useState } from 'react'
import { salir, sesionGuardada, type Sesion } from './api'
import Ingreso from './Ingreso'
import Inicio from './Inicio'

export default function App() {
  const [sesion, setSesion] = useState<Sesion | null>(() => sesionGuardada())
  const cerrar = useCallback(() => {
    salir()
    setSesion(null)
  }, [])

  if (!sesion) return <Ingreso alIngresar={setSesion} />

  return (
    <div className="marco">
      <header className="barra">
        <span className="marca"><img src="/hotel.svg" alt="" width={24} height={24} /> Hotel</span>
        <span className="usuario">
          {sesion.usuario.nombre}
          <button type="button" className="secundario" onClick={cerrar}>Salir</button>
        </span>
      </header>
      <main className="contenido">
        <Inicio alVencer={cerrar} />
      </main>
    </div>
  )
}
