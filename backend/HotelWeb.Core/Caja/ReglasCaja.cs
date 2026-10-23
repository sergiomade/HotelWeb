namespace HotelWeb.Core.Caja;

public enum AmbitoCaja { Hotel, Cafeteria }

/// <summary>Que es una forma de pago para la caja.</summary>
public enum ClaseForma
{
    /// <summary>Pesos en efectivo: se cuentan al cerrar.</summary>
    Efectivo,
    /// <summary>Dolares en efectivo: se cuentan aparte (US$).</summary>
    Dolares,
    /// <summary>Entra dinero pero no a la gaveta: tarjeta, cheque, deposito.</summary>
    OtroDinero,
    /// <summary>No entra dinero: credito, nota de credito, cargo a la habitacion.</summary>
    SinDinero,
    /// <summary>Sale de la gaveta (PAY-OUT del hotel).</summary>
    Salida,
}

/// <summary>Un cobro que entra al cuadre: una linea de pago de un recibo, de un adelanto o de una factura de la cafeteria.</summary>
/// <param name="Tipo">R recibo, A adelanto, F factura de la cafeteria (HotelCierreCajaDetalle.Tipo).</param>
/// <param name="Referencia">Habitacion o mesa.</param>
/// <param name="MontoUs">Los dolares recibidos (forma de dolares); el Monto es su valor en pesos.</param>
public sealed record MovimientoCaja(char Tipo, long Numero, DateTime Fecha, string? Referencia, string? Nombre, int Forma, string FormaNombre,
    decimal Monto, decimal MontoUs);

public sealed record FormaResumen(int Forma, string Nombre, ClaseForma Clase, int Cantidad, decimal Monto, decimal MontoUs);

/// <param name="Ingresos">Todo el dinero que entro (pesos, dolares en pesos, tarjetas, cheques...) menos los PAY-OUT.</param>
/// <param name="Efectivo">Los pesos que deben estar en la gaveta (efectivo menos PAY-OUT).</param>
/// <param name="Dolares">Los dolares que deben estar en la gaveta.</param>
/// <param name="SinDinero">Credito, notas de credito y cargos a la habitacion (informativo).</param>
public sealed record ResumenCaja(IReadOnlyList<FormaResumen> Formas, decimal Ingresos, decimal Efectivo, decimal Dolares, decimal SinDinero);

/// <summary>
/// El cuadre de caja (etapa 9a). Hotel como STP_CuadreDeCaja + el resumen de UFrmMntCuadredeCaja: recibos y adelantos sin
/// postear; PAY-OUT en negativo; el credito y las notas de credito no son dinero. Diferencia con Delphi: los dolares van
/// aparte en US$ (Delphi sumaba los US$ con los pesos en el total). Cafeteria: lo cobrado (faFacturaPago, lo aplicado).
/// </summary>
public static class ReglasCaja
{
    /// <summary>ConMonedas (recibos y adelantos del hotel) o faFormadePago (cafeteria).</summary>
    public static ClaseForma Clase(AmbitoCaja ambito, int forma) => ambito switch
    {
        AmbitoCaja.Hotel => forma switch
        {
            1 => ClaseForma.Efectivo,
            2 => ClaseForma.Dolares,
            3 or 4 or 8 => ClaseForma.OtroDinero,   // cheque, tarjeta, deposito
            7 => ClaseForma.Salida,                 // PAY-OUT
            _ => ClaseForma.SinDinero,              // 5 nota de credito, 6 credito
        },
        _ => forma switch
        {
            1 => ClaseForma.Efectivo,
            4 => ClaseForma.Dolares,
            2 or 5 or 6 => ClaseForma.OtroDinero,   // tarjeta, euros, cheque
            _ => ClaseForma.SinDinero,              // 3 credito, 8 cargo a la habitacion
        },
    };

    public static ResumenCaja Resumen(AmbitoCaja ambito, IEnumerable<MovimientoCaja> movimientos)
    {
        var formas = movimientos.GroupBy(m => m.Forma)
            .Select(g => new FormaResumen(g.Key, g.First().FormaNombre, Clase(ambito, g.Key), g.Count(), g.Sum(m => m.Monto), g.Sum(m => m.MontoUs)))
            .OrderBy(f => f.Clase).ThenBy(f => f.Forma).ToList();
        decimal Suma(ClaseForma c) => formas.Where(f => f.Clase == c).Sum(f => f.Monto);
        var salida = Suma(ClaseForma.Salida);
        var ingresos = Suma(ClaseForma.Efectivo) + Suma(ClaseForma.Dolares) + Suma(ClaseForma.OtroDinero) - salida;
        return new ResumenCaja(formas, ingresos, Suma(ClaseForma.Efectivo) - salida,
            formas.Where(f => f.Clase == ClaseForma.Dolares).Sum(f => f.MontoUs), Suma(ClaseForma.SinDinero));
    }

    /// <summary>Lo que impide cerrar, o null. Con diferencia hay que explicarla.</summary>
    public static string? ValidarCierre(ResumenCaja r, int movimientos, decimal contadoEfectivo, decimal contadoDolares, string? comentario)
    {
        if (movimientos == 0) return "No tiene nada pendiente que cerrar.";
        if (contadoEfectivo < 0 || contadoDolares < 0) return "Los montos contados no pueden ser negativos.";
        if (contadoEfectivo != Math.Round(contadoEfectivo, 2) || contadoDolares != Math.Round(contadoDolares, 2)) return "Los montos van con 2 decimales.";
        var hayDiferencia = contadoEfectivo != r.Efectivo || contadoDolares != r.Dolares;
        if (hayDiferencia && string.IsNullOrWhiteSpace(comentario)) return "Hay diferencia con lo esperado: escriba el motivo en el comentario.";
        if (comentario is { Length: > 200 }) return "El comentario es muy largo (200 letras como máximo).";
        return null;
    }
}
