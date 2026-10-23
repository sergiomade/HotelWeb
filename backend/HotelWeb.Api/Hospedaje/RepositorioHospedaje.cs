using System.Data;
using Dapper;
using HotelWeb.Api.Datos;
using HotelWeb.Core.Hospedaje;

namespace HotelWeb.Api.Hospedaje;

public enum EstadoNoche { PorCargar, YaCargada, Transferida, SinTarifa }

public sealed record NocheCuenta(int Habitacion, long Factura, string? Huesped, decimal Tarifa, DateTime? Salida,
    EstadoNoche Estado, bool SalidaVencida);

public sealed record CargaNoche(DateTime Noche, string Descripcion, IReadOnlyList<NocheCuenta> Cuentas)
{
    public int PorCargar => Cuentas.Count(c => c.Estado == EstadoNoche.PorCargar);
    public decimal Total => Cuentas.Where(c => c.Estado == EstadoNoche.PorCargar).Sum(c => c.Tarifa);
}

/// <summary>
/// Hospedaje de una noche a cada cuenta abierta, como "Cargar hospedaje" de Delphi (STP_CargarHospedajeDiario): servicio 1,
/// "HOSPEDAJE -> d/m/aaaa", la tarifa de la linea de la reserva (mismo tipo y categoria de la cuenta). Pero SIN repetir:
/// salta las cuentas que ya tienen esa noche (en Hotel_new hay 320 con una noche dos veces) y las transferidas.
/// </summary>
public sealed class RepositorioHospedaje
{
    private readonly Conexiones _conexiones;

    public RepositorioHospedaje(Conexiones conexiones) => _conexiones = conexiones;

    private sealed class Fila
    {
        public decimal Habitacion_id { get; set; }
        public decimal Factura_id { get; set; }
        public string? Huesped { get; set; }
        public decimal Tarifa { get; set; }
        public DateTime? FechaSalida { get; set; }
        public bool Transferida { get; set; }
        public int YaCargada { get; set; }
    }

    // la tarifa: la misma subconsulta de STP_CargarHospedajeDiario
    private const string Consulta = """
        SELECT a.Habitacion_id, a.Factura_id,
               COALESCE(NULLIF(RTRIM(a.Huesped), ''), NULLIF(RTRIM(r.Huesped), ''), RTRIM(c.Nombre)) AS Huesped,
               ISNULL((SELECT TOP 1 x.Tarifa FROM HotelHabitacionReservada x WITH (NOLOCK)
                        WHERE x.HabitacionReserva_id = a.HabitacionReserva_id AND x.CategoriaPrecio_id = a.CategoriaPrecio_id
                          AND x.Habitacion_id = a.TipoHabitacion), 0) AS Tarifa,
               r.FechaSalida, CAST(ISNULL(a.Transferida, 0) AS bit) AS Transferida,
               (SELECT COUNT(*) FROM HotelFacturaDetalle d {0}
                 WHERE d.Factura_id = a.Factura_id AND d.Descripcion = @texto) AS YaCargada
        FROM HotelFactura a {0}
        LEFT JOIN HotelHabitacionReserva r WITH (NOLOCK) ON r.HabitacionReserva_id = a.HabitacionReserva_id
        LEFT JOIN CxcCliente c WITH (NOLOCK) ON c.NumeroCliente = a.Cliente_id
        ORDER BY a.Habitacion_id
        """;

    public CargaNoche Ver(DateTime noche)
    {
        using var cn = _conexiones.Hotel();
        return Leer(cn, null, noche);
    }

    private static CargaNoche Leer(IDbConnection cn, IDbTransaction? tx, DateTime noche)
    {
        var texto = ReglasHospedaje.Descripcion(noche);
        var bloqueo = tx is null ? "WITH (NOLOCK)" : "WITH (UPDLOCK, HOLDLOCK)";
        var cuentas = cn.Query<Fila>(string.Format(Consulta, bloqueo), new { texto = new DbString { Value = texto, IsAnsi = true, Length = 50 } }, tx)
            .Select(f => new NocheCuenta((int)f.Habitacion_id, (long)f.Factura_id, f.Huesped, f.Tarifa, f.FechaSalida,
                f.YaCargada > 0 ? EstadoNoche.YaCargada
                : f.Transferida ? EstadoNoche.Transferida
                : f.Tarifa <= 0 ? EstadoNoche.SinTarifa
                : EstadoNoche.PorCargar,
                f.FechaSalida is { } s && s.Date <= noche.Date)).ToList();
        return new CargaNoche(noche.Date, texto, cuentas);
    }

    /// <summary>Carga la noche a las cuentas que la tienen pendiente. Devuelve como quedo todo.</summary>
    public (CargaNoche Despues, int Cargadas) Cargar(DateTime noche, string usuario, bool sinVencidas = false)
    {
        using var cn = _conexiones.Hotel();
        cn.Open();
        using var tx = cn.BeginTransaction(IsolationLevel.Serializable);
        // dos personas cargando la misma noche desde la web: una espera a la otra
        cn.Execute("EXEC sp_getapplock @Resource = 'HotelWeb_Hospedaje', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 20000",
            transaction: tx);
        var antes = Leer(cn, tx, noche);
        var cargadas = 0;
        // sinVencidas: no carga las cuentas cuya salida ya paso (el huesped debio haberse ido; Delphi las carga igual)
        foreach (var c in antes.Cuentas.Where(x => x.Estado == EstadoNoche.PorCargar && !(sinVencidas && x.SalidaVencida)))
        {
            // mismos valores que STP_CargarHospedajeDiario: (factura, sec, 1, texto, 1, tarifa, tarifa, 1, usuario, ahora, 1, 1)
            cn.Execute("""
                INSERT INTO HotelFacturaDetalle (Factura_id, Secuencia, Servicio_id, Descripcion, Cantidad, Precio, Importe, Activo,
                                                 Usuario, fechaadicion, referencia, Impreso)
                SELECT @factura, ISNULL(MAX(Secuencia), 0) + 1, '1', @texto, 1, @tarifa, @tarifa, 1, @usuario, GETDATE(), '1', 1
                FROM HotelFacturaDetalle WHERE Factura_id = @factura
                """, new
            {
                factura = c.Factura, tarifa = c.Tarifa,
                texto = new DbString { Value = antes.Descripcion, IsAnsi = true, Length = 50 },
                usuario = new DbString { Value = usuario.Length > 10 ? usuario[..10] : usuario, IsAnsi = true, Length = 10 },
            }, tx);
            cargadas++;
        }
        var despues = Leer(cn, tx, noche);
        tx.Commit();
        return (despues, cargadas);
    }
}
