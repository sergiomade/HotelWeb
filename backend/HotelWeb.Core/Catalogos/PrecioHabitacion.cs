namespace HotelWeb.Core.Catalogos;

/// <summary>
/// HotelHabitacionTipoPrecio guarda el precio SIN impuestos (numeric 12,3). En Delphi se escribe tal cual, y el
/// empleado lo saca dividiendo el precio final entre 1.28 (ITBIS 18 % + propina 10 %): 3,500 -> 2,734.375. Cuando lo
/// redondea a 2 decimales queda 1,953.13 y el huesped paga 2,500.01. En la web se escribe el precio final y se guarda
/// el precio sin impuestos con 3 decimales, que es lo que hace la mayoria de las filas.
/// </summary>
public static class PrecioHabitacion
{
    /// <summary>ITBIS 18 % + propina 10 %: lo que paga un cliente normal sobre el precio de la habitacion.</summary>
    public const decimal Factor = 1.28m;

    public const decimal Maximo = 999_999_999.999m;   // numeric(12,3)

    /// <summary>Precio final (con ITBIS y propina) de un precio sin impuestos, a centavos.</summary>
    public static decimal ConImpuestos(decimal sinImpuestos) =>
        Math.Round(sinImpuestos * Factor, 2, MidpointRounding.AwayFromZero);

    /// <summary>Precio sin impuestos (3 decimales, como la columna) para que el final de exactamente lo pedido.</summary>
    public static decimal DesdeFinal(decimal final) =>
        Math.Round(final / Factor, 3, MidpointRounding.AwayFromZero);

    /// <summary>Motivo por el que no se puede guardar, o null si esta bien.</summary>
    public static string? Validar(decimal sinImpuestos, decimal personaAdicional)
    {
        if (sinImpuestos < 0 || personaAdicional < 0) return "El precio no puede ser negativo.";
        if (sinImpuestos > Maximo) return "El precio es demasiado grande.";
        if (personaAdicional > 9_999_999_999.99m) return "El precio por persona adicional es demasiado grande.";
        if (decimal.Round(sinImpuestos, 3) != sinImpuestos) return "El precio sin impuestos lleva a lo sumo 3 decimales.";
        if (decimal.Round(personaAdicional, 2) != personaAdicional) return "El precio por persona adicional lleva 2 decimales.";
        return null;
    }
}
