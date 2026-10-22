namespace HotelWeb.Core.Catalogos;

/// <summary>Lo que se guarda de un servicio (HotelServicioAdicional).</summary>
public sealed record DatosServicio(string Descripcion, decimal Precio, bool Impuesto);

/// <summary>
/// Reglas del catalogo de servicios. El precio es SIN impuestos (al cargarlo a una cuenta Delphi copia descripcion y
/// precio a la linea, y el ITBIS y la propina se suman segun el cliente): cambiar el catalogo afecta solo los cargos
/// nuevos. Algunos servicios los usa el sistema por su numero.
/// </summary>
public static class ReglasServicio
{
    public const int LargoDescripcion = 50;   // varchar(50)
    public const decimal PrecioMaximo = 9_999_999_999.99m;   // numeric(12,2)

    /// <summary>Numero -> para que lo usa el sistema (Delphi y los procedimientos de la base).</summary>
    public static readonly IReadOnlyDictionary<int, string> DelSistema = new Dictionary<int, string>
    {
        [1] = "Hospedaje de cada noche y pagos de recibos",
        [3] = "Transferencia de habitacion",
        // hasta MIG_22 (2026-10-22) STP_PasarAlHotel cargaba la cafeteria con el 6: las lineas viejas lo tienen
        [6] = "Early check-in (y los cargos de cafeteria anteriores a MIG_22)",
        [7] = "Cargos de la cafeteria a la habitacion (STP_PasarAlHotel, desde MIG_22)",
        [9] = "Cargos que llegan de otra habitacion (Stp_TransferiraOcupada)",
        [11] = "Adelantos y abonos de reservas",
    };

    public static DatosServicio Normalizar(DatosServicio d) =>
        d with { Descripcion = (d.Descripcion ?? "").Trim().ToUpperInvariant() };

    public static string? Validar(DatosServicio d)
    {
        if (d.Descripcion.Length == 0) return "Escriba la descripcion del servicio.";
        if (d.Descripcion.Length > LargoDescripcion) return $"La descripcion lleva a lo sumo {LargoDescripcion} letras.";
        if (d.Precio < 0) return "El precio no puede ser negativo.";
        if (d.Precio > PrecioMaximo) return "El precio es demasiado grande.";
        if (decimal.Round(d.Precio, 2) != d.Precio) return "El precio lleva 2 decimales.";
        return null;
    }

    /// <summary>Lo que paga un cliente con ITBIS (18 %).</summary>
    public static decimal ConItbis(decimal precio) => Math.Round(precio * 1.18m, 2, MidpointRounding.AwayFromZero);

    /// <summary>Lo que paga un cliente con ITBIS y propina (28 %).</summary>
    public static decimal ConItbisYPropina(decimal precio) => Math.Round(precio * 1.28m, 2, MidpointRounding.AwayFromZero);

    /// <summary>El numero siguiente (Servicio_id es char(4) con numeros): el mayor + 1, como Delphi.</summary>
    public static string Siguiente(IEnumerable<string> existentes)
    {
        var mayor = existentes.Select(e => int.TryParse(e.Trim(), out var n) ? n : 0).DefaultIfEmpty(0).Max();
        var sig = mayor + 1;
        if (sig > 9999) throw new InvalidOperationException("No quedan numeros de servicio (Servicio_id es de 4 caracteres).");
        return sig.ToString();
    }
}
