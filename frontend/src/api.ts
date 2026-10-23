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

/** Nombre del hotel (Parametro.NombreEmpresa). No pide sesion. */
export async function obtenerEmpresa(): Promise<{ nombre: string; rnc: string | null }> {
  try {
    const r = await fetch('/api/empresa')
    if (r.ok) return await r.json()
  } catch {
    // sin conexion: el nombre generico
  }
  return { nombre: 'Hotel', rnc: null }
}

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

// ---- catalogos: clientes ----

export interface ClienteResumen {
  numero: number
  nombre: string
  rnc: string | null
  telefono: string | null
  tipoComprobante: number
  tipoNombre: string
  activo: boolean
}

export interface DatosCliente {
  nombre: string
  rnc: string | null
  cedula: string | null
  pasaporte: string | null
  direccion: string | null
  sector: string | null
  ciudad: string | null
  telefono: string | null
  contacto: string | null
  correo: string | null
  tipoComprobante: number
  itbis: boolean
  propina: boolean
  limiteCredito: number
  condiciones: string | null
  extranjero: boolean
  activo: boolean
}

export interface Cliente extends DatosCliente {
  numero: number
  estatus: string | null
}

export interface TipoComprobante {
  id: number
  nombre: string
  pideRnc: boolean
}

export interface PantallaClientes {
  pagina: { clientes: ClienteResumen[]; total: number; mostrados: number }
  comprobantes: TipoComprobante[]
  puedeEditar: boolean
}

export const buscarClientes = (texto: string) =>
  pedir<PantallaClientes>(`/api/catalogos/clientes?buscar=${encodeURIComponent(texto)}`)
export const obtenerCliente = (numero: number) => pedir<Cliente>(`/api/catalogos/clientes/${numero}`)
export const modificarCliente = (numero: number, d: DatosCliente) =>
  pedir<{ cliente: Cliente; avisos: string[] }>(`/api/catalogos/clientes/${numero}`, { method: 'PUT', body: JSON.stringify(d) })
export const crearCliente = (d: DatosCliente) =>
  pedir<{ cliente: Cliente; avisos: string[] }>('/api/catalogos/clientes', { method: 'POST', body: JSON.stringify(d) })

// ---- reservas ----

export interface ReservaResumen {
  id: number
  cliente: string
  huesped: string | null
  entrada: string
  salida: string
  noches: number
  estado: number
  estadoNombre: string
  habitaciones: string | null
  cantidadHabitaciones: number
  adelantos: number
  telefono: string | null
}

export interface LineaReserva {
  tipo: number
  cantidad: number
  adultos: number
  ninos: number
  bebes: number
  credito: boolean
  categoria: string
  tarifa: number
}

export interface LineaVista extends LineaReserva {
  secuencia: number
  tipoNombre: string
  importe: number
  noHabitacion: number | null
}

export interface Adelanto {
  id: number
  fecha: string
  monto: number
  montoUs: number | null
  formaPago: number
  formaPagoNombre: string
  estatus: string
  usuario: string | null
}

export interface DatosReserva {
  nombreCliente: string
  clienteId: number | null
  contacto: string | null
  telefono: string | null
  huesped: string | null
  cedula: string | null
  pasaporte: string | null
  rnc: string | null
  entrada: string
  salida: string
  comentario: string | null
  formaPago: number
  categoria: string | null
  lineas: LineaReserva[]
}

export interface Reserva extends Omit<DatosReserva, 'lineas' | 'nombreCliente'> {
  id: number
  cliente: string
  noches: number
  personas: number
  estado: number
  estadoNombre: string
  usuario: string | null
  creada: string | null
  lineas: LineaVista[]
  adelantos: Adelanto[]
  total: number
  totalAdelantos: number
}

export interface Disponibilidad {
  tipo: number
  descripcion: string
  habitaciones: number
  ocupadas: number
  reservadas: number
  libres: number
}

export interface Opcion { id: number; nombre: string }

export interface OpcionesReserva {
  formasPago: Opcion[]
  formasPagoAdelanto: Opcion[]
  anulaAdelantos: boolean
}

/** Error que pide confirmacion (la API respondio 409). */
export class PideConfirmar extends Error {}

export const buscarReservas = (desde: string, hasta: string, porSalida: boolean, texto: string, estado: number) =>
  pedir<ReservaResumen[]>(`/api/reservas?desde=${desde}&hasta=${hasta}&porSalida=${porSalida}` +
    `&buscar=${encodeURIComponent(texto)}${estado ? `&estado=${estado}` : ''}`)
