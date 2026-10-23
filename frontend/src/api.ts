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

// ---- etapa 5: check-out con e-CF ----

export interface TipoComprobante { id: number; codigo: string; nombre: string }

export interface PreparacionCheckout {
  habitacion: number
  factura: number
  cliente: string | null
  huesped: string | null
  rnc: string | null
  subTotal: number
  itbis: number
  propina: number
  total: number
  saldo: number
  transferida: boolean
  /** Un e-NCF que la cuenta ya traia (de antes): se usa ese. */
  ncfActual: string | null
  tipos: TipoComprobante[]
  sugerido: number
  /** Por que no se puede cerrar todavia (balance, totales). */
  impide: string | null
}

export interface EstadoEcf { encf: string; factura: number; estado: string; mensaje: string | null; aceptado: boolean }

export interface RespuestaCheckout {
  factura: { numero: number; encf: string | null; total: number; transferida: boolean }
  dgii: EstadoEcf | null
}

export interface LineaImpresa { fecha: string | null; descripcion: string; cantidad: number; precio: number; importe: number }

export interface FacturaImpresa {
  numero: number
  encf: string | null
  tipoNombre: string
  fecha: string
  vencimiento: string | null
  emisor: { rnc: string; razonSocial: string; direccion: string; telefono: string | null }
  cliente: string | null
  rnc: string | null
  huesped: string | null
  habitacion: number
  entrada: string | null
  salida: string | null
  items: LineaImpresa[]
  otros: LineaImpresa[]
  subTotal: number
  itbis: number
  propina: number
  total: number
  transferida: boolean
  timbre: { codigoSeguridad: string | null; fechaHoraFirma: string | null; url: string | null }
}

export const prepararCheckout = (habitacion: number) => pedir<PreparacionCheckout>(`/api/cuentas/${habitacion}/checkout`)
export const hacerCheckout = (habitacion: number, tipo: number, rnc: string) =>
  pedir<RespuestaCheckout>(`/api/cuentas/${habitacion}/checkout`, { method: 'POST', body: JSON.stringify({ tipo, rnc }) })
export const estadoDgii = (numero: number) => pedir<EstadoEcf>(`/api/facturas/${numero}/dgii`)
export const reenviarEcf = (numero: number) => pedir<EstadoEcf>(`/api/facturas/${numero}/enviar`, { method: 'POST' })
export const obtenerFacturaImpresa = (numero: number) => pedir<FacturaImpresa>(`/api/facturas/${numero}`)

export interface FacturaCerrada {
  numero: number
  fecha: string
  habitacion: number
  cliente: string | null
  huesped: string | null
  encf: string | null
  total: number
  transferida: boolean
  /** null: sin e-NCF (transferida o vieja) */
  dgii: EstadoEcf | null
  /** del walk-in (numera aparte; habitacion 0) */
  walkIn: boolean
  /** Hospedaje, WalkIn, Cafeteria, NotaHospedaje, NotaWalkIn o NotaCafeteria */
  origen: string
  /** en una nota de credito: el e-NCF que modifica */
  modifica: string | null
}

export const listarFacturas = (desde: string, hasta: string, texto: string, ambito: 'hotel' | 'cafeteria' = 'hotel') =>
  pedir<FacturaCerrada[]>(`/api/facturas?desde=${desde}&hasta=${hasta}&texto=${encodeURIComponent(texto)}&ambito=${ambito}`)

// ---- etapa 6: walk-in ----

export interface LineaWalkIn { servicio: number; descripcion: string; cantidad: number; precio: number }

export interface PagoWalkIn { formaPago: number; descripcion: string; monto: number; montoUs?: number | null; noCheque?: string | null; banco?: string | null }

export interface NuevoWalkIn {
  nombre: string
  cedula: string
  rnc: string
  telefono: string
  direccion: string
  tipo: number
  conPropina: boolean
  lineas: LineaWalkIn[]
  pagos: PagoWalkIn[]
}

export interface RespuestaWalkIn {
  walkIn: { numero: number; encf: string; subTotal: number; itbis: number; propina: number; total: number; recibido: number; devuelta: number; recibo: number }
  dgii: EstadoEcf
}

