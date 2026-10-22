import { useCallback, useState } from 'react'
import { salir, sesionGuardada, type Sesion } from './api'
import Ingreso from './Ingreso'
import Inicio from './Inicio'
import Rack from './Rack'

type Pagina = 'rack' | 'inicio'

export default function App() {
  const [sesion, setSesion] = useState<Sesion | null>(() => sesionGuardada())
  const [pagina, setPagina] = useState<Pagina>('rack')
  const cerrar = useCallback(() => {
    salir()
    setSesion(null)
  }, [])

  if (!sesion) return <Ingreso alIngresar={setSesion} />

  return (
    <div className="marco">
      <header className="barra">
        <span className="marca"><img src="/hotel.svg" alt="" width={24} height={24} /> Hotel</span>
        <nav className="menu">
          <button type="button" className={pagina === 'rack' ? 'activo' : ''} onClick={() => setPagina('rack')}>Habitaciones</button>
          <button type="button" className={pagina === 'inicio' ? 'activo' : ''} onClick={() => setPagina('inicio')}>Sistema</button>
        </nav>
        <span className="usuario">
          {sesion.usuario.nombre}
          <button type="button" className="secundario" onClick={cerrar}>Salir</button>
        </span>
      </header>
      {/* el rack usa todo el ancho de la pantalla; las demas paginas, una columna legible */}
      <main className={pagina === 'rack' ? 'contenido ancho' : 'contenido'}>
        {pagina === 'rack' ? <Rack alVencer={cerrar} /> : <Inicio alVencer={cerrar} />}
      </main>
    </div>
  )
}
