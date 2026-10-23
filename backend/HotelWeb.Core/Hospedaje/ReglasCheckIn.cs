namespace HotelWeb.Core.Hospedaje;

/// <summary>Una habitacion libre para el check-in.</summary>
public sealed record HabitacionLibre(int Numero, int Tipo, bool Disponible);

/// <summary>Un lugar a llenar: cada linea de la reserva se reparte en tantas habitaciones como su cantidad.</summary>
public sealed record Lugar(int Secuencia, int Tipo, int Indice);

/// <summary>
/// Lo que recepcion eligio: la habitacion de cada lugar y, si cambia, la categoria y la tarifa (sin impuestos). Sin
/// categoria ni tarifa se queda lo reservado.
/// </summary>
public sealed record Asignacion(int Secuencia, int Indice, int Habitacion, string? Categoria = null, decimal? Tarifa = null);

/// <summary>Una linea de la reserva tal como esta (HotelHabitacionReservada).</summary>
public sealed record LineaReservada(int Secuencia, int Tipo, string Categoria, decimal Tarifa, int Cantidad);

/// <summary>
/// Como queda una linea de la reserva despues del check-in: <paramref name="Secuencia"/> es la de la linea original
/// (si <paramref name="Nueva"/> es false, se actualiza esa; si es true, se agrega una linea con estos datos).
/// </summary>
public sealed record LineaFinal(int Secuencia, bool Nueva, int Tipo, string Categoria, decimal Tarifa, IReadOnlyList<Asignacion> Habitaciones)
{
    public int Cantidad => Habitaciones.Count;
}

/// <summary>
/// El precio que se cobra es el del TIPO de la habitacion que se da, no el reservado (pedido del usuario 2026-10-22): si
/// en el check-in cambia el tipo, la categoria o la tarifa de una habitacion, su linea de la reserva se ajusta (o se
/// separa, si la linea tiene varias habitaciones y no todas cambian), porque la carga de cada noche
/// (STP_CargarHospedajeDiario) busca la tarifa en la linea de la reserva por tipo y categoria.
/// </summary>
public static class RepartoCheckIn
{
    /// <param name="tipoDe">El tipo de cada habitacion (numero).</param>
    public static IReadOnlyList<LineaFinal> Repartir(IReadOnlyList<LineaReservada> lineas, IReadOnlyList<Asignacion> elegidas,
        Func<int, int> tipoDe)
    {
        var r = new List<LineaFinal>();
        foreach (var l in lineas)
        {
            var suyas = elegidas.Where(a => a.Secuencia == l.Secuencia).OrderBy(a => a.Indice).ToList();
            if (suyas.Count == 0) continue;
            // cada habitacion con su tipo real, su categoria y su tarifa
            var grupos = suyas.GroupBy(a => (Tipo: tipoDe(a.Habitacion),
                    Categoria: (a.Categoria ?? l.Categoria).Trim().ToUpperInvariant(),
                    Tarifa: a.Tarifa ?? l.Tarifa))
                .ToList();
            // el grupo que se queda en la linea original: el que no cambia nada; si todos cambian, el primero
            var igual = grupos.FirstOrDefault(g => g.Key.Tipo == l.Tipo && g.Key.Categoria == l.Categoria.Trim().ToUpperInvariant() && g.Key.Tarifa == l.Tarifa)
                        ?? grupos[0];
            foreach (var g in grupos)
                r.Add(new LineaFinal(l.Secuencia, !ReferenceEquals(g, igual), g.Key.Tipo, g.Key.Categoria, g.Key.Tarifa, g.ToList()));
        }
        return r;
    }

    /// <summary>True si la linea original queda exactamente como estaba (no hay que tocarla).</summary>
    public static bool SinCambios(LineaFinal f, LineaReservada original) =>
        !f.Nueva && f.Tipo == original.Tipo && f.Categoria == original.Categoria.Trim().ToUpperInvariant()
        && f.Tarifa == original.Tarifa && f.Cantidad == original.Cantidad;
}

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
        IReadOnlyList<HabitacionLibre> libres, Func<int, string> nombreTipo, Func<Asignacion, bool>? respetaReservado = null)
    {
        var r = new List<string>();
        foreach (var a in elegidas)
        {
            var l = lugares.First(x => x.Secuencia == a.Secuencia && x.Indice == a.Indice);
            var h = libres.FirstOrDefault(x => x.Numero == a.Habitacion);
            if (h is not null && h.Tipo != l.Tipo)
                r.Add(respetaReservado?.Invoke(a) == true
                    ? $"La {a.Habitacion} es {nombreTipo(h.Tipo)} y la reserva pedia {nombreTipo(l.Tipo)}: se le respeta el precio reservado."
                    : $"La {a.Habitacion} es {nombreTipo(h.Tipo)} y la reserva pedia {nombreTipo(l.Tipo)}: se cobra el precio de {nombreTipo(h.Tipo)}.");
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