export const opcionesReserva = () => pedir<OpcionesReserva>('/api/reservas/opciones')
export const obtenerReserva = (id: number) => pedir<Reserva>(`/api/reservas/${id}`)
export const disponibilidad = (entrada: string, salida: string, excluir?: number) =>
  pedir<Disponibilidad[]>(`/api/reservas/disponibilidad?entrada=${entrada}&salida=${salida}${excluir ? `&excluir=${excluir}` : ''}`)
export const crearReserva = (d: DatosReserva) =>
  pedir<{ reserva: Reserva; avisos: string[] }>('/api/reservas', { method: 'POST', body: JSON.stringify(d) })
export const modificarReserva = (id: number, d: DatosReserva) =>
  pedir<{ reserva: Reserva; avisos: string[] }>(`/api/reservas/${id}`, { method: 'PUT', body: JSON.stringify(d) })
export async function cancelarReserva(id: number, aunConAdelantos: boolean): Promise<Reserva> {
  try {
    return await pedir<Reserva>(`/api/reservas/${id}/cancelar?aunConAdelantos=${aunConAdelantos}`, { method: 'POST' })
  } catch (e) {
    if (e instanceof Error && e.message.includes('confirme')) throw new PideConfirmar(e.message)
    throw e
  }
}
export const registrarAdelanto = (id: number, monto: number, formaPago: number, montoUs: number | null) =>
  pedir<Reserva>(`/api/reservas/${id}/adelantos`, { method: 'POST', body: JSON.stringify({ monto, formaPago, montoUs }) })
export const anularAdelanto = (id: number, adelanto: number) =>
  pedir<Reserva>(`/api/reservas/${id}/adelantos/${adelanto}/anular`, { method: 'POST' })

// ---- recepcion: check-in y hospedaje de la noche ----

export interface LlegadaHoy {
  reserva: number
  cliente: string
  clienteId: number | null
  huesped: string | null
  entrada: string
  salida: string
  habitaciones: number
  detalle: string | null
  adelantos: number
  atrasada: boolean
}

export interface LugarCheckIn {
  secuencia: number
  indice: number
  tipo: number
  tipoNombre: string
  categoria: string | null
  tarifa: number
  adultos: number
  ninos: number
  bebes: number
  credito: boolean
  sugerida: number | null
}

export interface LibreCheckIn { numero: number; tipo: number; tipoNombre: string; disponible: boolean }

export interface PreparacionCheckIn {
  reserva: number
  cliente: string
  clienteId: number | null
  huesped: string | null
  entrada: string
  salida: string
  noches: number
  adelantos: number
  lugares: LugarCheckIn[]
  libres: LibreCheckIn[]
}

/** La habitacion de cada lugar, con la categoria y la tarifa (sin impuestos) que se cobran. */
export interface Asignacion { secuencia: number; indice: number; habitacion: number; categoria?: string; tarifa?: number }

export type EstadoNoche = 'PorCargar' | 'YaCargada' | 'Transferida' | 'SinTarifa'

export interface NocheCuenta {
  habitacion: number
  factura: number
  huesped: string | null
  tarifa: number
  salida: string | null
  estado: EstadoNoche
  salidaVencida: boolean
}

export interface CargaNoche { noche: string; descripcion: string; cuentas: NocheCuenta[]; porCargar: number; total: number }

export const llegadasHoy = () => pedir<LlegadaHoy[]>('/api/hospedaje/llegadas')
export const prepararCheckIn = (reserva: number) => pedir<PreparacionCheckIn>(`/api/hospedaje/checkin/${reserva}`)
export const hacerCheckIn = (reserva: number, habitaciones: Asignacion[], clienteId: number | null) =>
  pedir<{ reserva: number; cuentas: { habitacion: number; factura: number }[]; avisos: string[] }>(
    `/api/hospedaje/checkin/${reserva}`, { method: 'POST', body: JSON.stringify({ clienteId, habitaciones }) })
export const verNoche = (noche: string) => pedir<CargaNoche>(`/api/hospedaje/noche/${noche}`)
export const cargarNoche = (noche: string, sinVencidas: boolean) =>
  pedir<{ noche: CargaNoche; cargadas: number }>(`/api/hospedaje/noche/${noche}?sinVencidas=${sinVencidas}`, { method: 'POST' })

// ---- cargos a la cuenta de la habitacion ----

export const permisosCuenta = () => pedir<{ anulaCargos: boolean; anulaRecibos: boolean }>('/api/cuentas/permisos')

export interface PagoRecibo {
  formaPago: number
  descripcion: string
  monto: number
  montoUs: number | null
  noCheque: string | null
  fechaCheque?: string | null
  banco: string | null
}

export interface ReciboPago {
  numero: number
  fecha: string
  habitacion: number
  nombre: string
  concepto: string | null
  monto: number
  estatus: string
  posteado: boolean
  usuario: string | null
  pagos: (PagoRecibo & { formaPagoNombre: string })[]
}

