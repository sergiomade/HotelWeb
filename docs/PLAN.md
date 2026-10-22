# HotelWeb - plan por etapas

Web app para sustituir, con el tiempo, el programa Delphi del hotel (`C:\Contabilidad\Sources\Hotel`, PrjHotel)
y de la cafeteria (`C:\Contabilidad\Sources\RestaurantHotel`, PrjCaja). Acordado el 2026-10-22.

## Decisiones

- **Misma base de datos que Delphi** (`Hotel_new` en desarrollo, `HotelPruebas` en la nube para pruebas, la real en
  el hotel). La web y Delphi conviven: se reemplaza pantalla por pantalla, sin migrar datos ni dia de corte.
- **No se cambian ni se borran tablas.** Lo que haga falta se agrega con un script nuevo (como los MIG del hotel).
- **Se reutilizan los procedimientos que ya funcionan** (`Stp_CHECKOUT`, `Stp_TransferiraOcupada`,
  `STP_PasarAlHotel`, `STp_NumeroComprobanteFiscal_E`...), para que Delphi y la web hagan exactamente lo mismo.
- **Mismos usuarios y claves de Delphi** (base Seguridad, tabla `Seg_Usuario`, clave con la funcion `Encriptar`).
- **.NET 8 + React 19 (Vite, TypeScript)**, como CertificacionWeb y Star Bus. Datos con Dapper.
- **e-CF de CertificacionWeb** (`C:\Proyecto_Dgii\CertificacionWeb`): armado de E31-E45, E33/E34, RFCE, PDF con QR,
  envio por WebEncf. Ya validado contra lo que acepto la DGII.
- **Instalacion en IIS en el servidor del hotel.**
- **A la DGII solo se envia en testecf** hasta el dia de usarla en real.

## Como funciona el hotel (lo que la web debe respetar)

- Catalogos: `hotelHabitacion` (51, con posicion x/y del plano), `HotelHabitacionTipo`, `HotelHabitacionTipoPrecio`
  (precio por tipo y categoria A-F, G = cortesia), `HotelHabitacionEstado` (Normal, Restringida, Bloqueada, No usar),
  `HotelServicioAdicional`, `CxcCliente`, articulos de la cafeteria (`InvArticulo`, `InvPreciosArticulo`).
- Hospedaje: reserva (`HotelHabitacionReserva` + `HotelHabitacionReservada`, adelantos `HotelReservaAdelanto`) ->
  check-in abre la cuenta (`HotelFactura` + detalle) -> cargos y recibos (`HotelReciboIngreso`) -> checkout
  (`Stp_CHECKOUT`, pasa a `HotelTmpFactura`) con e-CF. Ocupada = tiene cuenta abierta en `HotelFactura`.
- ITBIS y propina se calculan una sola vez sobre el subtotal de la cuenta, en el checkout.
- **Transferencia a habitacion ocupada:** los cargos pasan a la cuenta destino en una linea "Transf.de Hab.#" (sin
  ITBIS); la de origen queda Transferida = 1 y se cierra sin e-NCF, sin envio y en 0.00 (MIG_21). A una habitacion
  libre es un cambio de habitacion (la cuenta se mueve con el huesped).
- **e-NCF solo al confirmar el cierre** (checkout, cobro de la cafeteria): un intento fallido no gasta numero.
- **E32 de 250,000 o mas:** RNC o cedula (o pasaporte si es extranjero) y nombre del comprador, como un E31.
- **Cafeteria con credito a la habitacion:** si la habitacion tiene credito (`HotelHabitacionReservada.Credito`), la
  factura de la cafeteria se cierra con la forma de pago 8 (TRANSF.HABITACION), sin e-NCF, y `STP_PasarAlHotel`
  agrega a la cuenta de la habitacion una linea "CAFETERIA - FACTURA # n" por el SUBTOTAL (sin ITBIS ni propina).
- **Walk-in:** factura directa de servicios (`HotelFacturaWalking`).
- **Notas de credito (E34) y debito (E33)** en hospedaje, walk-in y cafeteria (hoy solo existen las E34).
  E34 pasados 30 dias: por el total con IndicadorNotaCredito = 1.

## Etapas

| # | Etapa | Entrega | Escribe en la base |
|---|---|---|---|
| 0 | Base del proyecto | Solucion .NET 8 + React, conexion, login con usuarios de Delphi, instalacion IIS | No |
| 1 | Rack de habitaciones | Plano (x/y de Delphi), colores por estado, huesped, salida, saldo; detalle de la cuenta | No |
| 2 | Catalogos | Habitaciones, tipos, precios por categoria, estados, servicios, clientes | Si |
| 3 | Reservaciones | Crear/modificar/cancelar, varias habitaciones, adelantos, proyeccion | Si |
| 4 | Hospedaje en curso | Check-in, cargos, recibos, credito, cambio de salida, cambio de habitacion, transferencia | Si |
| 5 | Checkout con e-CF | e-NCF al cerrar, reglas E31/E44/E45/250k, envio, PDF con QR, monitor | Si + DGII |
| 6 | Walk-in | Factura directa con e-CF | Si + DGII |
| 7 | Notas E34 y E33 | Hospedaje y walk-in, regla de 30 dias | Si + DGII |
| 8 | Cafeteria | Mesas, comandas, cobro con e-CF, cargo a habitacion, notas | Si + DGII |
| 9 | Caja y reportes | Cuadre, cambio de turno, ventas, secuencias, E43, E41 | Si |
| 10 | Puesta en marcha | Cada modulo a prueba junto a Delphi; al final se apaga Delphi | No |

