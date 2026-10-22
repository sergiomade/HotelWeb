namespace HotelWeb.Api.Rack;

/// <summary>SQL del rack (solo lectura). Mismas tablas y funcion de saldo que Delphi.</summary>
internal static class ConsultasRack
{
    /// <summary>
    /// Una fila por habitacion. Cuenta abierta = HotelFactura (si hubiera dos, la mas nueva). En las reservas,
    /// HotelHabitacionReservada.Habitacion_id es el TIPO y NoHabitacion el numero de la habitacion.
    /// </summary>
    public const string Habitaciones = """
        SELECT  h.Habitacion_id                         AS Numero,
                h.x                                     AS X,
                h.y                                     AS Y,
                CAST(h.Comentario AS varchar(500))      AS Comentario,
                RTRIM(t.Descripcion)                    AS Tipo,
                RTRIM(e.Descripcion)                    AS Estado,
                CAST(ISNULL(e.Disponible, 0) AS bit)    AS EstadoDisponible,
                f.Factura_id                            AS Factura,
                COALESCE(NULLIF(RTRIM(f.Huesped), ''), NULLIF(RTRIM(r.Huesped), ''), RTRIM(c.Nombre)) AS Huesped,
                RTRIM(c.Nombre)                         AS Cliente,
                r.FechaEntrada                          AS FechaEntrada,
                r.FechaSalida                           AS FechaSalida,
                d.Personas                              AS Personas,
                CAST(ISNULL(d.Credito, 0) AS bit)       AS Credito,
                CASE WHEN f.Factura_id IS NULL THEN NULL ELSE dbo.BalanceActualHabitacion(f.Factura_id) END AS Saldo,
                CAST(ISNULL(f.Transferida, 0) AS bit)   AS Transferida,
                ll.HabitacionReserva_id                 AS ReservaLlegada,
                RTRIM(ll.Huesped)                       AS HuespedLlegada,
                ll.FechaSalida                          AS SalidaLlegada
        FROM    hotelHabitacion h
        LEFT JOIN HotelHabitacionTipo   t ON t.HabitacionTipo = h.HabitacionTipo
        LEFT JOIN HotelHabitacionEstado e ON e.HabitacionEstado_id = h.HabitacionEstado_id
        OUTER APPLY (SELECT TOP 1 * FROM HotelFactura x WITH (NOLOCK)
                     WHERE x.Habitacion_id = h.Habitacion_id ORDER BY x.Factura_id DESC) f
        LEFT JOIN HotelHabitacionReserva r WITH (NOLOCK) ON r.HabitacionReserva_id = f.HabitacionReserva_id
        LEFT JOIN CxcCliente c WITH (NOLOCK) ON c.NumeroCliente = f.Cliente_id
        OUTER APPLY (SELECT TOP 1 ISNULL(z.Adultos, 0) + ISNULL(z.Ninos, 0) + ISNULL(z.Bebes, 0) AS Personas, z.Credito
                     FROM HotelHabitacionReservada z WITH (NOLOCK)
                     WHERE z.HabitacionReserva_id = f.HabitacionReserva_id AND z.NoHabitacion = h.Habitacion_id) d
        OUTER APPLY (SELECT TOP 1 a.HabitacionReserva_id, a.Huesped, a.FechaSalida
                     FROM HotelHabitacionReserva a WITH (NOLOCK)
                     JOIN HotelHabitacionReservada b WITH (NOLOCK) ON b.HabitacionReserva_id = a.HabitacionReserva_id
                     WHERE a.EstatusReservacion_id = 1 AND b.NoHabitacion = h.Habitacion_id
                       AND CAST(a.FechaEntrada AS date) = @hoy
                     ORDER BY a.HabitacionReserva_id) ll
        ORDER BY h.Habitacion_id
        """;

    /// <summary>Encabezado de la cuenta abierta de una habitacion, con las reglas de impuestos del cliente.</summary>
    public const string Cuenta = """
        SELECT TOP 1
                f.Factura_id                            AS Factura,
                f.Habitacion_id                         AS Habitacion,
                COALESCE(NULLIF(RTRIM(f.Huesped), ''), NULLIF(RTRIM(r.Huesped), ''), RTRIM(c.Nombre)) AS Huesped,
                f.Cliente_id                            AS ClienteId,
                RTRIM(c.Nombre)                         AS Cliente,
                RTRIM(COALESCE(NULLIF(RTRIM(f.RNC), ''), c.RNC)) AS Rnc,
                CAST(ISNULL(c.Itbis, 0) AS bit)         AS LlevaItbis,
                CAST(ISNULL(c.Propina, 0) AS bit)       AS LlevaPropina,
                CAST(CASE WHEN c.CodigoCompFiscal = 13 THEN 1 ELSE 0 END AS bit) AS RegimenEspecial,
                r.HabitacionReserva_id                  AS Reserva,
                r.FechaEntrada                          AS FechaEntrada,
                r.FechaSalida                           AS FechaSalida,
                RTRIM(f.CategoriaPrecio_id)             AS Categoria,
                CAST(ISNULL(f.Transferida, 0) AS bit)   AS Transferida,
                dbo.BalanceActualHabitacion(f.Factura_id) AS Saldo
        FROM    HotelFactura f WITH (NOLOCK)
        LEFT JOIN HotelHabitacionReserva r WITH (NOLOCK) ON r.HabitacionReserva_id = f.HabitacionReserva_id
        LEFT JOIN CxcCliente c WITH (NOLOCK) ON c.NumeroCliente = f.Cliente_id
        WHERE   f.Habitacion_id = @habitacion
        ORDER BY f.Factura_id DESC
        """;

    public const string Lineas = """
        SELECT  Secuencia, RTRIM(Descripcion) AS Descripcion, Cantidad, Precio, Importe,
                CAST(ISNULL(Activo, 0) AS bit) AS Activo, RTRIM(Usuario) AS Usuario, FechaAdicion AS Fecha
        FROM    HotelFacturaDetalle WITH (NOLOCK)
        WHERE   Factura_id = @factura
        ORDER BY Secuencia
        """;
}
