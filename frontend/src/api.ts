// Llamadas a la API. La sesion (token) se guarda en el navegador; si vence, se vuelve al ingreso.

export interface Usuario {
  id: number
  login: string
  nombre: string
  supervisor: boolean
}

export interface Sesion {
  token: string
  vence: string
  usuario: Usuario
}

export interface EstadoBase {
  servidor: string
  base: string
  conecta: boolean
  error: string | null
}

export interface Diagnostico {
  version: string
  empresa: string
  rnc: string
  habitaciones: number
  ocupadas: number
  hotel: EstadoBase
  seguridad: EstadoBase
}

const CLAVE_SESION = 'hotelweb.sesion'

export function sesionGuardada(): Sesion | null {
  try {
    const s = JSON.parse(localStorage.getItem(CLAVE_SESION) ?? 'null') as Sesion | null
    return s && new Date(s.vence) > new Date() ? s : null
  } catch {
    return null
  }
}

function guardar(s: Sesion | null) {
  try {
    if (s) localStorage.setItem(CLAVE_SESION, JSON.stringify(s))
    else localStorage.removeItem(CLAVE_SESION)
  } catch {
    // navegador sin almacenamiento: la sesion dura mientras la pagina este abierta
  }
}

export class SesionVencida extends Error {}

async function pedir<T>(ruta: string, opciones: RequestInit = {}): Promise<T> {
  const s = sesionGuardada()
  const r = await fetch(ruta, {
    ...opciones,
    headers: {
      'Content-Type': 'application/json',
      ...(s ? { Authorization: `Bearer ${s.token}` } : {}),
      ...opciones.headers,
    },
  })
  if (r.status === 401 && s) {
    guardar(null)
    throw new SesionVencida('La sesion vencio: ingrese de nuevo.')
  }
  if (!r.ok) {
    const texto = await r.text()
    throw new Error(texto || `Error ${r.status}`)
  }
  return (await r.json()) as T
}

export async function ingresar(usuario: string, clave: string): Promise<Sesion> {
  const r = await fetch('/api/auth/ingresar', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ usuario, clave }),
  })
  if (!r.ok) throw new Error((await r.text()) || 'No se pudo ingresar.')
  const s = (await r.json()) as Sesion
  guardar(s)
  return s
}

export function salir() {
  guardar(null)
}

export const obtenerDiagnostico = () => pedir<Diagnostico>('/api/diagnostico')

// ---- rack de habitaciones ----

export type Situacion = 'Libre' | 'Ocupada' | 'LlegaHoy' | 'Bloqueada'

export interface HabitacionRack {
  numero: number
  piso: number
  x: number
  y: number
  tipo: string | null
  estado: string | null
  comentario: string | null
  situacion: Situacion
  avisos: { saleHoy: boolean; salidaVencida: boolean; ocupadaEstandoBloqueada: boolean }
  factura: number | null
  huesped: string | null
  cliente: string | null
  fechaEntrada: string | null
  fechaSalida: string | null
  personas: number | null
  credito: boolean
  saldo: number | null
  transferida: boolean
  llegada: { reserva: number; huesped: string | null; fechaSalida: string | null } | null
}

export interface RackHoy {
  hoy: string
  habitaciones: HabitacionRack[]
}

export interface LineaCuenta {
  secuencia: number
  descripcion: string
  cantidad: number
  precio: number
  importe: number
  activo: boolean
  usuario: string | null
  fecha: string | null
}

export interface CuentaHabitacion {
  factura: number
  habitacion: number
  huesped: string | null
  clienteId: number | null
  cliente: string | null
  rnc: string | null
  llevaItbis: boolean
  llevaPropina: boolean
  regimenEspecial: boolean
  reserva: number | null
  fechaEntrada: string | null
  fechaSalida: string | null
  categoria: string | null
  transferida: boolean
  saldo: number
  desglose: { cargos: number; itbis: number; propina: number; pagosYPayOut: number; saldo: number }
  lineas: LineaCuenta[]
}

export const obtenerRack = () => pedir<RackHoy>('/api/rack')
export const obtenerCuenta = (habitacion: number) => pedir<CuentaHabitacion>(`/api/rack/${habitacion}/cuenta`)

// ---- catalogos: precios ----

export interface TipoHabitacion {
  id: number
  descripcion: string
  siglas: string | null
  minimo: number
  maximo: number
  habitaciones: number
}

export interface CategoriaPrecio {
  id: string
  descripcion: string
}

export interface Precio {
  tipo: number
  categoria: string
  sinImpuestos: number
  conImpuestos: number
  personaAdicional: number
  usuario: string | null
  fecha: string | null
}