Estimado a 2-3 h diarias: etapas 0-1 unas 2 semanas; 2-5 de 2 a 3 meses; 6-9 otros 2 a 3 meses (5 a 7 meses en total).

## Estado

- Etapa 0 (2026-10-22): HECHA y probada por el usuario (entra con su usuario de Delphi).
  - API .NET 8 (`backend/HotelWeb.Api`), Dapper sobre `Hotel_new` y `Seguridad` (appsettings.Development.json).
  - Ingreso con `Seg_Usuario`: `HotelWeb.Core/Seguridad/CifradoDelphi.cs` es la funcion `Encriptar` de Delphi
    (tablas copiadas del .pas; probada contra los 13 usuarios). Token JWT de 12 horas. La web no cambia claves.
  - `GET /api/diagnostico/ping` (sin sesion) y `GET /api/diagnostico` (empresa, habitaciones, ocupadas, conexion).
  - Pagina React: ingreso e inicio con el estado de la conexion. La API sirve la pagina (un solo sitio IIS).
  - `deploy/`: `preparar-paquete.ps1` y `instalar-iis.ps1` (sitio HotelWeb, puerto 8098). No instalado aun.
  - 21 pruebas.
- Pendiente de la etapa 0, para mas adelante: permisos por usuario (Delphi usa `Seg_Opcion`); hoy todo usuario
  activo entra.
- Etapa 1 (2026-10-22): HECHA y probada por el usuario (le gusto; se ajusto el tamano).
  - `GET /api/rack`: las habitaciones con su situacion (`HotelWeb.Core/Rack/SituacionHabitacion.cs`: Ocupada =
    cuenta abierta en HotelFactura y manda sobre el bloqueo; Llega hoy = reserva activa con FechaEntrada hoy y
    `HotelHabitacionReservada.NoHabitacion` = la habitacion; Bloqueada = estado sin Disponible), avisos (sale hoy,
    salida vencida, ocupada estando bloqueada), huesped, salida, personas, credito y saldo (`dbo.BalanceActualHabitacion`).
  - `GET /api/rack/{habitacion}/cuenta`: cuenta abierta con sus lineas y el desglose (`CalculoCuenta`, mismas reglas
    que la funcion: ITBIS 18 % salvo E44, propina 10 %, PAY-OUT y pagos sin impuestos, redondeo a pesos). Coincide
    con la funcion en las cuentas 102, 111 y 214 de Hotel_new.
  - Pagina: plano con la posicion x/y de Delphi (todo el ancho, 95 %, texto que crece con la pantalla), vista lista
    por piso para celular, filtro por situacion, panel de la cuenta, se refresca cada minuto.
  - 37 pruebas.- Etapa 2 (en curso, 2026-10-22): PRECIOS HECHOS y probados por el usuario.
  - Permisos: en el hotel los de Delphi no estan configurados (las pantallas piden la opcion 420 del modulo 30, que
    no existe; los 9 usuarios estan en el grupo FINANZAS; el login marca a todos como supervisor). Decision del
    usuario: una lista corta en appsettings `Permisos:EditanCatalogos` (Usuario_Id; hoy 1 Yudelka Brens y 4 Sergio
    Made, los mismos que abren Sucursal en Delphi). Los demas solo consultan. No se toca la base Seguridad.
  - `GET /api/catalogos/precios` (tabla + puedeEditar) y `PUT /api/catalogos/precios/{tipo}/{categoria}`.
    Se escribe el precio FINAL (con ITBIS y propina) y se guarda final / 1.28 con 3 decimales en
    HotelHabitacionTipoPrecio (`HotelWeb.Core/Catalogos/PrecioHabitacion.cs`); usuario (login cortado a 10) y fecha
    como Delphi. Valen para los check-in nuevos (Delphi lee el precio en UFrmMntCheckIn).
  - Pagina Precios: matriz tipo x categoria al 90 % del ancho, marca los finales con centavos (redondeo viejo).
  - 56 pruebas.
  - Falta del resto de la etapa 2: habitaciones, tipos, estados, servicios y clientes.- Etapa 2: HABITACIONES y TIPOS HECHOS (2026-10-22), probados por el usuario.
  - Menu: Rack | Catalogos (pestanas Habitaciones, Tipos, Precios) | Sistema. Catalogos al 90 % del ancho.
  - Habitaciones (`/api/catalogos/habitaciones`): crear y modificar tipo, estado, comentario, extension, TV, aire y
    "sabanas" (la columna hotelHabitacion.cabana significa sabanas, lo aclaro el usuario). No se borran (estado
    "No usar"). Una nueva va en la siguiente casilla libre de la fila de su piso (`ReglasHabitacion.PosicionSugerida`).
  - Tipos (`/api/catalogos/tipos`): crear (numero = mayor + 1, como Delphi) y modificar nombre (en mayusculas, sin
    repetir), siglas y personas. No se borran.
  - 75 pruebas.- Etapa 2: SERVICIOS HECHOS (2026-10-22). `/api/catalogos/servicios`: crear (numero mayor + 1, comparado como numero)
  y modificar descripcion (mayusculas), precio sin impuestos (se ve con ITBIS y con ITBIS y propina; se puede
  calcular desde un final) y si lleva ITBIS. No se borran. Marcados los que usa el sistema (1, 3, 6, 7, 9, 11).
  Hallazgo: STP_PasarAlHotel carga los consumos de la cafeteria con el servicio 6 (EARLY CHECK IN), no con el 7.
  El usuario no quiere la columna "Ultimo cambio" en las tablas (se quito de Servicios y Habitaciones).
  85 pruebas.  - Corregido con MIG_22 (Hotel\Sql\Migracion, aplicado en Hotel_new y HotelPruebas): la cafeteria carga con el servicio 7.