export interface TicketWalkIn {
  numero: number
  encf: string | null
  tipoNombre: string
  fecha: string
  vencimiento: string | null
  emisor: { rnc: string; razonSocial: string; direccion: string; telefono: string | null }
  nombre: string | null
  identificacion: string | null
  items: LineaImpresa[]
  subTotal: number
  itbis: number
  propina: number
  total: number
  pagos: { formaPago: string; descripcion: string; monto: number }[]
  concepto: string | null
  usuario: string | null
  timbre: { codigoSeguridad: string | null; fechaHoraFirma: string | null; url: string | null }
}

export const crearWalkIn = (w: NuevoWalkIn) => pedir<RespuestaWalkIn>('/api/walkins', { method: 'POST', body: JSON.stringify(w) })
export const ticketWalkIn = (numero: number) => pedir<TicketWalkIn>(`/api/walkins/${numero}/ticket`)
export const reenviarWalkIn = (numero: number) => pedir<EstadoEcf>(`/api/walkins/${numero}/enviar`, { method: 'POST' })

// ---- etapa 7: notas de credito E34 ----

export interface NotaPrevia { numero: number; encf: string | null; fecha: string; total: number; codigo: number | null; razon: string | null }

export interface PreparacionNota {
  origen: 'hospedaje' | 'walkin' | 'cafeteria'
  factura: number
  encf: string
  fecha: string
  cliente: string | null
  rnc: string | null
  subTotal: number
  itbis: number
  propina: number
  total: number
  dgii: EstadoEcf
  notas: NotaPrevia[]
  pendiente: number
  dias: number
  /** mas de 30 dias: la nota no rebaja el ITBIS (IndicadorNotaCredito 1) */
  sinItbis: boolean
  puedeAnular: boolean
  puedeMonto: boolean
}

export interface ResultadoNota { numero: number; encf: string; monto: number; mensaje: string; dgii: EstadoEcf }

export interface NotaImpresa {
  origen: string
  numero: number
  encf: string
  fecha: string
  emisor: { rnc: string; razonSocial: string; direccion: string; telefono: string | null }
  cliente: string | null
  rnc: string | null
  ncfModificado: string
  fechaModificado: string
  codigo: number
  razon: string | null
  desglose: { gravado: number; exento: number; itbis: number; propina: number; monto: number }
  sinItbis: boolean
  timbre: { codigoSeguridad: string | null; fechaHoraFirma: string | null; url: string | null }
}

export const prepararNota = (origen: string, factura: number) => pedir<PreparacionNota>(`/api/notas/${origen}/factura/${factura}`)
export const crearNota = (origen: string, factura: number, d: { codigo: number; monto: number; motivo: string; responsable: string; clave: string }) =>
  pedir<ResultadoNota>(`/api/notas/${origen}/factura/${factura}`, { method: 'POST', body: JSON.stringify(d) })
export const notaImpresa = (origen: string, numero: number) => pedir<NotaImpresa>(`/api/notas/${origen}/${numero}`)
export const reenviarNota = (origen: string, numero: number) => pedir<EstadoEcf>(`/api/notas/${origen}/${numero}/enviar`, { method: 'POST' })

// ---- etapa 8: cafeteria ----

export type EstadoMesa = 'Libre' | 'Abierta' | 'PreCuenta'

export interface MesaCafeteria {
  mesa: number
  descripcion: string
  x: number
  y: number
  estado: EstadoMesa
  factura: number | null
  total: number
  lineas: number
  habitacion: number | null
  abierta: string | null
}

export interface ArticuloCafeteria {
  referencia: string
  descripcion: string
  familia: number
  nombreFamilia: string
  precio: number
  /** precio por trago (fraccion); 0 si no se vende asi */
  precio2: number
  /** tragos por unidad; 1 = no se vende por trago */
  fraccion: number
}

export interface LineaMesa {
  secuencia: number
  referencia: string
  descripcion: string
  cantidad: number
  precio: number
  importe: number
  /** 1 = por unidad, 2 = por trago */
  tragos: number
  impreso: boolean
}

