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
| 9 | Caja y reportes | Cuadre, cambio de turno, ventas del periodo, secuencias, E43, E41; reportes de Delphi: habitaciones ocupadas, proyeccion de reservaciones, NCF de las facturas, facturas canceladas sin cobro, maestro de clientes | Si |
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
## Etapa 4 (hospedaje en curso): decisiones del usuario (2026-10-22)
- Orden: 1) check-in desde la reserva + carga del hospedaje de la noche; 2) cargar servicios a la cuenta;
  3) recibos de pago; 4) cambio de salida, de habitacion y transferencia.
- Check-in: sugerir habitaciones libres del TIPO reservado (Delphi: Fn_HabitacionDisponible no usa el tipo).
  HotelHabitacionReservadaTmp es UNA tabla para todos y STP_CrearHabitacionesReserva la vacia entera: la web
  llena solo las filas de su reserva y hace todo (Tmp + STP_CheckInHabitaciones) en una transaccion.
  Delphi exige cliente en la reserva para el check-in.
- Hospedaje de la noche: a mano por fecha (como "Cargar hospedaje" / STP_CargarHospedajeDiario) pero SIN repetir:
  en Hotel_new hay 320 cuentas con la misma noche cargada dos veces. Saltar tambien las cuentas transferidas.
- Recibos: en esta etapa, con los procedimientos de Delphi (STP_CARGARPAGOAHABITACION, Stp_AnularRecibodeIngreso,
  que escriben asientos en ConCabezeraDiario). Estudiarlos a fondo antes.- Etapa 4, parte 1 HECHA (2026-10-22): check-in y carga de la noche (`/api/hospedaje`). Check-in con
  STP_CheckInHabitaciones (Tmp solo de la reserva, todo en una transaccion serializable), sugiere libres del tipo,
  pide cliente si la reserva no tiene. Carga de la noche sin repetir, salta transferidas y sin tarifa, opcion de no
  cargar las de salida vencida. Probado contra Hotel_new (y limpiado). Pantalla Recepcion (Check-in | Cargar noche).
  Probada por el usuario. 133 pruebas.
  - Reserva: precios por tipo y categoria siempre a la vista (compactos, una linea; tocar uno lo usa), como Delphi.
- Siguiente de la etapa 4: cargar servicios a la cuenta.- Etapa 4, parte 2 HECHA (2026-10-22): cargos a la cuenta (`/api/cuentas/{habitacion}/cargos`, anular con
  Permisos:AnulanCargos). Recalcula el encabezado de HotelFactura con ITBIS y propina TRUNCADOS a 2 decimales como
  Delphi. No se cargan a mano los servicios 3, 7, 11; no se anulan pagos, adelantos, transferencias, cafeteria ni
  PAY-OUT. Desde el panel de la cuenta del rack. Probado contra Hotel_new (102 restaurada) y por el usuario.
  145 pruebas.- Etapa 4, parte 3 HECHA (2026-10-22): recibos de pago (`/api/cuentas/{habitacion}/recibos`, `/api/recibos/{n}/anular` con
  Permisos:AnulanRecibos). Como Delphi: HotelReciboIngreso + HotelReciboDetalle (una linea por forma de pago, todas a la
  habitacion del recibo; PAY-OUT en negativo), numero mayor + 1, STP_CARGARPAGOAHABITACION y recalculo del encabezado.
  Hallazgos: el "Anular" de Delphi llama a Stp_AnularRecibodeIngreso, que trabaja sobre IngRecibos (cuentas por
  cobrar) y NO anula el recibo del hotel (de 8048, ninguno anulado); crear un recibo no hace asiento: lo toma
  STP_CuadreDeCaja (Posteado = 0) sin mirar el estatus. Decision del usuario: anular = Estatus Anulado, el pago sale de
  la cuenta (lineas inactivas) y Posteado = 1 para que el cuadre no lo cuente; solo antes del cuadre y con la cuenta
  abierta. Probado contra Hotel_new (102 restaurada). Falta prueba del usuario. 155 pruebas.
