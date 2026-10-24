import { useCallback, useEffect, useState } from 'react'
import { avisosSecuencias, obtenerEmpresa, refrescarUsuario, salir, sesionGuardada, SesionVencida, type SecuenciaEcf, type Sesion } from './api'
import Ingreso from './Ingreso'
import Tablero from './Tablero'
import Catalogos from './Catalogos'
import Rack from './Rack'
import Recepcion from './Recepcion'
import Reservas from './Reservas'
import WalkIn from './WalkIn'
import Cafeteria from './Cafeteria'
import Reportes from './Reportes'
import Compras from './Compras'
import Seguridad from './Seguridad'
import MiClave from './MiClave'

type Pagina = 'rack' | 'recepcion' | 'walkin' | 'cafeteria' | 'compras' | 'reportes' | 'reservas' | 'catalogos' | 'inicio' | 'seguridad'

// el menu; permiso = la clave de PermisosHotel que hace falta para verlo (sin permiso: lo ven todos)
const MENU: { pagina: Pagina; texto: string; permiso?: string }[] = [
  { pagina: 'rack', texto: 'Hospedaje', permiso: 'hospedaje' },
  { pagina: 'reservas', texto: 'Reservas', permiso: 'reservas' },
  { pagina: 'recepcion', texto: 'Recepción', permiso: 'recepcion' },
  { pagina: 'walkin', texto: 'Walk-in', permiso: 'walkin' },
  { pagina: 'cafeteria', texto: 'Cafetería', permiso: 'cafeteria' },
  { pagina: 'compras', texto: 'Compras', permiso: 'compras' },
  { pagina: 'reportes', texto: 'Reportes', permiso: 'reportes' },
  { pagina: 'catalogos', texto: 'Catalogos', permiso: 'catalogos' },
  { pagina: 'inicio', texto: 'Tablero' },
  { pagina: 'seguridad', texto: 'Seguridad', permiso: 'seguridad' },
]

/** Una sesion guardada de antes (sin la lista de permisos) ve las areas como antes, pero no Seguridad. */
function puedeVer(sesion: Sesion | null, permiso?: string) {
  if (!permiso) return true
  const p = sesion?.usuario.permisos
  return p ? p.includes(permiso) : permiso !== 'seguridad'
}

// fotos del hotel (carpeta Images) como franja arriba de las paginas de atencion; las de trabajo (compras, reportes,
// catalogos, sistema) van sin foto. pos = que parte de la foto se ve en la franja
const FOTOS: Partial<Record<Pagina, { src: string; alt: string; pos: string }>> = {
  rack: { src: '/seccion-hospedaje.jpg', alt: 'Una habitación del hotel', pos: 'center 62%' },
  reservas: { src: '/fondo-ingreso.jpg', alt: 'La entrada del Hotel Las Caobas', pos: 'center 45%' },
  recepcion: { src: '/seccion-recepcion.jpg', alt: 'El lobby y la recepción del hotel', pos: 'center 55%' },
  walkin: { src: '/seccion-walkin.jpg', alt: 'La piscina del hotel', pos: 'center 60%' },
  cafeteria: { src: '/seccion-cafeteria.jpg', alt: 'La cafetería del hotel', pos: 'center 60%' },
  inicio: { src: '/seccion-tablero.jpg', alt: 'Una habitación matrimonial del hotel', pos: 'center 55%' },
}

export default function App() {
  const [sesion, setSesion] = useState<Sesion | null>(() => sesionGuardada())
  // se entra al tablero del dia (decision del usuario 2026-10-23)
  const [pagina, setPagina] = useState<Pagina>('inicio')
  const [empresa, setEmpresa] = useState('')
  const [avisos, setAvisos] = useState<SecuenciaEcf[]>([])
  const [vistaCatalogos, setVistaCatalogos] = useState(0)
  const [miClave, setMiClave] = useState(false)

  // los permisos al dia al entrar y al recargar la pagina (un administrador pudo cambiarlos)
  const token = sesion?.token
  const actualizar = useCallback(() => {
    refrescarUsuario()
      .then((s) => s && setSesion(s))
      .catch((e) => { if (e instanceof SesionVencida) setSesion(null) })
  }, [])
  useEffect(() => {
    if (token) actualizar()
  }, [token, actualizar])

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
    if (puedeVer(sesion, 'catalogos')) setPagina('catalogos')
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

  const menu = MENU.filter((m) => puedeVer(sesion, m.permiso))
  // si perdio el permiso de la pagina abierta, va al tablero
  const actual: Pagina = menu.some((m) => m.pagina === pagina) ? pagina : 'inicio'

  return (
    <div className="marco">
      <header className="barra">
        <span className="marca"><img src="/hotel.svg" alt="" width={24} height={24} /> {empresa || 'Hotel'}</span>
        <nav className="menu">
          {menu.map((m) => (
            <button key={m.pagina} type="button" className={actual === m.pagina ? 'activo' : ''} onClick={() => setPagina(m.pagina)}>{m.texto}</button>
          ))}
        </nav>
        <span className="usuario">
          {sesion.usuario.nombre}
          <button type="button" className="secundario" onClick={() => setMiClave(true)} title="Cambiar mi clave">Mi clave</button>
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
      <main className="contenido ancho">
        {FOTOS[actual] && <div className="franja-seccion no-imprimir" style={{ backgroundImage: `url('${FOTOS[actual]!.src}')`, backgroundPosition: FOTOS[actual]!.pos }}
                               role="img" aria-label={FOTOS[actual]!.alt} />}
        {actual === 'rack' && <Rack alVencer={cerrar} />}
        {actual === 'recepcion' && <Recepcion alVencer={cerrar} />}
        {actual === 'walkin' && <WalkIn alVencer={cerrar} />}
        {actual === 'cafeteria' && <Cafeteria alVencer={cerrar} />}
        {actual === 'compras' && <Compras alVencer={cerrar} />}
        {actual === 'reportes' && <Reportes alVencer={cerrar} />}
        {actual === 'reservas' && <Reservas alVencer={cerrar} />}
        {actual === 'catalogos' && <Catalogos key={vistaCatalogos} alVencer={cerrar} />}
        {actual === 'inicio' && <Tablero alVencer={cerrar} />}
        {actual === 'seguridad' && <Seguridad alVencer={cerrar} miId={sesion.usuario.id} alCambiar={actualizar} />}
      </main>
      {miClave && <MiClave alCerrar={() => setMiClave(false)} alVencer={cerrar} />}
    </div>
  )
}
