using System.Data;
using Dapper;
using HotelWeb.Api.Catalogos;
using HotelWeb.Api.Hospedaje;
using HotelWeb.Core.Cafeteria;

namespace HotelWeb.Api.Cafeteria;

public sealed record HabitacionCafeteria(int Habitacion, long Factura, string? Huesped, DateTime? Salida, bool Credito);

public sealed record ResultadoCargo(long Factura, int Mesa, int Habitacion, string? Huesped, decimal SubTotal, decimal Total);

/// <summary>
/// El cargo de la cuenta de una mesa a la habitacion (etapa 8c), como Delphi (dbeHabitacionExit + UFrmPagar): sin e-NCF,
/// cliente 2 (FaParametro.CuentaClienteHabitacion), pago forma 8 por el SUBTOTAL y STP_PasarAlHotel, que agrega a la
/// cuenta la linea "CAFETERIA - FACTURA # n" (servicio 7, MIG_22): el ITBIS y la propina los pone el check-out del hotel.
/// Decision del usuario (2026-10-23): SOLO si la reserva tiene credito (Delphi avisaba pero dejaba cargar).
/// </summary>
public sealed partial class RepositorioCafeteria
{
    /// <summary>Las habitaciones ocupadas (cuenta abierta, no transferida) con su huesped y si tienen credito.</summary>
    public IReadOnlyList<HabitacionCafeteria> Habitaciones()
    {
        using var cn = _conexiones.Hotel();
        return Ocupadas(cn, null, null);
    }

    private static List<HabitacionCafeteria> Ocupadas(IDbConnection cn, IDbTransaction? tx, int? habitacion) =>
        cn.Query<(int Habitacion, decimal Factura, string? Huesped, DateTime? Salida, int? Credito)>("""
            SELECT a.Habitacion_id, a.Factura_id, RTRIM(r.NombreCliente), dbo.Fn_SalidaCuenta(a.Factura_id),
                   (SELECT MIN(CAST(b.Credito AS int)) FROM HotelHabitacionReservada b WHERE b.HabitacionReserva_id = a.HabitacionReserva_id)
            FROM HotelFactura a WITH (NOLOCK)
            LEFT JOIN HotelHabitacionReserva r WITH (NOLOCK) ON r.HabitacionReserva_id = a.HabitacionReserva_id
            WHERE ISNULL(a.Transferida, 0) = 0 AND (@habitacion IS NULL OR a.Habitacion_id = @habitacion)
            ORDER BY a.Habitacion_id
            """, new { habitacion }, tx)
            .Select(x => new HabitacionCafeteria(x.Habitacion, (long)x.Factura, x.Huesped, x.Salida, x.Credito == 1)).ToList();

    public ResultadoCargo CargarAHabitacion(int mesa, int habitacion, string usuario)
    {
        using var cn = _conexiones.Hotel();
        cn.Open();
        using var tx = cn.BeginTransaction();
        Bloquear(cn, tx);
        var f = FacturaDeMesa(cn, tx, mesa) ?? throw new ErrorCatalogo($"La mesa {mesa} no tiene cuenta abierta.");
        var lineas = LeerLineas(cn, tx, f).Select(x => x.Linea).ToList();
        if (!ReglasCafeteria.PreCuentaAlDia(lineas))
            throw new ErrorCatalogo(lineas.Count == 0 ? "La mesa no tiene nada que cargar." : "Saque la pre-cuenta de todo antes de cargar a la habitación.");

        var hs = Ocupadas(cn, tx, habitacion);
        if (hs.Count != 1) throw new ErrorCatalogo(hs.Count == 0 ? $"La habitación {habitacion} no está ocupada." : $"La habitación {habitacion} tiene más de una cuenta abierta.");
        var h = hs[0];
        if (!h.Credito) throw new ErrorCatalogo($"La habitación {habitacion} no tiene crédito en la cafetería: cobre la cuenta.");

        var cliente = cn.ExecuteScalar<int?>("SELECT TOP 1 CuentaClienteHabitacion FROM FaParametro", transaction: tx) ?? ReglasCobroCafeteria.ClienteHabitacion;
        var i = cn.QueryFirst<(bool Itbis, bool Propina, decimal PorcItbis, decimal PorcPropina)>("""
            SELECT ISNULL(c.Itbis, 1), ISNULL(c.Propina, 1), p.PorcItbis, p.PorcPropina
            FROM (SELECT TOP 1 * FROM FaParametro) p LEFT JOIN CxcCliente c ON c.NumeroCliente = @cliente
            """, new { cliente = (decimal)cliente }, tx);
        var t = ReglasCafeteria.Totales(lineas, i.Itbis, i.Propina, i.PorcItbis, i.PorcPropina);
        var u = U(usuario);
        var nombre = (h.Huesped ?? "").Trim();
        if (nombre.Length > 60) nombre = nombre[..60];

        cn.Execute("""
            UPDATE FaFactura SET NCF = NULL, Habitacion_id = @habitacion, NumeroCliente = @cliente, NombreCliente = @nombre, RncCliente = '00000000000',
                   Fecha = CAST(GETDATE() AS date), Subtotal = @SubTotal, Itbis = @Itbis, Propina = @Propina, Total = @Total,
                   Descuento = 0, PorcDescuento = 0, Cambio = 0
            WHERE InvFactura = @f
            """, new { habitacion, cliente = (decimal)cliente, nombre = A(nombre == "" ? "CLIENTE HABITACION" : nombre, 60), t.SubTotal, t.Itbis, t.Propina, t.Total, f }, tx);
        cn.Execute("DELETE FROM faFacturaPago WHERE InvFactura = @f", new { f }, tx);
        cn.Execute("""
            INSERT INTO faFacturaPago (InvFactura, Secuencia, FormaPago_id, FormadePago, MontoPago, Usuario, FechaAdicion)
            VALUES (@f, 1, @forma, 'CARGO HABITACION', @monto, @u, GETDATE())
            """, new { f, forma = ReglasCobroCafeteria.Habitacion, monto = t.SubTotal, u }, tx);
        var seccion = cn.ExecuteScalar<int>("SELECT faSeccion_id FROM FaFactura WHERE InvFactura = @f", new { f }, tx);
        cn.Execute("STP_PasarAlHotel", new { InvFactura = (decimal)f, Usuario = A(u.Value, 10), Habitacion = (decimal)habitacion, FaSeccion_id = seccion },
            tx, commandType: CommandType.StoredProcedure);
        if (cn.ExecuteScalar<int>("SELECT COUNT(*) FROM HotelFacturaDetalle WHERE Factura_id = @factura AND Descripcion = @d",
                new { factura = (decimal)h.Factura, d = A($"CAFETERIA - FACTURA # {f}", 50) }, tx) == 0)
            throw new ErrorCatalogo("No se pudo pasar el cargo a la cuenta de la habitación: intente de nuevo.");
        // Delphi no recalcula el encabezado de la cuenta del hotel; la web si, como cualquier cargo (RepositorioCuentas)
        RepositorioCuentas.Recalcular(cn, tx, habitacion);
        cn.Execute("Stp_TransferirCerrarFactura", new { InvFactura = (decimal)f }, tx, commandType: CommandType.StoredProcedure);
        if (cn.ExecuteScalar<int>("SELECT COUNT(*) FROM FaFactura WHERE InvFactura = @f", new { f }, tx) != 0)
            throw new ErrorCatalogo("No se pudo cerrar la cuenta: intente de nuevo.");
        tx.Commit();
        return new ResultadoCargo(f, mesa, habitacion, h.Huesped, t.SubTotal, t.Total);
    }
}