- Falta de la etapa 4: cambio de salida, cambio de habitacion y transferencia a ocupada; impresion del recibo (despues).- Etapa 4, parte 4a HECHA (2026-10-23): cambio de salida (`/api/cuentas/{habitacion}/salida`). Delphi no lo tiene (la
  reserva hospedada no se edita; STP_CargarHospedajeDiario no mira la salida). Decision del usuario: salida POR
  HABITACION en una tabla nueva, MIG_24 (HotelCambioSalida + dbo.Fn_SalidaCuenta; Delphi no la usa). Si todas las
  cuentas abiertas de la reserva quedan con la misma salida, cambia tambien la reserva (FechaSalida, CantidadNoches,
  importes). Rack, cuenta, carga de la noche y disponibilidad usan Fn_SalidaCuenta. LA WEB REQUIERE MIG_24.
- Decision del usuario (2026-10-23): cambio a habitacion LIBRE de otro tipo = como el check-in (precio del tipo nuevo
  desde la proxima noche; "Respetar precio reservado" si la nueva es mas cara). Delphi solo cambia el numero.
- Etapa 4, parte 4b HECHA (2026-10-23): cambio a habitacion libre (`/api/cuentas/{habitacion}/cambio`). Mueve
  HotelFactura (numero, TipoHabitacion, CategoriaPrecio_id), sus recibos (HotelReciboIngreso.Habitacion; Delphi los
  dejaba en la vieja) y sus cambios de salida. Linea de la reserva: solo numero / actualizar (cantidad 1) / separar.
  Regla nueva (tambien en el check-in): no dos lineas de la reserva con el mismo tipo y categoria a distinta tarifa,
  porque STP_CargarHospedajeDiario toma la primera (TOP 1 sin orden). Sin linea de reserva: solo cambia el numero.
- Falta de la etapa 4: transferencia a ocupada (Stp_TransferiraOcupada; estudiar sus impuestos 1.018/1.010 y el
  0.28 del origen antes de usarla).
- Etapa 4, parte 4c HECHA (2026-10-23): transferencia a ocupada (`/api/cuentas/{habitacion}/transferencia`). NO usa
  Stp_TransferiraOcupada: pasa SUM(Importe) de todo (cargos sin impuestos mezclados con pagos con impuestos) y cierra el
  origen x1.28 solo si el cliente lleva ITBIS (solo propina o E44 quedaban con saldo y sin check-out). Decisiones del
  usuario: cargos en una linea servicio 9 (el destino les pone los impuestos de su cliente, como ya sale en el e-CF:
  E320000006067 los lleva gravados), PAY-OUT aparte, pagos y adelantos pasan como pagos (quedan inactivos en el origen);
  el origen queda en 0 y se cierra en el mismo paso con Stp_CHECKOUT (sin e-NCF). Todo en una transaccion.
- ETAPA 4 COMPLETA salvo la impresion del recibo (despues). Siguiente: etapa 5, checkout con e-CF.

