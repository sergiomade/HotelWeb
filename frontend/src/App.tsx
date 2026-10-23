import { useCallback, useEffect, useState } from 'react'
import { avisosSecuencias, obtenerEmpresa, salir, sesionGuardada, type SecuenciaEcf, type Sesion } from './api'
import Ingreso from './Ingreso'
import Inicio from './Inicio'
import Catalogos from './Catalogos'
import Rack from './Rack'
import Recepcion from './Recepcion'
import Reservas from './Reservas'
import WalkIn from './WalkIn'
import Cafeteria from './Cafeteria'
import Reportes from './Reportes'

type Pagina = 'rack' | 'recepcion' | 'walkin' | 'cafeteria' | 'reportes' | 'reservas' | 'catalogos' | 'inicio'

export default function App() {
  const [sesion, setSesion] = useState<Sesion | null>(() => sesionGuardada())
  const [pagina, setPagina] = useState<Pagina>('rack')
  const [empresa, setEmpresa] = useState('')
  const [avisos, setAvisos] = useState<SecuenciaEcf[]>([])
  const [vistaCatalogos, setVistaCatalogos] = useState(0)

  // las secuencias e-CF por agotarse, por vencer o sin autorizacion: un aviso por sesion (como MostrarAvisosSecuenciasEcf)
  useEffect(() => {
    if (!sesion) return
    try {
      if (sessionStorage.getItem('hotelweb.avisos-secuencias') === sesion.token) return
    } catch {
      // sin almacenamiento: se avisa cada vez
    }
    avisosSecuencias().then(setAvisos).catch(() => setAvisos([]))
  }, [sesion])

  function cerrarAvisos() {
    setAvisos([])
    try {
      if (sesion) sessionStorage.setItem('hotelweb.avisos-secuencias', sesion.token)
    } catch {
      // no importa
    }
  }

  function verSecuencias() {
    try {
      localStorage.setItem('hotelweb.catalogos', 'secuencias')
    } catch {
      // no importa
    }
    setVistaCatalogos((n) => n + 1)
    setPagina('catalogos')
    cerrarAvisos()
  }

  // el nombre del hotel (Parametro.NombreEmpresa) en la barra, el ingreso y el titulo de la pestana
  useEffect(() => {
    obtenerEmpresa().then((e) => {
      setEmpresa(e.nombre)
      document.title = e.nombre
    })
  }, [])
  const cerrar = useCallback(() => {
    salir()
    setSesion(null)
  }, [])

  if (!sesion) return <Ingreso alIngresar={setSesion} empresa={empresa} />

  return (
    <div className="marco">
      <header className="barra">
        <span className="marca"><img src="/hotel.svg" alt="" width={24} height={24} /> {empresa || 'Hotel'}</span>
        <nav className="menu">
          <button type="button" className={pagina === 'rack' ? 'activo' : ''} onClick={() => setPagina('rack')}>Hospedaje</button>
          <button type="button" className={pagina === 'reservas' ? 'activo' : ''} onClick={() => setPagina('reservas')}>Reservas</button>
          <button type="button" className={pagina === 'recepcion' ? 'activo' : ''} onClick={() => setPagina('recepcion')}>Recepción</button>
          <button type="button" className={pagina === 'walkin' ? 'activo' : ''} onClick={() => setPagina('walkin')}>Walk-in</button>
          <button type="button" className={pagina === 'cafeteria' ? 'activo' : ''} onClick={() => setPagina('cafeteria')}>Cafetería</button>
          <button type="button" className={pagina === 'reportes' ? 'activo' : ''} onClick={() => setPagina('reportes')}>Reportes</button>
          <button type="button" className={pagina === 'catalogos' ? 'activo' : ''} onClick={() => setPagina('catalogos')}>Catalogos</button>
          <button type="button" className={pagina === 'inicio' ? 'activo' : ''} onClick={() => setPagina('inicio')}>Sistema</button>
        </nav>
        <span className="usuario">
          {sesion.usuario.nombre}
          <button type="button" className="secundario" onClick={cerrar}>Salir</button>
        </span>
      </header>
      {avisos.length > 0 && (
        <div className="aviso-secuencias" role="alert">
          <span><strong>Secuencias e-CF:</strong>{' '}
            {avisos.map((a) => `${a.codigo} ${a.estado.toLowerCase()}${a.estado === 'POR AGOTARSE' ? ` (quedan ${a.restantes})` : a.estado === 'POR VENCER' ? ` (vence en ${a.dias} días)` : ''}`).join(' · ')}
          </span>
          <span className="botones">
            <button type="button" className="secundario chico" onClick={verSecuencias}>Ver</button>
            <button type="button" className="secundario chico" onClick={cerrarAvisos} aria-label="Cerrar el aviso">✕</button>
          </span>
        </div>
      )}
      {/* rack y catalogos usan todo el ancho de la pantalla; Sistema, una columna legible */}
      <main className={pagina === 'inicio' ? 'contenido' : 'contenido ancho'}>
        {pagina === 'rack' && <Rack alVencer={cerrar} />}
        {pagina === 'recepcion' && <Recepcion alVencer={cerrar} />}
        {pagina === 'walkin' && <WalkIn alVencer={cerrar} />}
        {pagina === 'cafeteria' && <Cafeteria alVencer={cerrar} />}
        {pagina === 'reportes' && <Reportes alVencer={cerrar} />}
        {pagina === 'reservas' && <Reservas alVencer={cerrar} />}
        {pagina === 'catalogos' && <Catalogos key={vistaCatalogos} alVencer={cerrar} />}
        {pagina === 'inicio' && <Inicio alVencer={cerrar} />}
      </main>
    </div>
  )
}
