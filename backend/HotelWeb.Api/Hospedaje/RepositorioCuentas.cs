using System.Data;
using Dapper;
using HotelWeb.Api.Catalogos;
using HotelWeb.Api.Datos;
using HotelWeb.Api.Rack;
using HotelWeb.Core.Hospedaje;
using HotelWeb.Core.Rack;

namespace HotelWeb.Api.Hospedaje;

/// <summary>
/// Cargos a la cuenta abierta de una habitacion (HotelFactura + HotelFacturaDetalle), como la pantalla de la factura de
/// Delphi: agregar un servicio y anular un cargo. Despues de cada cambio se recalcula el encabezado (SubTotal, ITBIS,
/// propina, Total), que es lo que usa el check-out.
/// </summary>
public sealed class RepositorioCuentas
{
    private readonly Conexiones _conexiones;
    private readonly RepositorioRack _rack;

    public RepositorioCuentas(Conexiones conexiones, RepositorioRack rack)
    {
        _conexiones = conexiones;
        _rack = rack;
    }

    private sealed class Encabezado
    {
        public decimal Factura_id { get; set; }
        public bool Transferida { get; set; }
        public bool Itbis { get; set; }
        public bool Propina { get; set; }
        public bool RegimenEspecial { get; set; }
    }

    private static Encabezado Cuenta(IDbConnection cn, IDbTransaction tx, int habitacion) =>
        cn.QueryFirstOrDefault<Encabezado>("""
            SELECT TOP 1 f.Factura_id, CAST(ISNULL(f.Transferida, 0) AS bit) AS Transferida,
                   CAST(ISNULL(c.Itbis, 0) AS bit) AS Itbis, CAST(ISNULL(c.Propina, 0) AS bit) AS Propina,
                   CAST(CASE WHEN c.CodigoCompFiscal = 13 THEN 1 ELSE 0 END AS bit) AS RegimenEspecial
            FROM HotelFactura f WITH (UPDLOCK)
            LEFT JOIN CxcCliente c ON c.NumeroCliente = f.Cliente_id
            WHERE f.Habitacion_id = @habitacion ORDER BY f.Factura_id DESC
            """, new { habitacion }, tx) ?? throw new ErrorCatalogo($"La habitacion {habitacion} no tiene cuenta abierta.");

    private sealed class FilaLinea
    {
        public int Secuencia { get; set; }
        public string? Descripcion { get; set; }
        public decimal Cantidad { get; set; }
        public decimal Precio { get; set; }
        public decimal Importe { get; set; }
        public bool Activo { get; set; }
    }

    private static List<LineaCuenta> Lineas(IDbConnection cn, IDbTransaction tx, decimal factura) =>
        cn.Query<FilaLinea>("""
            SELECT Secuencia, RTRIM(Descripcion) AS Descripcion, Cantidad, Precio, Importe, CAST(ISNULL(Activo, 0) AS bit) AS Activo
            FROM HotelFacturaDetalle WITH (UPDLOCK) WHERE Factura_id = @factura
            """, new { factura }, tx)
            .Select(l => new LineaCuenta(l.Secuencia, l.Descripcion ?? "", l.Cantidad, l.Precio, l.Importe, l.Activo, null, null)).ToList();

    /// <summary>El encabezado de la cuenta con sus lineas, como CalcularTotales + Salvar de Delphi.</summary>
    private static void Recalcular(IDbConnection cn, IDbTransaction tx, Encabezado e)
    {
        var t = ReglasCargo.Encabezado(Lineas(cn, tx, e.Factura_id), e.Itbis, e.Propina, e.RegimenEspecial);
        cn.Execute("UPDATE HotelFactura SET SubTotal = @SubTotal, Itbis = @Itbis, Propina = @Propina, Total = @Total WHERE Factura_id = @factura",
            new { t.SubTotal, t.Itbis, t.Propina, t.Total, factura = e.Factura_id }, tx);
    }

    public CuentaHabitacion Cargar(int habitacion, NuevoCargo cargo, string usuario)
    {
        var c = ReglasCargo.Normalizar(cargo);
        using (var cn = _conexiones.Hotel())
        {
            cn.Open();
            using var tx = cn.BeginTransaction();
            var e = Cuenta(cn, tx, habitacion);
            if (ReglasCargo.Validar(c, e.Transferida) is { } motivo) throw new ErrorCatalogo(motivo);
            if (cn.ExecuteScalar<int>("SELECT COUNT(*) FROM HotelServicioAdicional WHERE LTRIM(RTRIM(Servicio_id)) = @s",
                    new { s = c.Servicio.ToString() }, tx) == 0)
                throw new ErrorCatalogo($"No existe el servicio {c.Servicio}.");
            // como FacturaDetalleNewRecord + SpeedButton1Click de Delphi: activo, usuario, ahora; sin referencia ni impreso
            cn.Execute("""
                INSERT INTO HotelFacturaDetalle (Factura_id, Secuencia, Servicio_id, Descripcion, Cantidad, Precio, Importe, Activo,
                                                 Usuario, fechaadicion)
                SELECT @factura, ISNULL(MAX(Secuencia), 0) + 1, @servicio, @descripcion, @cantidad, @precio, @importe, 1, @usuario, GETDATE()
                FROM HotelFacturaDetalle WHERE Factura_id = @factura
                """, new
            {
                factura = e.Factura_id,
                servicio = new DbString { Value = c.Servicio.ToString(), IsAnsi = true, Length = 4 },
                descripcion = new DbString { Value = c.Descripcion, IsAnsi = true, Length = 50 },
                cantidad = c.Cantidad, precio = c.Precio, importe = c.Importe,
                usuario = new DbString { Value = usuario.Length > 10 ? usuario[..10] : usuario, IsAnsi = true, Length = 10 },
            }, tx);
            Recalcular(cn, tx, e);
            tx.Commit();
        }
        return _rack.Cuenta(habitacion)!;
    }

    public CuentaHabitacion Anular(int habitacion, int secuencia)
    {
        using (var cn = _conexiones.Hotel())
        {
            cn.Open();
            using var tx = cn.BeginTransaction();
            var e = Cuenta(cn, tx, habitacion);
            var l = Lineas(cn, tx, e.Factura_id).FirstOrDefault(x => x.Secuencia == secuencia)
                    ?? throw new ErrorCatalogo($"La cuenta no tiene la linea {secuencia}.");
            if (ReglasCargo.ValidarAnular(l) is { } motivo) throw new ErrorCatalogo(motivo);
            // como desmarcar Activo en Delphi (CalcularImporte pone el importe en 0)
            cn.Execute("UPDATE HotelFacturaDetalle SET Activo = 0, Importe = 0 WHERE Factura_id = @factura AND Secuencia = @secuencia",
                new { factura = e.Factura_id, secuencia }, tx);
            Recalcular(cn, tx, e);
            tx.Commit();
        }
        return _rack.Cuenta(habitacion)!;
    }
}