## Etapa 5 (checkout con e-CF): decisiones del usuario (2026-10-23)
- Flujo: boton Check-out en la cuenta (balance 0, como Stp_CHECKOUT) -> tipo E31/E32/E44/E45 con las validaciones de
  Delphi ANTES de tomar el numero -> en UNA transaccion: e-NCF (STp_NumeroComprobanteFiscal_E, candado MIG_18) +
  HotelFactura.NCF/RNC + Stp_CHECKOUT -> XML (copia en C# de ArmarYEnviarEcf de UCreateXmlHotel.pas) en FacturaXml ->
  envio por la misma API (EnviarDesdeHotel; E32 < 250k: FirmarDesdeHotel + RFCE) -> respuesta; boton Reenviar si falla.
- PAY-OUT FUERA del e-CF (es reembolso, no venta). Delphi lo pone como item y no en los totales: los items no cuadran
  (latente: ninguno de los 25 e-CF cerrados en Hotel_new tiene PAY-OUT).
- Pruebas: se puede enviar por la API de este equipo (localhost:8005), que es de pruebas.
- Factura impresa: carta, como Delphi, con el QR (fc.dgii < 250k E32, ecf.dgii el resto).
- Etapa 5, parte 1 HECHA (2026-10-23): check-out con e-CF desde la cuenta (`/api/cuentas/{h}/checkout`), estado y
  reenvio (`/api/facturas/{n}/dgii`, `/enviar`) y factura impresa carta con QR (`/factura/{n}`, `/api/facturas/{n}`).
  XML en C# (HotelWeb.Core/Ecf/EcfHotel.cs) IGUAL byte a byte al de Delphi aceptado (prueba con E320000006067).
  El e-CF entero se valida ANTES de tomar el e-NCF. Ambiente del QR: Ecf:Ambiente (1 testecf en Development, 3 ecf).
  Probado contra testecf por localhost:8005: E320000006069 (RFCE) y E310000000237 (ECF) ACEPTADAS (facturas 9655 y
  9656 de Hotel_new; se dejan: borrar una factura con e-NCF podria repetir el numero).
- Decision del usuario (2026-10-23): en el check-out web basta la sesion (Delphi pide entrar otra vez). Vencimiento de la
  secuencia por TIPO (ComprobanteFiscal.FechaVencimientoEncf; Delphi toma siempre la del E31) e impreso en todos los
  tipos, E32 incluida (en el XML de la E32 no va). Espera a pantalla completa mientras se habla con la DGII.
- Falta de la etapa 5: lista de facturas cerradas con su estado en la DGII (reimprimir, reenviar).
- Etapa 5, parte 2 HECHA (2026-10-23): Recepcion > Facturas (`GET /api/facturas?desde&hasta&texto`): facturas cerradas
  con su estado en la DGII (DocumentosXml una vez por tipo para el rango; ECF con trackId de a 4), imprimir y reenviar.
  ETAPA 5 COMPLETA. Siguiente: etapa 6 (walk-in).

## Etapa 6 (walk-in): decisiones del usuario (2026-10-23)
- Como UfrmWalking de Delphi: nombre, cedula/RNC, comprobante E31/E32/E44/E45, servicios, pagos; ITBIS 18 % salvo E44;
  propina solo si "con propina". Todo en una transaccion (numero, e-NCF, encabezado, lineas, recibo con
  FacturaWalking_id) y despues el envio con el mismo armador del e-CF.
- Impresion: ticket POS 80 mm con el QR, como Delphi.
- Devuelta: se graba LO APLICADO (Delphi graba lo entregado: walk-in 314, 3,500 por 3,226.55, y STP_CuadreDeCaja suma
  HotelReciboDetalle.Monto: cuenta la devuelta como ingreso).
- Menu propio "Walk-in" en la barra.
- Etapa 6 HECHA (2026-10-23): menu Walk-in (`POST /api/walkins`, ticket `/walkin/{n}`, `/api/walkins/{n}/dgii|enviar`).
  Lo aplicado en el recibo y la devuelta en su concepto ("recibido X, devuelta Y"); la devuelta solo sale del efectivo.
  Los walk-in salen en Recepcion > Facturas (estado DGII por e-NCF). Probado en testecf: E320000006071 (walk-in 318,
  efectivo con devuelta) y E310000000238 (319, tarjeta, con propina) ACEPTADAS; se dejan en Hotel_new.

## Etapa 7 (notas de credito E34): decisiones del usuario (2026-10-23)
- Las dos de Delphi: anulacion total (codigo 1, Stp_AnularFacturaHotelNC, registro en HotelFacturaAnulada) y nota por
  monto (codigo 3, hasta el balance pendiente; como UFrmMntNCFrontDesk/NCWalking), de check-out y de walk-in.
- Autoriza un responsable con su usuario y clave (como PedirAutorizacion de Delphi) y el motivo es obligatorio.
- E33 (nota de debito): despues, si hace falta (Delphi no la tiene).
- Hallazgo: la web no escribia EcfEstadoDgii (lo llena el monitor de Delphi con sp_GuardarEstadoEcf) y
  Stp_AnularFacturaHotelNC lo exige: la web guarda ahi cada respuesta de la DGII.
- Etapa 7 HECHA (2026-10-23): notas de credito E34 desde Recepcion > Facturas (`/api/notas/{hospedaje|walkin}/...`,
  impresion `/nota/{origen}/{n}`). E34 en C# igual byte a byte a la de Delphi aceptada (E340000000001). Anulacion total
  con Stp_AnularFacturaHotelNC (HotelFacturaAnulada); nota por monto como SalvarNC (codigo 3). Responsable con
  Seg_Usuario.CancelaFactura. La web ahora escribe EcfEstadoDgii (sp_GuardarEstadoEcf). Probado en testecf:
  E340000000002 (50 al walk-in 318) y E340000000003 (anula la E310000000237, factura 9656) ACEPTADAS.
  Pendiente: la nota por monto no guarda quien autorizo en una tabla (Delphi no la autorizaba); queda en el log.

## Etapa 8 (cafeteria): estudio de Delphi y decisiones del usuario (2026-10-23)
- Delphi (RestaurantHotel\PrjCaja): mesas (faMesa, 42 en una seccion) -> cuenta abierta en FaFactura/FaFacturaDetalle
  (EstatusFactura_id 1); articulos por Stp_FacturarUnArticulo (precio por Fn_Precio1 segun el Precio_id del cliente;
  Precio2 = fraccion/trago, Tragos 2); articulo especial (referencia '00' alimentos art. 8, '**' bebidas art. 7);
  pre-cuenta (FaFacturaDetalle.Impreso = 1, FaFacturaHoraImpresion); cobro (FaFacturaPago, STP_PasarACXC para el
  credito, STP_PasarAlHotel, Stp_TransferirCerrarFactura -> FaTmpFactura) y e-CF (Comunes\uCreateXml.pas: el mismo XML
  que EcfHotel, comprobado con E310000000233 y E320000006062 aceptadas). Numero: Max(FaFactura, FaTmpFactura) + 1.
  Totales: ITBIS y propina sobre el subtotal, TRUNCADOS a 2 decimales (2236 de 2236 en 2026).
- Cargo a la habitacion: cliente 2 (FaParametro.CuentaClienteHabitacion), sin e-NCF, pago forma 8 por el SUBTOTAL;
  STP_PasarAlHotel lo agrega a la cuenta (servicio 7). Decision: SOLO si la reserva tiene credito
  (HotelHabitacionReservada.Credito); Delphi avisaba pero dejaba cargar.
- Fecha de la factura: la del dia (decision; Delphi usa FaParametro.FechaProceso).
- Descuento: no se ofrece por ahora (decision). En Delphi las lineas del e-CF no llevan el descuento y no cuadran.
- Pre-cuenta igual que Delphi: obligatoria antes de cobrar; lo impreso no cambia de cantidad ni precio.
- No se usan (2026): camareros (siempre 1 LOCAL), guarniciones, libras. Si se usan: fraccion (173 lineas desde
  2025-06), articulo especial (30), transferir mesa (Stp_TransferirMesas, FaLog).
- Partes: 8a mesas y cuenta abierta (pre-cuenta, mover de mesa); 8b cobro con e-CF y ticket; 8c cargo a la
  habitacion; 8d lista de facturas y notas E34 (faFacturaNC).
- Parte 8a HECHA (2026-10-23, sin commit): menu Cafeteria (`/api/cafeteria/...`): mesas (libre / abierta / pre-cuenta),
  cuenta abierta con articulos por codigo o familia (trago o botella), articulo especial, cantidades, borrar (lo impreso
  no se borra ni cambia; queda en FaLog), pre-cuenta 80 mm (`/precuenta/{factura}`, Impreso = 1 y FaFacturaHoraImpresion)
  y mover de mesa (libre: cambia Mesa_id; ocupada: une y cierra la vieja como Stp_TransferirMesas). Totales como Delphi:
  ITBIS y propina truncados y el total de los montos sin truncar (609.38 -> 780.00). Fecha: la del dia al abrir (al
  cobrar se pondra la del cobro). Probado en Hotel_new: cuentas 12792 (movida y unida) y 12793 (mesa 42, queda abierta).
- Parte 8b HECHA (2026-10-23, sin commit): Cobrar en la cuenta de la mesa (`POST /api/cafeteria/mesas/{m}/cobrar`,
  ticket `/cafeteria/{factura}`, `/api/cafeteria/facturas/{f}/dgii|enviar`). Una transaccion: cliente (contado o de CxC)
  y totales, e-NCF con todo validado antes, faFacturaPago con lo APLICADO (la devuelta en FaFactura.Cambio; el cambio de
  turno de Delphi suma MontoPago), STP_PasarACXC, Stp_TransferirCerrarFactura; despues el envio. Decision: credito solo
  a un cliente de CxC y por lo que falta (Delphi lo cargaba al cliente 1 y por mas). E44 sin ITBIS. Fecha = la del cobro.
  EstatusFactura_id queda en 1 en FaTmpFactura, como Delphi. El e-CF lleva el TOTAL grabado (FacturaEcf.TotalCobrado):
  XML igual byte a byte a E310000000233 y E320000006062 (+RFCE). Probado en testecf: E310000000239 (12795, tarjeta +
  efectivo con devuelta), E310000000240 (12796, credito a BANCO BHD 2027) y E320000006073 (12793, RFCE) ACEPTADAS.
- Parte 8c HECHA (2026-10-23, sin commit): "Cargar a habitacion" en el cobro (`GET /api/cafeteria/habitaciones`,
  `POST /api/cafeteria/mesas/{m}/habitacion`). Sin e-NCF; cliente FaParametro.CuentaClienteHabitacion (2), nombre del
  huesped, pago forma 8 por el SUBTOTAL, STP_PasarAlHotel (linea "CAFETERIA - FACTURA # n", servicio 7) y la web
  recalcula el encabezado de la cuenta del hotel (Delphi no). Solo habitaciones ocupadas, no transferidas y con credito
  (todas las lineas de la reserva con Credito = 1). Probado en Hotel_new: mesa 3 (factura 12797, 562.50) a la 102.
- Parte 8d HECHA (2026-10-23, sin commit): Cafeteria > Facturas (`GET /api/facturas?...&ambito=cafeteria`): lo cobrado
  (FaTmpFactura, sin las cuentas vacias de mesas unidas) y sus notas, con su estado en la DGII, ticket, reenvio y nota de
  credito E34 (`/api/notas/cafeteria/...`, impresion `/nota/cafeteria/{n}`). Por monto: como SalvarNC de la cafeteria
  (faFacturaNC, codigo 3). Anulacion total: Stp_AnularFacturaHotelNC no tiene la cafeteria; la web hace lo mismo en una
  transaccion (faFacturaNC codigo 1 por el total, sin ITBIS pasados 30 dias, y HotelFacturaAnulada con Origen
  'CAFETERIA'). La E34 de la cafeteria acredita el TOTAL cobrado (OriginalNota.TotalCobrado). Probado en testecf:
  E340000000005 (390 a la E310000000239) y E340000000006 (anula la E310000000240 de BANCO BHD) ACEPTADAS. Usuario
  temporal 9901 creado y BORRADO. ETAPA 8 COMPLETA (falta el commit de 8d).

## Etapa 9 (caja y reportes): decisiones del usuario (2026-10-23)
- Partes: 9a cuadre y cierre de caja; 9b reportes de ventas y comprobantes; 9c reportes del hospedaje (habitaciones
  ocupadas, proyeccion de reservaciones, maestro de clientes, canceladas sin cobro); 9d secuencias e-CF, E43 y E41.
- Delphi: el cuadre del hotel (UFrmMntCuadredeCaja + STP_CuadreDeCaja) toma TODOS los recibos y adelantos con
  Posteado = 0, resume por forma de pago y al "cerrar el turno" los marca Posteado = 1 (de todos los usuarios). El total
  sumaba los US$ con los pesos. La cafeteria tiene su "cambio de turno" (conteo por billetes, FaCambiodeTurno): casi no
  se usa (2 veces desde 2025, pruebas).
- Decisiones: dos cuadres (hotel y cafeteria); el cajero escribe lo contado (efectivo y dolares) y queda la diferencia
  (con diferencia, el comentario es obligatorio); cada cajero cierra SOLO lo suyo.
- MIG_25 (Hotel\Sql\Migracion): HotelCierreCaja + HotelCierreCajaDetalle (R recibo, A adelanto, F factura de la
  cafeteria). Aplicada en Hotel_new y HotelPruebas (2026-10-23). Falta la real.
- Hallazgo (etapa 3): la web grababa los adelantos con Posteado = 1 y no entraban en ningun cuadre; Delphi los deja en 0
  (valor por omision) y el cuadre los marca. Corregido: Posteado = 0; anular un adelanto sin cerrar lo marca 1.
- Parte 9a HECHA (2026-10-23, sin commit): Recepcion > Caja y Cafeteria > Caja (`/api/caja/{hotel|cafeteria}/...`),
  reporte carta `/cierre/{n}` con firmas. Hotel: recibos (incluye walk-in) y adelantos del usuario sin postear; PAY-OUT
  sale del efectivo; credito y notas de credito no son dinero; dolares aparte en US$. Cafeteria: lo que el usuario cobro
  (faFacturaPago) que no esta en un cierre, desde su primer cierre (o desde hoy). Si entra un cobro mientras cuenta, no
  cierra. Probado en Hotel_new: cierres 1 (cafeteria) y 2 (hotel, faltan 100 con comentario).
