using System.Data;
using Dapper;
using HotelWeb.Api.Catalogos;
using HotelWeb.Api.Datos;
using HotelWeb.Core.Hospedaje;
using HotelWeb.Core.Reservas;

namespace HotelWeb.Api.Hospedaje;

public sealed record CambioSalida(DateTime Fecha, int Habitacion, DateTime? Anterior, DateTime Nueva, string? Comentario, string? Usuario);

public sealed record OtraHabitacion(int Habitacion, DateTime Salida);

/// <summary>Lo que necesita la pantalla para cambiar la salida de una habitacion.</summary>
public sealed record SalidaCuenta(int Habitacion, long Factura, long? Reserva, DateTime Entrada, DateTime Salida,
    DateTime? SalidaReserva, bool Transferida, IReadOnlyList<OtraHabitacion> Otras, IReadOnlyList<CambioSalida> Cambios);

public sealed record ResultadoSalida(SalidaCuenta Salida, bool CambioLaReserva, IReadOnlyList<string> Avisos);

/// <summary>
/// Cambio de la fecha de salida de una habitacion hospedada (MIG_24: HotelCambioSalida + Fn_SalidaCuenta). Delphi no lo
/// tiene. Si todas las cuentas abiertas de la reserva quedan con la misma salida, tambien cambia la reserva
/// (FechaSalida, CantidadNoches e importes de las lineas), para que los reportes de Delphi la vean.
/// </summary>
public sealed class RepositorioSalidas
{
    private readonly Conexiones _conexiones;

    public RepositorioSalidas(Conexiones conexiones) => _conexiones = conexiones;

    private static DbString A(string? v, int largo) => new() { Value = v, IsAnsi = true, Length = largo };

    private sealed class FilaCuenta
    {
        public decimal Factura_id { get; set; }
        public decimal Habitacion_id { get; set; }
        public decimal? HabitacionReserva_id { get; set; }
        public bool? Transferida { get; set; }
        public DateTime? FechaEntrada { get; set; }
        public DateTime? FechaSalidaReserva { get; set; }
        public DateTime? Salida { get; set; }
        public DateTime Fecha { get; set; }
    }

    private const string SelCuenta = """
        SELECT f.Factura_id, f.Habitacion_id, f.HabitacionReserva_id, f.Transferida, r.FechaEntrada,
               r.FechaSalida AS FechaSalidaReserva, dbo.Fn_SalidaCuenta(f.Factura_id) AS Salida, f.Fecha
        FROM HotelFactura f {0}
        LEFT JOIN HotelHabitacionReserva r ON r.HabitacionReserva_id = f.HabitacionReserva_id
        """;

    private static FilaCuenta Cuenta(IDbConnection cn, IDbTransaction? tx, int habitacion) =>
        cn.QueryFirstOrDefault<FilaCuenta>(string.Format(SelCuenta, tx is null ? "WITH (NOLOCK)" : "WITH (UPDLOCK)")
            + " WHERE f.Habitacion_id = @habitacion ORDER BY f.Factura_id DESC", new { habitacion }, tx)
        ?? throw new ErrorCatalogo($"La habitacion {habitacion} no tiene cuenta abierta.");

    private static DateTime Entrada(FilaCuenta c) => (c.FechaEntrada ?? c.Fecha).Date;

    public SalidaCuenta Ver(int habitacion)
    {
        using var cn = _conexiones.Hotel();
        return Ver(cn, null, Cuenta(cn, null, habitacion));
    }

    private static SalidaCuenta Ver(IDbConnection cn, IDbTransaction? tx, FilaCuenta c)
    {
        var otras = c.HabitacionReserva_id is null
            ? new List<OtraHabitacion>()
            : cn.Query<(decimal Hab, DateTime? Salida)>("""
                SELECT Habitacion_id, dbo.Fn_SalidaCuenta(Factura_id) FROM HotelFactura
                WHERE HabitacionReserva_id = @reserva AND Factura_id <> @factura AND ISNULL(Transferida, 0) = 0
                ORDER BY Habitacion_id
                """, new { reserva = c.HabitacionReserva_id, factura = c.Factura_id }, tx)
                .Where(o => o.Salida is not null).Select(o => new OtraHabitacion((int)o.Hab, o.Salida!.Value)).ToList();
        var cambios = cn.Query<(DateTime Fecha, decimal Hab, DateTime? Anterior, DateTime Nueva, string? Comentario, string? Usuario)>("""
            SELECT FechaAdicion, Habitacion_id, SalidaAnterior, SalidaNueva, RTRIM(Comentario), RTRIM(Usuario)
            FROM HotelCambioSalida WHERE Factura_id = @factura ORDER BY Cambio_id DESC
            """, new { factura = c.Factura_id }, tx)
            .Select(x => new CambioSalida(x.Fecha, (int)x.Hab, x.Anterior, x.Nueva, x.Comentario, x.Usuario)).ToList();
        return new SalidaCuenta((int)c.Habitacion_id, (long)c.Factura_id, (long?)c.HabitacionReserva_id, Entrada(c),
            (c.Salida ?? Entrada(c).AddDays(1)).Date, c.FechaSalidaReserva, c.Transferida == true, otras, cambios);
    }