export interface PantallaPrecios {
  tabla: { tipos: TipoHabitacion[]; categorias: CategoriaPrecio[]; precios: Precio[] }
  puedeEditar: boolean
}

export const obtenerPrecios = () => pedir<PantallaPrecios>('/api/catalogos/precios')
export const guardarPrecio = (tipo: number, categoria: string, conImpuestos: number, personaAdicional: number) =>
  pedir<Precio>(`/api/catalogos/precios/${tipo}/${encodeURIComponent(categoria)}`, {
    method: 'PUT',
    body: JSON.stringify({ conImpuestos, personaAdicional }),
  })

// ---- catalogos: habitaciones ----

export interface Habitacion {
  numero: number
  tipo: number
  estado: number
  comentario: string | null
  extension: string | null
  television: boolean
  aireAcondicionado: boolean
  cabana: boolean
  x: number
  y: number
  ocupada: boolean
  usuario: string | null
  fecha: string | null
}

export interface EstadoHabitacion {
  id: number
  descripcion: string
  disponible: boolean
}

export interface PantallaHabitaciones {
  catalogo: { habitaciones: Habitacion[]; tipos: TipoHabitacion[]; estados: EstadoHabitacion[] }
  puedeEditar: boolean
}

export interface DatosHabitacion {
  tipo: number
  estado: number
  comentario: string | null
  extension: string | null
  television: boolean
  aireAcondicionado: boolean
  cabana: boolean
}

export const obtenerHabitaciones = () => pedir<PantallaHabitaciones>('/api/catalogos/habitaciones')
export const modificarHabitacion = (numero: number, d: DatosHabitacion) =>
  pedir<Habitacion>(`/api/catalogos/habitaciones/${numero}`, { method: 'PUT', body: JSON.stringify(d) })
export const crearHabitacion = (numero: number, x: number | null, y: number | null, d: DatosHabitacion) =>
  pedir<Habitacion>('/api/catalogos/habitaciones', { method: 'POST', body: JSON.stringify({ numero, x, y, ...d }) })
export const posicionSugerida = (numero: number) =>
  pedir<{ x: number; y: number }>(`/api/catalogos/habitaciones/posicion/${numero}`)

// ---- catalogos: tipos de habitacion ----

export interface DatosTipo {
  descripcion: string
  siglas: string | null
  minimo: number
  maximo: number
}

export const obtenerTipos = () => pedir<{ tipos: TipoHabitacion[]; puedeEditar: boolean }>('/api/catalogos/tipos')
export const modificarTipo = (id: number, d: DatosTipo) =>
  pedir<TipoHabitacion>(`/api/catalogos/tipos/${id}`, { method: 'PUT', body: JSON.stringify(d) })
export const crearTipo = (d: DatosTipo) =>
  pedir<TipoHabitacion>('/api/catalogos/tipos', { method: 'POST', body: JSON.stringify(d) })

// ---- catalogos: servicios ----

export interface Servicio {
  id: number
  descripcion: string
  precio: number
  conItbis: number
  conItbisYPropina: number
  impuesto: boolean
  cuenta: number
  delSistema: string | null
  usos: number
  usuario: string | null
  fecha: string | null
}

export interface DatosServicio {
  descripcion: string
  precio: number
  impuesto: boolean
}

export const obtenerServicios = () => pedir<{ servicios: Servicio[]; puedeEditar: boolean }>('/api/catalogos/servicios')
export const modificarServicio = (id: number, d: DatosServicio) =>
  pedir<Servicio>(`/api/catalogos/servicios/${id}`, { method: 'PUT', body: JSON.stringify(d) })
export const crearServicio = (d: DatosServicio) =>
  pedir<Servicio>('/api/catalogos/servicios', { method: 'POST', body: JSON.stringify(d) })

/** Mismo factor que la API (ITBIS 18 % + propina 10 %): solo para mostrar mientras se escribe. */
export const FACTOR_IMPUESTOS = 1.28

// ---- formatos ----

const pesos = new Intl.NumberFormat('es-DO', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
export const dinero = (n: number | null | undefined) => (n == null ? '' : pesos.format(n))

/** dd/mm (o dd/mm/aaaa si es de otro ano). Las fechas de la API vienen sin zona: se leen como locales. */
export function fecha(iso: string | null | undefined, conAno = false): string {
  if (!iso) return ''
  const [a, m, d] = iso.slice(0, 10).split('-')
  return conAno || a !== String(new Date().getFullYear()) ? `${d}/${m}/${a}` : `${d}/${m}`
}