export interface CuentaMesa {
  mesa: number
  nombreMesa: string
  factura: number
  fecha: string
  cliente: number
  nombreCliente: string | null
  habitacion: number | null
  conItbis: boolean
  conPropina: boolean
  lineas: LineaMesa[]
  totales: { subTotal: number; itbis: number; propina: number; total: number }
  preCuentaAlDia: boolean
  tasaDolar: number
  abierta: string
}

export interface PreCuentaImpresa {
  emisor: { rnc: string; razonSocial: string; direccion: string; telefono: string | null }
  cuenta: CuentaMesa
  hora: string
  usuario: string | null
}

/** Como pedir, pero 204 (sin contenido) da null: la mesa esta libre. */
async function pedirOpcional<T>(ruta: string, opciones: RequestInit = {}): Promise<T | null> {
  const s = sesionGuardada()
  const r = await fetch(ruta, {
    ...opciones,
    headers: { 'Content-Type': 'application/json', ...(s ? { Authorization: `Bearer ${s.token}` } : {}), ...opciones.headers },
  })
  if (r.status === 401 && s) {
    guardar(null)
    throw new SesionVencida('La sesion vencio: ingrese de nuevo.')
  }
  if (!r.ok) throw new Error((await r.text()) || `Error ${r.status}`)
  return r.status === 204 ? null : ((await r.json()) as T)
}

const CAFE = '/api/cafeteria'
export const mesasCafeteria = () => pedir<MesaCafeteria[]>(`${CAFE}/mesas`)
export const articulosCafeteria = () => pedir<ArticuloCafeteria[]>(`${CAFE}/articulos`)
export const cuentaMesa = (mesa: number) => pedirOpcional<CuentaMesa>(`${CAFE}/mesas/${mesa}`)
export const agregarArticulo = (mesa: number, referencia: string, cantidad: number, porFraccion: boolean | null = null) =>
  pedir<CuentaMesa>(`${CAFE}/mesas/${mesa}/lineas`, { method: 'POST', body: JSON.stringify({ referencia, cantidad, porFraccion }) })
export const agregarEspecial = (mesa: number, bebida: boolean, descripcion: string, precio: number) =>
  pedir<CuentaMesa>(`${CAFE}/mesas/${mesa}/especial`, { method: 'POST', body: JSON.stringify({ bebida, descripcion, precio }) })
export const cambiarLineaMesa = (mesa: number, secuencia: number, cantidad: number, porFraccion: boolean | null = null) =>
  pedir<CuentaMesa>(`${CAFE}/mesas/${mesa}/lineas/${secuencia}`, { method: 'PUT', body: JSON.stringify({ cantidad, porFraccion }) })
export const borrarLineaMesa = (mesa: number, secuencia: number) =>
  pedirOpcional<CuentaMesa>(`${CAFE}/mesas/${mesa}/lineas/${secuencia}`, { method: 'DELETE' })
export const preCuentaMesa = (mesa: number) => pedir<PreCuentaImpresa>(`${CAFE}/mesas/${mesa}/precuenta`, { method: 'POST' })
export const preCuentaImpresa = (factura: number) => pedir<PreCuentaImpresa>(`${CAFE}/cuentas/${factura}/precuenta`)
export const moverMesa = (mesa: number, destino: number) =>
  pedir<CuentaMesa>(`${CAFE}/mesas/${mesa}/mover`, { method: 'POST', body: JSON.stringify({ destino }) })

// ---- etapa 8b: cobro de la cafeteria ----

export interface BancoTarjeta { id: number; nombre: string }

export interface ClienteCobro { numero: number; nombre: string; rnc: string | null; itbis: boolean; propina: boolean; tipo: number | null; credito: boolean }

export interface PagoCafeteria { formaPago: number; monto: number; tipoTarjeta: number | null }

export interface DatosCobro { tipo: number; cliente: number | null; rnc: string; nombre: string; pagos: PagoCafeteria[] }