export const recibosDeCuenta = (habitacion: number) => pedir<ReciboPago[]>(`/api/cuentas/${habitacion}/recibos`)
export const hacerRecibo = (habitacion: number, concepto: string, pagos: PagoRecibo[]) =>
  pedir<ReciboPago>(`/api/cuentas/${habitacion}/recibos`, { method: 'POST', body: JSON.stringify({ concepto, pagos }) })
export const anularRecibo = (numero: number) => pedir<ReciboPago>(`/api/recibos/${numero}/anular`, { method: 'POST' })
export const cargarACuenta = (habitacion: number, servicio: number, descripcion: string, cantidad: number, precio: number) =>
  pedir<CuentaHabitacion>(`/api/cuentas/${habitacion}/cargos`, { method: 'POST', body: JSON.stringify({ servicio, descripcion, cantidad, precio }) })
export const anularCargo = (habitacion: number, secuencia: number) =>
  pedir<CuentaHabitacion>(`/api/cuentas/${habitacion}/cargos/${secuencia}/anular`, { method: 'POST' })

/** Las lineas que se pueden anular desde la cuenta (la API lo vuelve a revisar). */
export function seAnula(l: LineaCuenta): boolean {
  const d = l.descripcion.toUpperCase()
  return l.activo && l.importe > 0 && !d.startsWith('TRANSF') && !d.startsWith('CAFETERIA - FACTURA') && !d.startsWith('PAY-OUT')
}

/** Fecha de hoy (o +dias) como aaaa-mm-dd, en la hora local. */
export function hoyIso(dias = 0): string {
  const d = new Date()
  d.setDate(d.getDate() + dias)
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

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

// ---- hospedaje: cambio de salida (MIG_24) ----

export interface CambioSalida { fecha: string; habitacion: number; anterior: string | null; nueva: string; comentario: string | null; usuario: string | null }

export interface SalidaCuenta {
  habitacion: number
  factura: number
  reserva: number | null
  entrada: string
  salida: string
  salidaReserva: string | null
  transferida: boolean
  /** Las demas habitaciones abiertas de la misma reserva, con su salida. */
  otras: { habitacion: number; salida: string }[]
  cambios: CambioSalida[]
}

export const verSalida = (habitacion: number) => pedir<SalidaCuenta>(`/api/cuentas/${habitacion}/salida`)
export const cambiarSalida = (habitacion: number, salida: string, todas: boolean, comentario: string) =>
  pedir<{ salida: SalidaCuenta; cambioLaReserva: boolean; avisos: string[] }>(`/api/cuentas/${habitacion}/salida`, {
    method: 'POST', body: JSON.stringify({ salida, todas, comentario }),
  })

// ---- hospedaje: cambio a una habitacion libre ----

export interface PreparacionCambio {
  habitacion: number
  factura: number
  reserva: number | null
  tipo: number
  tipoNombre: string
  categoria: string
  /** Lo que se cobra cada noche ahora (sin impuestos). */
  tarifa: number
  tieneLinea: boolean
  libres: LibreCheckIn[]
}

export const prepararCambio = (habitacion: number) => pedir<PreparacionCambio>(`/api/cuentas/${habitacion}/cambio`)
export const cambiarHabitacion = (habitacion: number, hasta: number, categoria: string, tarifa: number | null) =>
  pedir<{ desde: number; hasta: number; factura: number; accion: string; avisos: string[] }>(`/api/cuentas/${habitacion}/cambio`, {
    method: 'POST', body: JSON.stringify({ hasta, categoria, tarifa }),
  })

// ---- hospedaje: transferencia a una habitacion ocupada ----

export interface OcupadaTransferencia { habitacion: number; factura: number; huesped: string | null; cliente: string | null; impuestos: string }

export interface PreparacionTransferencia {
  habitacion: number
  factura: number
  huesped: string | null
  cliente: string | null
  impuestos: string
  /** Cargos sin impuestos que pasan en una linea. */
  cargos: number
  payOut: number
  /** Pagos y adelantos (negativo) que pasan como pagos. */
  pagos: number
  saldo: number
  ocupadas: OcupadaTransferencia[]
}

export const prepararTransferencia = (habitacion: number) => pedir<PreparacionTransferencia>(`/api/cuentas/${habitacion}/transferencia`)
export const transferirCuenta = (habitacion: number, hasta: number) =>
  pedir<{ desde: number; hasta: number; facturaDestino: number; cerrada: number | null; cargos: number; pagos: number; avisos: string[] }>(
    `/api/cuentas/${habitacion}/transferencia`, { method: 'POST', body: JSON.stringify({ hasta }) })
