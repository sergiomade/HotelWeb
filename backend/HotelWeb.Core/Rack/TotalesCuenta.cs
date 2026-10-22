namespace HotelWeb.Core.Rack;

/// <summary>Una linea de la cuenta de la habitacion (HotelFacturaDetalle).</summary>
public sealed record LineaCuenta(int Secuencia, string Descripcion, decimal Cantidad, decimal Precio, decimal Importe,
    bool Activo, string? Usuario, DateTime? Fecha)
{
    /// <summary>Los PAY-OUT (dinero que el hotel adelanta por el huesped) no llevan impuestos, como en Delphi.</summary>
    public bool EsPayOut => Descripcion.StartsWith("PAY-OUT", StringComparison.OrdinalIgnoreCase);

    /// <summary>Cargo con impuestos: activo, positivo y no PAY-OUT.</summary>
    public bool EsCargo => Activo && Importe > 0 && !EsPayOut;

    /// <summary>Pagos (recibos, lineas negativas) y PAY-OUT: se suman tal cual.</summary>
    public bool EsPagoOPayOut => Activo && (Importe < 0 || EsPayOut);
}

public sealed record Totales(decimal Cargos, decimal Itbis, decimal Propina, decimal PagosYPayOut, decimal Saldo);

/// <summary>
/// El desglose del saldo con las reglas de dbo.BalanceActualHabitacion (la funcion que exige el check-out):
/// ITBIS 18 % si el cliente lo lleva y no es de regimen especial (E44); propina 10 % si la lleva; los PAY-OUT y los
/// pagos sin impuestos; el saldo se redondea a pesos. El saldo que se muestra es el de la funcion; esto lo explica.
/// </summary>
public static class CalculoCuenta
{
    public const decimal PorcItbis = 0.18m;
    public const decimal PorcPropina = 0.10m;

    public static Totales Calcular(IEnumerable<LineaCuenta> lineas, bool llevaItbis, bool llevaPropina, bool regimenEspecial)
    {
        var lista = lineas.ToList();
        var cargos = lista.Where(l => l.EsCargo).Sum(l => l.Importe);
        var otros = lista.Where(l => l.EsPagoOPayOut).Sum(l => l.Importe);
        var itbis = llevaItbis && !regimenEspecial ? cargos * PorcItbis : 0m;
        var propina = llevaPropina ? cargos * PorcPropina : 0m;
        // Round de T-SQL a 0 decimales: lejos del cero en el .5
        var saldo = Math.Round(cargos + itbis + propina + otros, 0, MidpointRounding.AwayFromZero);
        return new Totales(cargos, Math.Round(itbis, 2), Math.Round(propina, 2), otros, saldo);
    }
}
