import { useCallback, useState } from 'react'
import { salir, sesionGuardada, type Sesion } from './api'
import Ingreso from './Ingreso'
import Inicio from './Inicio'
import Precios from './Precios'
import Rack from './Rack'

type Pagina = 'rack' | 'precios' | 'inicio'

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
          <button type="button" className={pagina === 'precios' ? 'activo' : ''} onClick={() => setPagina('precios')}>Precios</button>
          <button type="button" className={pagina === 'inicio' ? 'activo' : ''} onClick={() => setPagina('inicio')}>Sistema</button>
        </nav>
        <span className="usuario">
          {sesion.usuario.nombre}
          <button type="button" className="secundario" onClick={cerrar}>Salir</button>
        </span>
      </header>
      {/* rack y precios usan todo el ancho de la pantalla; Sistema, una columna legible */}
      <main className={pagina === 'inicio' ? 'contenido' : 'contenido ancho'}>
        {pagina === 'rack' && <Rack alVencer={cerrar} />}
        {pagina === 'precios' && <Precios alVencer={cerrar} />}
        {pagina === 'inicio' && <Inicio alVencer={cerrar} />}
      </main>
    </div>
  )
}