    /// <param name="todas">Tambien las demas habitaciones abiertas de la reserva.</param>
    public ResultadoSalida Cambiar(int habitacion, DateTime nueva, bool todas, string? comentario, string usuario, DateTime hoy)
    {
        using var cn = _conexiones.Hotel();
        cn.Open();
        using var tx = cn.BeginTransaction(IsolationLevel.Serializable);
        var c = Cuenta(cn, tx, habitacion);
        if (ReglasSalida.Validar(nueva, Entrada(c), hoy, c.Salida, c.Transferida == true) is { } motivo) throw new ErrorCatalogo(motivo);

        // las cuentas que cambian: esta, y si se pidio, las demas abiertas (no transferidas) de la reserva
        var cuentas = new List<FilaCuenta> { c };
        if (todas && c.HabitacionReserva_id is not null)
            cuentas.AddRange(cn.Query<FilaCuenta>(string.Format(SelCuenta, "WITH (UPDLOCK)")
                    + " WHERE f.HabitacionReserva_id = @reserva AND f.Factura_id <> @factura AND ISNULL(f.Transferida, 0) = 0",
                    new { reserva = c.HabitacionReserva_id, factura = c.Factura_id }, tx)
                .Where(o => o.Salida?.Date != nueva.Date && nueva.Date > Entrada(o)));

        var avisos = new List<string>();
        var nota = string.IsNullOrWhiteSpace(comentario) ? null : comentario.Trim();
        foreach (var x in cuentas)
        {
            cn.Execute("""
                INSERT INTO HotelCambioSalida (Factura_id, Habitacion_id, HabitacionReserva_id, SalidaAnterior, SalidaNueva, Comentario, Usuario)
                VALUES (@factura, @habitacion, @reserva, @anterior, @nueva, @comentario, @usuario)
                """, new
            {
                factura = x.Factura_id, habitacion = x.Habitacion_id, reserva = x.HabitacionReserva_id, anterior = x.Salida,
                nueva = nueva.Date, comentario = A(nota is { Length: > 200 } n ? n[..200] : nota, 200),
                usuario = A(usuario.Length > 10 ? usuario[..10] : usuario, 10),
            }, tx);
            var cargadas = cn.ExecuteScalar<int>("""
                SELECT COUNT(*) FROM HotelFacturaDetalle
                WHERE Factura_id = @factura AND Activo = 1 AND RTRIM(Servicio_id) = '1' AND Descripcion LIKE 'HOSPEDAJE ->%'
                """, new { factura = x.Factura_id }, tx);
            if (ReglasSalida.Aviso(nueva, cargadas, Entrada(x)) is { } a) avisos.Add($"Habitacion {(int)x.Habitacion_id}: {a}");
        }

        // si todas las cuentas abiertas de la reserva quedan con la misma salida, la reserva tambien
        var cambioReserva = false;
        if (c.HabitacionReserva_id is { } reserva)
        {
            var salidas = cn.Query<DateTime?>("""
                SELECT dbo.Fn_SalidaCuenta(Factura_id) FROM HotelFactura
                WHERE HabitacionReserva_id = @reserva AND ISNULL(Transferida, 0) = 0
                """, new { reserva }, tx).Where(s => s is not null).Select(s => s!.Value);
            if (ReglasSalida.SalidaDeLaReserva(salidas) is { } unica && unica != c.FechaSalidaReserva?.Date && c.FechaEntrada is { } entrada)
            {
                var noches = ReglasReserva.Noches(entrada, unica);
                cn.Execute("""
                    UPDATE HotelHabitacionReserva SET FechaSalida = @unica, CantidadNoches = @noches WHERE HabitacionReserva_id = @reserva;
                    UPDATE HotelHabitacionReservada SET Importe = ROUND(Cantidad * Tarifa * @noches, 2) WHERE HabitacionReserva_id = @reserva;
                    """, new { unica, noches, reserva }, tx);
                cambioReserva = true;
            }
        }

        var vista = Ver(cn, tx, Cuenta(cn, tx, habitacion));
        tx.Commit();
        return new ResultadoSalida(vista, cambioReserva, avisos);
    }
}
