namespace HotelWeb.Core.Hospedaje;

/// <summary>Una habitacion libre para el check-in.</summary>
public sealed record HabitacionLibre(int Numero, int Tipo, bool Disponible);

/// <summary>Un lugar a llenar: cada linea de la reserva se reparte en tantas habitaciones como su cantidad.</summary>
public sealed record Lugar(int Secuencia, int Tipo, int Indice);

/// <summary>Lo que recepcion eligio: la habitacion de cada lugar.</summary>
public sealed record Asignacion(int Secuencia, int Indice, int Habitacion);

/// <summary>
/// Reglas del check-in. Delphi (STP_CrearHabitacionesReserva + Fn_HabitacionDisponible) sugiere la primera libre de
/// CUALQUIER tipo; la web sugiere libres del tipo reservado, y deja escoger cualquier libre.
/// </summary>
public static class ReglasCheckIn
{
    /// <summary>Los lugares de una reserva: por cada linea, uno por habitacion (cantidad).</summary>
    public static IReadOnlyList<Lugar> Lugares(IEnumerable<(int Secuencia, int Tipo, int Cantidad)> lineas) =>
        lineas.SelectMany(l => Enumerable.Range(1, Math.Max(0, l.Cantidad)).Select(i => new Lugar(l.Secuencia, l.Tipo, i))).ToList();

    /// <summary>Sugerencia: para cada lugar, la primera libre y disponible de su tipo (por numero) que no se haya sugerido.</summary>
    public static IReadOnlyList<Asignacion> Sugerir(IReadOnlyList<Lugar> lugares, IReadOnlyList<HabitacionLibre> libres)
    {
        var usadas = new HashSet<int>();
        var r = new List<Asignacion>();
        foreach (var l in lugares)
        {
            var h = libres.Where(x => x.Tipo == l.Tipo && x.Disponible && !usadas.Contains(x.Numero)).OrderBy(x => x.Numero).FirstOrDefault();
            if (h is null) continue;   // sin libre de ese tipo: recepcion escoge otra
            usadas.Add(h.Numero);
            r.Add(new Asignacion(l.Secuencia, l.Indice, h.Numero));
        }
        return r;
    }

    /// <summary>Motivo por el que no se puede hacer el check-in con esas habitaciones, o null.</summary>
    public static string? Validar(IReadOnlyList<Lugar> lugares, IReadOnlyList<Asignacion> elegidas, IReadOnlyList<HabitacionLibre> libres)
    {
        if (lugares.Count == 0) return "La reserva no tiene habitaciones.";
        foreach (var l in lugares)
            if (!elegidas.Any(a => a.Secuencia == l.Secuencia && a.Indice == l.Indice && a.Habitacion > 0))
                return "Falta escoger la habitacion de cada lugar de la reserva.";
        if (elegidas.Count != lugares.Count) return "Hay habitaciones de mas para esta reserva.";
        var repetida = elegidas.GroupBy(a => a.Habitacion).FirstOrDefault(g => g.Count() > 1);
        if (repetida is not null) return $"La habitacion {repetida.Key} esta escogida dos veces.";
        foreach (var a in elegidas)
        {
            var h = libres.FirstOrDefault(x => x.Numero == a.Habitacion);
            if (h is null) return $"La habitacion {a.Habitacion} no esta libre (ocupada o no existe).";
            if (!h.Disponible) return $"La habitacion {a.Habitacion} esta bloqueada o no se usa.";
        }
        return null;
    }

    /// <summary>Avisos (no impiden): habitaciones de otro tipo que el reservado.</summary>
    public static IReadOnlyList<string> Avisos(IReadOnlyList<Lugar> lugares, IReadOnlyList<Asignacion> elegidas,
        IReadOnlyList<HabitacionLibre> libres, Func<int, string> nombreTipo)
    {
        var r = new List<string>();
        foreach (var a in elegidas)
        {
            var l = lugares.First(x => x.Secuencia == a.Secuencia && x.Indice == a.Indice);
            var h = libres.FirstOrDefault(x => x.Numero == a.Habitacion);
            if (h is not null && h.Tipo != l.Tipo)
                r.Add($"La {a.Habitacion} es {nombreTipo(h.Tipo)} y la reserva pide {nombreTipo(l.Tipo)}: se cobra la tarifa reservada.");
        }
        return r;
    }
}

/// <summary>La carga del hospedaje de una noche (STP_CargarHospedajeDiario), sin repetir.</summary>
public static class ReglasHospedaje
{
    /// <summary>El texto de la linea, igual que Delphi: "HOSPEDAJE -> 22/10/2026" (dia y mes sin cero delante).</summary>
    public static string Descripcion(DateTime noche) => $"HOSPEDAJE -> {noche.Day}/{noche.Month}/{noche.Year}";
}
