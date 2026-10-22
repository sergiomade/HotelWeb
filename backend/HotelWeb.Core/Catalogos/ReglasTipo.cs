namespace HotelWeb.Core.Catalogos;

/// <summary>Lo que se guarda de un tipo de habitacion (HotelHabitacionTipo).</summary>
public sealed record DatosTipo(string Descripcion, string? Siglas, int Minimo, int Maximo);

/// <summary>Reglas del catalogo de tipos de habitacion.</summary>
public static class ReglasTipo
{
    public const int LargoDescripcion = 50;   // varchar(50)
    public const int LargoSiglas = 3;         // char(3)
    public const int MaximoPersonas = 20;

    /// <summary>Como lo guarda Delphi: en mayusculas y sin espacios de mas.</summary>
    public static DatosTipo Normalizar(DatosTipo d) => d with
    {
        Descripcion = (d.Descripcion ?? "").Trim().ToUpperInvariant(),
        Siglas = string.IsNullOrWhiteSpace(d.Siglas) ? "" : d.Siglas.Trim().ToUpperInvariant(),
    };

    /// <summary>Motivo por el que no se puede guardar (ya normalizado), o null si esta bien.</summary>
    public static string? Validar(DatosTipo d, IEnumerable<(int Id, string Descripcion)> otros, int? id)
    {
        if (d.Descripcion.Length == 0) return "Escriba el nombre del tipo.";
        if (d.Descripcion.Length > LargoDescripcion) return $"El nombre lleva a lo sumo {LargoDescripcion} letras.";
        if ((d.Siglas?.Length ?? 0) > LargoSiglas) return $"Las siglas llevan a lo sumo {LargoSiglas} letras.";
        if (d.Minimo < 1) return "El minimo de personas es 1 o mas.";
        if (d.Maximo < d.Minimo) return "El maximo de personas no puede ser menor que el minimo.";
        if (d.Maximo > MaximoPersonas) return $"El maximo de personas es {MaximoPersonas}.";
        if (otros.Any(o => o.Id != id && string.Equals(o.Descripcion.Trim(), d.Descripcion, StringComparison.OrdinalIgnoreCase)))
            return $"Ya hay un tipo llamado {d.Descripcion}.";
        return null;
    }
}
