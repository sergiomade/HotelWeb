import { useCallback, useState } from 'react'
import { salir, sesionGuardada, type Sesion } from './api'
import Ingreso from './Ingreso'
import Inicio from './Inicio'
import Catalogos from './Catalogos'
import Rack from './Rack'
import Recepcion from './Recepcion'
import Reservas from './Reservas'

type Pagina = 'rack' | 'recepcion' | 'reservas' | 'catalogos' | 'inicio'

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
          <button type="button" className={pagina === 'rack' ? 'activo' : ''} onClick={() => setPagina('rack')}>Rack</button>
          <button type="button" className={pagina === 'recepcion' ? 'activo' : ''} onClick={() => setPagina('recepcion')}>Recepción</button>
          <button type="button" className={pagina === 'reservas' ? 'activo' : ''} onClick={() => setPagina('reservas')}>Reservas</button>
          <button type="button" className={pagina === 'catalogos' ? 'activo' : ''} onClick={() => setPagina('catalogos')}>Catalogos</button>
          <button type="button" className={pagina === 'inicio' ? 'activo' : ''} onClick={() => setPagina('inicio')}>Sistema</button>
        </nav>
        <span className="usuario">
          {sesion.usuario.nombre}
          <button type="button" className="secundario" onClick={cerrar}>Salir</button>
        </span>
      </header>
      {/* rack y catalogos usan todo el ancho de la pantalla; Sistema, una columna legible */}
      <main className={pagina === 'inicio' ? 'contenido' : 'contenido ancho'}>
        {pagina === 'rack' && <Rack alVencer={cerrar} />}
        {pagina === 'recepcion' && <Recepcion alVencer={cerrar} />}
        {pagina === 'reservas' && <Reservas alVencer={cerrar} />}
        {pagina === 'catalogos' && <Catalogos alVencer={cerrar} />}
        {pagina === 'inicio' && <Inicio alVencer={cerrar} />}
      </main>
    </div>
  )
}