- Etapa 2: CLIENTES HECHOS y ETAPA 2 COMPLETA (2026-10-22), probada por el usuario.
  - Clientes (`/api/catalogos/clientes`, CxcCliente, la misma de cuentas por cobrar): busqueda en el servidor (nombre,
    RNC/cedula, numero; hasta 50), crear (numero mayor + 1 y los valores por omision de Delphi: cuenta de
    FaParametro.CuentaCatalogo_idCliente, libro 1, "30 Dias", Activo) y modificar solo las columnas del hotel. No se
    borran (se ponen inactivos; solo "Inactivo" es inactivo, como Delphi). RNC: con digito verificador obligatorio
    para E31/E44/E45 (`HotelWeb.Core/Identificacion/IdentificacionRD.cs`, copia de uIdentificacionRD.pas); en E32
    y para el correo solo avisa (hay datos viejos mal escritos). Avisa RNC repetido en otro cliente.
  - Dialogos: los formularios donde se escribe no se cierran al tocar afuera (`frontend/src/Fondo.tsx`); se perdia el
    trabajo al seleccionar texto y soltar el mouse fuera. El panel de consulta si, solo con clic completo en el fondo.
  - 111 pruebas.
- Siguiente: etapa 3, reservaciones.
## Etapa 3 (reservaciones): decisiones del usuario (2026-10-22), sin empezar a programar
- Reserva por TIPO como Delphi (HotelHabitacionReservada: Habitacion_id = tipo, NoHabitacion = 0; la habitacion se
  asigna en el check-in con STP_CrearHabitacionesReserva). Anular = EstatusReservacion_id 2 (no se borra).
- Sobreventa: avisar cuantas quedan libres del tipo en esas fechas, pero dejar guardar.
- Confirmacion impresa: despues.
- ADELANTOS: se registran ya desde la web (HotelReservaAdelanto: identidad, Fecha, Reserva_id, Cliente_id, Monto,
  Montous, Monedas_id = forma de pago de ConMonedas: 1 efectivo, 2 dolares, 3 cheque, 4 tarjeta, 8 deposito...,
  Usuario, Estatus 'Activa', Posteado).
  - VARIOS por reserva: hacer MIG_23 para que STP_CheckInHabitaciones y STP_CrearHabitacionesReserva sumen los
    adelantos ACTIVOS (hoy usan "Select Monto ... Where Reserva_id" que espera uno solo: con 2 o mas el check-in
    falla, y no miran el Estatus). Hay 6 reservas viejas con varios (3132 tiene 4). Stp_ImprimirFacturaFiscal ya suma.
  - Anular un adelanto: si, antes del check-in, usuario con permiso; Estatus 'Anulada' (no se borra).
- Pendiente: MIG_23 (probar en transaccion revertida, aplicar en Hotel_new y HotelPruebas), luego API y pantallas.- Etapa 3 en curso (2026-10-22): MIG_23 HECHO y aplicado (Hotel_new y HotelPruebas). API `/api/reservas` HECHA y
  probada contra Hotel_new (buscar por entrada/salida, ver, crear, modificar solo activas, cancelar con 409 si hay
  adelantos activos, disponibilidad por tipo, registrar adelanto, anular adelanto con Permisos:AnulanAdelantos).
  Pantallas Reservas y editor (cliente con busqueda, lineas por tipo con tarifa de la tabla de precios, libres por
  tipo, adelantos) HECHAS y probadas por el usuario. 126 pruebas.
  - Falta de la etapa 3: confirmacion impresa (decidido: despues).