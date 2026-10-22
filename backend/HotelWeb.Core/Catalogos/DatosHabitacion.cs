namespace HotelWeb.Core.Catalogos;

/// <summary>
/// Lo que se puede cambiar de una habitacion (hotelHabitacion). La posicion x/y es la del plano de Delphi.
/// Cabana es la columna hotelHabitacion.cabana, que en el hotel significa "sabanas" (en pantalla dice Sabanas).
/// </summary>
public sealed record CambiosHabitacion(
    int Tipo, int Estado, string? Comentario, string? Extension, bool Television, bool AireAcondicionado, bool Cabana);

/// <summary>Reglas del catalogo de habitaciones (las columnas de hotelHabitacion y lo que la web no deja hacer).</summary>
public static class ReglasHabitacion
{
    public const int LargoComentario = 500;   // Comentario es text; en la web se limita a algo que quepa en la ficha
    public const int LargoExtension = 4;      // char(4)
    public const int NumeroMaximo = 99_999;

    /// <summary>Motivo por el que no se puede guardar, o null si esta bien.</summary>
    public static string? Validar(CambiosHabitacion c)
    {
        if (c.Tipo <= 0) return "Elija el tipo de habitacion.";
        if (c.Estado <= 0) return "Elija el estado de la habitacion.";
        if ((c.Comentario?.Length ?? 0) > LargoComentario) return $"El comentario lleva a lo sumo {LargoComentario} letras.";
        if ((c.Extension?.Trim().Length ?? 0) > LargoExtension) return $"La extension lleva a lo sumo {LargoExtension} caracteres.";
        return null;
    }

    public static string? ValidarNueva(int numero, int x, int y, CambiosHabitacion c)
    {
        if (numero <= 0 || numero > NumeroMaximo) return $"El numero de la habitacion va de 1 a {NumeroMaximo}.";
        if (x < 0 || y < 0 || x > 5000 || y > 5000) return "La posicion en el plano no es valida.";
        return Validar(c);
    }

    /// <summary>Comentario y extension como los guarda Delphi: sin espacios de mas y vacio = NULL.</summary>
    public static string? Limpio(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>
    /// Donde poner una habitacion nueva en el plano: la siguiente casilla libre de la fila de su piso (las
    /// habitaciones se separan cada 83 puntos), o una fila nueva debajo de todo si el piso no tiene fila.
    /// </summary>
    public static (int X, int Y) PosicionSugerida(int numero, IReadOnlyList<(int Numero, int X, int Y)> existentes)
    {
        const int paso = 83, margen = 5, fila = 90;
        if (existentes.Count == 0) return (margen, 65);
        var piso = numero / 100;
        var delPiso = existentes.Where(e => e.Numero / 100 == piso).ToList();
        if (delPiso.Count > 0)
        {
            // la fila del piso con la habitacion mas cercana en numero
            var cercana = delPiso.OrderBy(e => Math.Abs(e.Numero - numero)).First();
            var enFila = existentes.Where(e => e.Y == cercana.Y).Select(e => e.X).ToHashSet();
            for (var x = margen; x < 5000; x += paso)
                if (!enFila.Any(o => Math.Abs(o - x) < paso - 3)) return (x, cercana.Y);
        }
        return (margen, existentes.Max(e => e.Y) + fila);
    }
}
