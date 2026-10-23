using System.Data;
using Dapper;
using HotelWeb.Api.Ecf;
using HotelWeb.Core.Ecf;

namespace HotelWeb.Api.Cafeteria;

/// <summary>Las facturas cobradas de la cafeteria para su lista (etapa 8d).</summary>
public sealed partial class RepositorioCafeteria
{
    /// <summary>
    /// FaTmpFactura de un rango de fechas, las mas nuevas primero. Las cargadas a una habitacion no llevan e-CF (van como
    /// "transferidas": la factura la hace el hotel); las cuentas vacias de las mesas unidas (TipoVenta 3, total 0) no salen.
    /// Texto: numero, e-NCF, mesa, habitacion o parte del nombre.
    /// </summary>
    internal static List<(FacturaCerrada F, FacturaEcf E)> Lista(IDbConnection cn, DateTime desde, DateTime hasta, string? t) =>
        cn.Query<(int Numero, DateTime Fecha, int Mesa, int? Habitacion, string? Nombre, string? Ncf, string? Rnc, decimal SubTotal, decimal Itbis,
                decimal Propina, decimal Total)>("""
            SELECT TOP 500 f.InvFactura, f.Fecha, f.Mesa_id, NULLIF(f.Habitacion_id, 0), RTRIM(f.NombreCliente), RTRIM(f.NCF), RTRIM(f.RncCliente),
                   f.Subtotal, f.Itbis, f.Propina, f.Total
            FROM FaTmpFactura f WITH (NOLOCK)
            WHERE f.Fecha >= @desde AND f.Fecha < @hasta AND NOT (f.TipoVenta_id = 3 AND f.Total = 0)
              AND (@t IS NULL OR RTRIM(f.NCF) = @t OR CAST(f.InvFactura AS varchar(20)) = @t OR CAST(f.Mesa_id AS varchar(20)) = @t
                   OR CAST(f.Habitacion_id AS varchar(20)) = @t OR f.NombreCliente LIKE '%' + @t + '%')
            ORDER BY f.InvFactura DESC
            """, new { desde = desde.Date, hasta = hasta.Date.AddDays(1), t = new DbString { Value = t, IsAnsi = true, Length = 60 } })
            .Select(x =>
            {
                var e = new FacturaEcf(x.Ncf ?? "", x.Numero, x.Fecha.Date, x.Rnc, x.Nombre, x.SubTotal, x.Itbis, x.Propina, false,
                    Array.Empty<ItemEcf>(), x.Total);
                var aHabitacion = x.Habitacion is not null && string.IsNullOrEmpty(x.Ncf);
                return (new FacturaCerrada(x.Numero, x.Fecha, x.Habitacion ?? 0, x.Nombre, $"Mesa {x.Mesa}", x.Ncf, x.Total, aHabitacion, null,
                    false, "Cafeteria"), e);
            }).ToList();
}
