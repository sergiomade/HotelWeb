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
