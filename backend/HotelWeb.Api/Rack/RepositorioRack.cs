using Dapper;
using HotelWeb.Api.Datos;
using HotelWeb.Core.Rack;

namespace HotelWeb.Api.Rack;

public sealed record HabitacionRack(
    int Numero, int Piso, int X, int Y, string? Tipo, string? Estado, string? Comentario,
    Situacion Situacion, Avisos Avisos,
    long? Factura, string? Huesped, string? Cliente, DateTime? FechaEntrada, DateTime? FechaSalida,
    int? Personas, bool Credito, decimal? Saldo, bool Transferida,
    Llegada? Llegada);

public sealed record Llegada(long Reserva, string? Huesped, DateTime? FechaSalida);

public sealed record RackHoy(DateTime Hoy, IReadOnlyList<HabitacionRack> Habitaciones);

public sealed record CuentaHabitacion(
    long Factura, int Habitacion, string? Huesped, long? ClienteId, string? Cliente, string? Rnc,
    bool LlevaItbis, bool LlevaPropina, bool RegimenEspecial,
    long? Reserva, DateTime? FechaEntrada, DateTime? FechaSalida, string? Categoria, bool Transferida,
    decimal Saldo, Totales Desglose, IReadOnlyList<LineaCuenta> Lineas);

/// <summary>Lecturas del rack sobre las tablas de Delphi. No escribe nada.</summary>
public sealed class RepositorioRack
{
    private readonly Conexiones _conexiones;

    public RepositorioRack(Conexiones conexiones) => _conexiones = conexiones;

    // Filas como clases con propiedades (no records posicionales): Dapper convierte int/numeric/decimal sin exigir
    // que el tipo de cada columna coincida exacto con el del constructor.
    private sealed class FilaRack
    {
        public decimal Numero { get; set; }
        public int? X { get; set; }
        public int? Y { get; set; }
        public string? Comentario { get; set; }
        public string? Tipo { get; set; }
        public string? Estado { get; set; }
        public bool EstadoDisponible { get; set; }
        public decimal? Factura { get; set; }
        public string? Huesped { get; set; }
        public string? Cliente { get; set; }
        public DateTime? FechaEntrada { get; set; }
        public DateTime? FechaSalida { get; set; }
        public int? Personas { get; set; }
        public bool Credito { get; set; }
        public decimal? Saldo { get; set; }
        public bool Transferida { get; set; }
        public decimal? ReservaLlegada { get; set; }
        public string? HuespedLlegada { get; set; }
        public DateTime? SalidaLlegada { get; set; }
    }

    public RackHoy Obtener(DateTime ahora)
    {
        using var cn = _conexiones.Hotel();
        var filas = cn.Query<FilaRack>(ConsultasRack.Habitaciones, new { hoy = ahora.Date });
        var lista = filas.Select(f =>
        {
            var numero = (int)f.Numero;
            var datos = new DatosHabitacion(numero, f.EstadoDisponible, f.Factura is not null, f.FechaSalida,
                f.ReservaLlegada is not null);
            return new HabitacionRack(
                numero, ReglaRack.PisoDe(numero), f.X ?? 0, f.Y ?? 0, f.Tipo, f.Estado, Limpio(f.Comentario),
                ReglaRack.SituacionDe(datos), ReglaRack.AvisosDe(datos, ahora),
                (long?)f.Factura, f.Huesped, f.Cliente, f.FechaEntrada, f.FechaSalida, f.Personas, f.Credito, f.Saldo,
                f.Transferida,
                f.ReservaLlegada is { } r ? new Llegada((long)r, f.HuespedLlegada, f.SalidaLlegada) : null);
        }).ToList();
        return new RackHoy(ahora, lista);
    }

    private sealed class FilaCuenta
    {
        public decimal Factura { get; set; }
        public decimal Habitacion { get; set; }
        public string? Huesped { get; set; }
        public decimal? ClienteId { get; set; }
        public string? Cliente { get; set; }
        public string? Rnc { get; set; }
        public bool LlevaItbis { get; set; }
        public bool LlevaPropina { get; set; }
        public bool RegimenEspecial { get; set; }
        public decimal? Reserva { get; set; }
        public DateTime? FechaEntrada { get; set; }
        public DateTime? FechaSalida { get; set; }
        public string? Categoria { get; set; }
        public bool Transferida { get; set; }
        public decimal Saldo { get; set; }
    }

    private sealed class FilaLinea
    {
        public int? Secuencia { get; set; }
        public string? Descripcion { get; set; }
        public decimal? Cantidad { get; set; }
        public decimal? Precio { get; set; }
        public decimal? Importe { get; set; }
        public bool Activo { get; set; }
        public string? Usuario { get; set; }
        public DateTime? Fecha { get; set; }
    }

    /// <summary>La cuenta abierta de la habitacion, o null si esta libre.</summary>
    public CuentaHabitacion? Cuenta(int habitacion)
    {
        using var cn = _conexiones.Hotel();
        var c = cn.QueryFirstOrDefault<FilaCuenta>(ConsultasRack.Cuenta, new { habitacion });
        if (c is null) return null;

        var lineas = cn.Query<FilaLinea>(ConsultasRack.Lineas, new { factura = c.Factura })
            .Select(l => new LineaCuenta(l.Secuencia ?? 0, l.Descripcion ?? "", l.Cantidad ?? 0, l.Precio ?? 0,
                l.Importe ?? 0, l.Activo, l.Usuario, l.Fecha))
            .ToList();

        return new CuentaHabitacion(
            (long)c.Factura, (int)c.Habitacion, c.Huesped, (long?)c.ClienteId, c.Cliente, c.Rnc,
            c.LlevaItbis, c.LlevaPropina, c.RegimenEspecial,
            (long?)c.Reserva, c.FechaEntrada, c.FechaSalida, c.Categoria, c.Transferida,
            c.Saldo, CalculoCuenta.Calcular(lineas, c.LlevaItbis, c.LlevaPropina, c.RegimenEspecial), lineas);
    }

    private static string? Limpio(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
