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