export interface RespuestaCobro {
  cobro: { factura: number; mesa: number; encf: string; subTotal: number; itbis: number; propina: number; total: number; recibido: number; devuelta: number; credito: boolean }
  dgii: EstadoEcf
}

export interface TicketCafeteria {
  factura: number
  mesa: number
  encf: string | null
  tipoNombre: string
  fecha: string
  vencimiento: string | null
  emisor: { rnc: string; razonSocial: string; direccion: string; telefono: string | null }
  nombre: string | null
  identificacion: string | null
  items: LineaImpresa[]
  subTotal: number
  itbis: number
  propina: number
  total: number
  pagos: { formaPago: string; descripcion: string; monto: number }[]
  devuelta: number
  usuario: string | null
  timbre: { codigoSeguridad: string | null; fechaHoraFirma: string | null; url: string | null }
}

export const bancosTarjeta = () => pedir<BancoTarjeta[]>(`${CAFE}/bancos`)
export const nombreDgii = (rnc: string) => pedirOpcional<{ nombre: string }>(`${CAFE}/rnc/${encodeURIComponent(rnc)}`)
export const clienteCobro = (numero: number) => pedir<ClienteCobro>(`${CAFE}/clientes/${numero}`)
export const cobrarMesa = (mesa: number, d: DatosCobro) =>
  pedir<RespuestaCobro>(`${CAFE}/mesas/${mesa}/cobrar`, { method: 'POST', body: JSON.stringify(d) })
export const ticketCafeteria = (factura: number) => pedir<TicketCafeteria>(`${CAFE}/facturas/${factura}/ticket`)
export const reenviarCafeteria = (factura: number) => pedir<EstadoEcf>(`${CAFE}/facturas/${factura}/enviar`, { method: 'POST' })

// ---- etapa 8c: cargo a la habitacion ----

export interface HabitacionCafeteria { habitacion: number; factura: number; huesped: string | null; salida: string | null; credito: boolean }

export interface ResultadoCargo { factura: number; mesa: number; habitacion: number; huesped: string | null; subTotal: number; total: number }

export const habitacionesCafeteria = () => pedir<HabitacionCafeteria[]>(`${CAFE}/habitaciones`)
export const cargarAHabitacion = (mesa: number, habitacion: number) =>
  pedir<ResultadoCargo>(`${CAFE}/mesas/${mesa}/habitacion`, { method: 'POST', body: JSON.stringify({ habitacion }) })

// ---- etapa 9a: cuadre y cierre de caja ----

export type AmbitoCaja = 'hotel' | 'cafeteria'
export type ClaseForma = 'Efectivo' | 'Dolares' | 'OtroDinero' | 'SinDinero' | 'Salida'

export interface MovimientoCaja {
  tipo: string
  numero: number
  fecha: string
  referencia: string | null
  nombre: string | null
  forma: number
  formaNombre: string
  monto: number
  montoUs: number
}

export interface ResumenCaja {
  formas: { forma: number; nombre: string; clase: ClaseForma; cantidad: number; monto: number; montoUs: number }[]
  ingresos: number
  efectivo: number
  dolares: number
  sinDinero: number
}

export interface CuadreCaja { ambito: string; usuario: string; desde: string | null; movimientos: MovimientoCaja[]; resumen: ResumenCaja }

export interface CierreResumen {
  cierre: number
  ambito: string
  usuario: string
  hasta: string
  total: number
  esperadoEfectivo: number
  contadoEfectivo: number
  esperadoDolares: number
  contadoDolares: number
  comentario: string | null
}

export interface CierreImpreso {
  emisor: { rnc: string; razonSocial: string; direccion: string; telefono: string | null }
  cierre: CierreResumen
  desde: string | null
  movimientos: MovimientoCaja[]
  resumen: ResumenCaja
}

export const cuadreCaja = (ambito: AmbitoCaja) => pedir<CuadreCaja>(`/api/caja/${ambito}/pendiente`)
export const cierresCaja = (ambito: AmbitoCaja) => pedir<CierreResumen[]>(`/api/caja/${ambito}/cierres`)
export const cerrarCaja = (ambito: AmbitoCaja, d: { efectivo: number; dolares: number; comentario: string; movimientos: number; ingresos: number }) =>
  pedir<CierreResumen>(`/api/caja/${ambito}/cerrar`, { method: 'POST', body: JSON.stringify(d) })
