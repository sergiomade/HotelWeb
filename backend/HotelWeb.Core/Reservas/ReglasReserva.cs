namespace HotelWeb.Core.Reservas;

/// <summary>Estados de una reserva (HotelReservacionEstatus).</summary>
public static class EstadoReserva
{
    public const int Activa = 1;
    public const int Cancelada = 2;
    public const int Hospedado = 3;

    public static string Nombre(int id) => id switch { Activa => "Activa", Cancelada => "Cancelada", Hospedado => "Hospedado", _ => $"Estado {id}" };
}

/// <summary>Una linea de la reserva: un TIPO de habitacion y cuantas (HotelHabitacionReservada), como Delphi.</summary>
public sealed record LineaReserva(int Tipo, int Cantidad, int Adultos, int Ninos, int Bebes, bool Credito,
    string Categoria, decimal Tarifa);

public sealed record DatosReserva(
    string NombreCliente, long? ClienteId, string? Contacto, string? Telefono, string? Huesped,
    string? Cedula, string? Pasaporte, string? Rnc,
    DateTime Entrada, DateTime Salida, string? Comentario, int FormaPago, string? Categoria,
    IReadOnlyList<LineaReserva> Lineas);

/// <summary>Libres de un tipo en un rango de fechas (para avisar de sobreventa).</summary>
public sealed record Disponibilidad(int Tipo, string Descripcion, int Habitaciones, int Ocupadas, int Reservadas)
{
    public int Libres => Habitaciones - Ocupadas - Reservadas;
}

/// <summary>Reglas de las reservas (UFrmMntReservacion de Delphi, con lo que la web agrega).</summary>
public static class ReglasReserva
{
    /// <summary>Noches entre la entrada y la salida (DaysBetween de Delphi, sin horas).</summary>
    public static int Noches(DateTime entrada, DateTime salida) => (int)(salida.Date - entrada.Date).TotalDays;

    /// <summary>Importe de una linea: cantidad x tarifa x noches (DBETARIFAExit de Delphi).</summary>
    public static decimal Importe(LineaReserva l, int noches) => Math.Round(l.Cantidad * l.Tarifa * noches, 2);

    public static int Habitaciones(IEnumerable<LineaReserva> lineas) => lineas.Sum(l => l.Cantidad);

    public static int Personas(IEnumerable<LineaReserva> lineas) => lineas.Sum(l => l.Cantidad * (l.Adultos + l.Ninos + l.Bebes));

    /// <summary>Dos estadias se cruzan si una entra antes de que la otra salga (el dia de salida queda libre).</summary>
    public static bool SeCruzan(DateTime entradaA, DateTime salidaA, DateTime entradaB, DateTime salidaB) =>
        entradaA.Date < salidaB.Date && entradaB.Date < salidaA.Date;

    public static DatosReserva Normalizar(DatosReserva d)
    {
        static string? T(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        return d with
        {
            NombreCliente = (d.NombreCliente ?? "").Trim().ToUpperInvariant(),
            Contacto = T(d.Contacto)?.ToUpperInvariant(), Telefono = T(d.Telefono), Huesped = T(d.Huesped)?.ToUpperInvariant(),
            Cedula = T(d.Cedula), Pasaporte = T(d.Pasaporte)?.ToUpperInvariant(), Rnc = T(d.Rnc), Comentario = T(d.Comentario),
            Categoria = T(d.Categoria)?.ToUpperInvariant(),
            Entrada = d.Entrada.Date, Salida = d.Salida.Date,
            Lineas = d.Lineas.Select(l => l with { Categoria = (l.Categoria ?? "").Trim().ToUpperInvariant() }).ToList(),
        };
    }

    /// <summary>Motivo por el que no se puede guardar, o null. <paramref name="hoy"/>: no se reserva para el pasado.</summary>
    public static string? Validar(DatosReserva d, DateTime hoy, bool nueva)
    {
        if (d.NombreCliente.Length == 0) return "Escriba el nombre del cliente.";
        if (d.NombreCliente.Length > 60) return "El nombre del cliente lleva a lo sumo 60 letras.";
        if ((d.Contacto?.Length ?? 0) > 60) return "El contacto lleva a lo sumo 60 letras.";
        if ((d.Huesped?.Length ?? 0) > 60) return "El huesped lleva a lo sumo 60 letras.";
        if ((d.Telefono?.Length ?? 0) > 13) return "El telefono lleva a lo sumo 13 caracteres.";
        if ((d.Cedula?.Length ?? 0) > 13) return "La cedula lleva a lo sumo 13 caracteres.";
        if ((d.Pasaporte?.Length ?? 0) > 15) return "El pasaporte lleva a lo sumo 15 caracteres.";
        if ((d.Rnc?.Length ?? 0) > 13) return "El RNC lleva a lo sumo 13 caracteres.";
        if ((d.Comentario?.Length ?? 0) > 1000) return "El comentario lleva a lo sumo 1000 letras.";
        if (d.Salida <= d.Entrada) return "La salida tiene que ser despues de la entrada.";
        if (nueva && d.Entrada < hoy.Date) return "No se puede reservar con entrada en el pasado.";
        if (Noches(d.Entrada, d.Salida) > 365) return "La reserva no puede pasar de 365 noches.";
        if (d.Lineas.Count == 0) return "Agregue al menos un tipo de habitacion.";
        foreach (var l in d.Lineas)
        {
            if (l.Tipo <= 0) return "Elija el tipo de habitacion de cada linea.";
            if (l.Cantidad < 1 || l.Cantidad > 99) return "La cantidad de habitaciones va de 1 a 99.";
            if (l.Adultos < 0 || l.Ninos < 0 || l.Bebes < 0) return "Las personas no pueden ser negativas.";
            if (l.Adultos + l.Ninos + l.Bebes == 0) return "Cada habitacion lleva al menos una persona.";
            if (l.Tarifa < 0) return "La tarifa no puede ser negativa.";
            if (l.Categoria.Length != 1) return "Elija la categoria de precio de cada linea.";
        }
        return null;
    }

    /// <summary>Avisos de sobreventa: tipos donde se piden mas habitaciones de las libres en esas fechas.</summary>
    public static IReadOnlyList<string> Sobreventa(DatosReserva d, IReadOnlyList<Disponibilidad> libres)
    {
        var avisos = new List<string>();
        foreach (var g in d.Lineas.GroupBy(l => l.Tipo))
        {
            var pedidas = g.Sum(l => l.Cantidad);
            var disp = libres.FirstOrDefault(x => x.Tipo == g.Key);
            var quedan = Math.Max(0, disp?.Libres ?? 0);
            if (pedidas > quedan)
                avisos.Add($"{disp?.Descripcion ?? $"Tipo {g.Key}"}: se piden {pedidas} y quedan {quedan} libres en esas fechas.");
        }
        return avisos;
    }
}