export const cierreImpreso = (cierre: number) => pedir<CierreImpreso>(`/api/caja/cierres/${cierre}`)

// ---- etapa 9b: reportes de ventas y comprobantes ----

export interface VentaHotel { origen: string; numero: number; fecha: string; habitacion: string | null; cliente: string | null; encf: string | null; subTotal: number; itbis: number; propina: number; total: number; transferida: boolean }

export interface VentaCafeteria {
  numero: number; fecha: string; mesa: number; cliente: string | null; encf: string | null; alimentos: number; bebidas: number; otros: number
  subTotal: number; itbis: number; propina: number; total: number; efectivo: number; tarjeta: number; credito: number; habitacion: number
  exceso: number; habitacionId: number | null
}

export interface Comprobante { origen: string; numero: number; fecha: string; tipo: string; ncf: string; rnc: string | null; cliente: string | null; base: number; itbis: number; propina: number; total: number; modifica: string | null; estado: string | null }

export interface ReporteComprobantes { lista: Comprobante[]; porTipo: { tipo: string; cantidad: number; base: number; itbis: number; propina: number; total: number }[] }

export interface FacturaCancelada { cancelacion: number; fechaCancelacion: string; factura: number; habitacion: number | null; cliente: string | null; cargos: number; autoriza: string | null; usuario: string | null; motivo: string | null }

const rango = (desde: string, hasta: string) => `desde=${desde}&hasta=${hasta}`
export const reporteVentasHotel = (desde: string, hasta: string) => pedir<VentaHotel[]>(`/api/reportes/ventas-hotel?${rango(desde, hasta)}`)
export const reporteVentasCafeteria = (desde: string, hasta: string) => pedir<VentaCafeteria[]>(`/api/reportes/ventas-cafeteria?${rango(desde, hasta)}`)
export const reporteComprobantes = (desde: string, hasta: string) => pedir<ReporteComprobantes>(`/api/reportes/comprobantes?${rango(desde, hasta)}`)
export const reporteCanceladas = (desde: string, hasta: string) => pedir<FacturaCancelada[]>(`/api/reportes/canceladas?${rango(desde, hasta)}`)

// ---- etapa 9c: reportes del hospedaje ----

export interface ClienteReporte {
  numero: number; nombre: string; identificacion: string | null; telefono: string | null; direccion: string | null; contacto: string | null
  correo: string | null; comprobante: number | null; limiteCredito: number; itbis: boolean; propina: boolean; estatus: string | null
}

export const reporteClientes = (texto: string, conRnc: boolean, conCredito: boolean, inactivos: boolean) =>
  pedir<ClienteReporte[]>(`/api/reportes/clientes?texto=${encodeURIComponent(texto)}&conRnc=${conRnc}&conCredito=${conCredito}&inactivos=${inactivos}`)

// ---- etapa 9d: secuencias e-CF autorizadas ----

export interface SecuenciaEcf {
  codigo: string; descripcion: string; autorizacion: string | null; fechaAutorizacion: string | null; desde: number | null; hasta: number | null
  vence: string | null; ultima: number; siguiente: number; restantes: number; dias: number | null
  /** OK, POR AGOTARSE, POR VENCER, AGOTADA, VENCIDA, SIN AUTORIZACION o NO SE USA */
  estado: string
}

export interface PantallaSecuencias { secuencias: SecuenciaEcf[]; puedeEditar: boolean; avisoRestantes: number; avisoDias: number }

export const obtenerSecuencias = () => pedir<PantallaSecuencias>('/api/secuencias')
export const avisosSecuencias = () => pedir<SecuenciaEcf[]>('/api/secuencias/avisos')
export const guardarSecuencia = (codigo: string, d: { autorizacion: string; fechaAutorizacion: string; desde: number; hasta: number; vence: string }) =>
  pedir<{ mensaje: string }>(`/api/secuencias/${codigo}`, { method: 'PUT', body: JSON.stringify(d) })

// ---- etapa 9d: gastos menores (E43) ----

export interface GastoLista { numero: number; fecha: string; encf: string; descripcion: string; beneficiario: string; total: number; formaPago: string; usuario: string | null; dgii: EstadoEcf | null }

export interface PantallaGastos { gastos: GastoLista[]; puedeCrear: boolean; formasPago: Record<string, string>; tiposGasto: Record<string, string> }

export interface NuevoGasto { fecha: string; descripcion: string; bienServicio: number; cantidad: number; precio: number; formaPago: string; tipoGasto: string; beneficiario: string; cedula: string; detalle: string }

export interface TicketGasto {
  gasto: { encf: string; numero: number; fecha: string; descripcion: string; bienServicio: number; cantidad: number; precio: number; total: number; formaPago: string; tipoGasto: string; beneficiario: string; cedula: string | null; detalle: string; usuario: string | null }
  emisor: { rnc: string; razonSocial: string; direccion: string; telefono: string | null }
  vencimiento: string | null
  formaPago: string
  tipoGasto: string
  timbre: { codigoSeguridad: string | null; fechaHoraFirma: string | null; url: string | null }
}

export const listarGastos = (desde: string, hasta: string) => pedir<PantallaGastos>(`/api/gastos?desde=${desde}&hasta=${hasta}`)
export const crearGasto = (g: NuevoGasto) => pedir<{ numero: number; encf: string; dgii: EstadoEcf }>('/api/gastos', { method: 'POST', body: JSON.stringify(g) })
export const ticketGasto = (numero: number) => pedir<TicketGasto>(`/api/gastos/${numero}/ticket`)
export const reenviarGasto = (numero: number) => pedir<EstadoEcf>(`/api/gastos/${numero}/enviar`, { method: 'POST' })

// ---- etapa 9d: compras a informales (E41) ----

export interface CompraLista { numero: number; fecha: string; encf: string; cedula: string; proveedor: string; total: number; retenido: number; netoPagar: number; formaPago: string; usuario: string | null; dgii: EstadoEcf | null }

export interface PantallaCompras { compras: CompraLista[]; puedeCrear: boolean; formasPago: Record<string, string>; tiposGasto: Record<string, string>; retencionesIsr: Record<string, string> }

export interface LineaCompra { descripcion: string; bienServicio: number; cantidad: number; precio: number; gravado: boolean; monto?: number; itbis?: number; itbisRetenido?: number; isrRetenido?: number }

export interface NuevaCompra {
  fecha: string; fechaPago: string; cedula: string; proveedor: string; direccion: string; verificado: boolean; tipoIsr: string
  formaPago: string; tipoGasto: string; observacion: string; lineas: LineaCompra[]
}

export interface TicketCompra {
  compra: {
    encf: string; numero: number; fecha: string; fechaPago: string; cedula: string; proveedor: string; direccion: string | null; tipoIsr: string
    formaPago: string; tipoGasto: string; observacion: string | null; lineas: Required<LineaCompra>[]; montoGravado: number; montoExento: number
    itbis: number; total: number; itbisRetenido: number; isrRetenido: number; netoPagar: number; usuario: string | null
  }
  emisor: { rnc: string; razonSocial: string; direccion: string; telefono: string | null }
  vencimiento: string | null
  formaPago: string
  tipoGasto: string
  retencionIsr: string
  timbre: { codigoSeguridad: string | null; fechaHoraFirma: string | null; url: string | null }
}

export const listarCompras = (desde: string, hasta: string) => pedir<PantallaCompras>(`/api/compras?desde=${desde}&hasta=${hasta}`)
export const crearCompra = (c: NuevaCompra) => pedir<{ numero: number; encf: string; dgii: EstadoEcf }>('/api/compras', { method: 'POST', body: JSON.stringify(c) })
export const ticketCompra = (numero: number) => pedir<TicketCompra>(`/api/compras/${numero}/ticket`)
export const reenviarCompra = (numero: number) => pedir<EstadoEcf>(`/api/compras/${numero}/enviar`, { method: 'POST' })
