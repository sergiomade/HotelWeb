namespace HotelWeb.Core.Cafeteria;

/// <summary>Un pago que digita el cajero (faFormadePago: 1 efectivo, 2 tarjeta, 3 credito).</summary>
public sealed record PagoCafeteria(int FormaPago, decimal Monto, int? TipoTarjeta);

/// <summary>Lo que se graba en faFacturaPago: lo APLICADO (la devuelta va en FaFactura.Cambio).</summary>
public sealed record PagoAplicado(int FormaPago, string Descripcion, decimal Monto, int? TipoTarjeta);

public sealed record PagosCobro(IReadOnlyList<PagoAplicado> Aplicados, decimal Recibido, decimal Devuelta);

/// <summary>
/// Reglas del cobro de la cafeteria (etapa 8b), como UFrmPagar de Delphi con las decisiones del usuario (2026-10-23):
/// - Credito (forma 3) solo a un cliente de CxC (no al de contado, al de la habitacion ni al comodin) y por lo que falta:
///   Delphi lo dejaba al cliente 1 y por mas del total (factura 12722: 751 sobre 750).
/// - Se graba lo aplicado: Stp_CalculoCambiodeTurno suma faFacturaPago.MontoPago y con lo entregado contaba la devuelta
///   como venta (igual que el walk-in, etapa 6). La devuelta solo sale del efectivo.
/// - E44 (regimenes especiales) va sin ITBIS, como el walk-in (Delphi pedia cambiar el cliente).
/// </summary>
public static class ReglasCobroCafeteria
{
    public const int Efectivo = 1;
    public const int Tarjeta = 2;
    public const int Credito = 3;
    /// <summary>TRANSF.HABITACION: el cargo a la habitacion (parte 8c).</summary>
    public const int Habitacion = 8;

    public const int ClienteContado = 1;
    public const int ClienteHabitacion = 2;

    public static readonly IReadOnlyDictionary<int, string> Formas = new Dictionary<int, string>
    {
        [Efectivo] = "EFECTIVO",
        [Tarjeta] = "TARJETA DE CREDITO",
        [Credito] = "CREDITO",
    };

    /// <summary>Si el cliente de CxC puede llevar la cuenta a credito.</summary>
    public static bool AdmiteCredito(long? cliente, long comodin) =>
        cliente is { } c && c > 0 && c != ClienteContado && c != ClienteHabitacion && c != comodin;

    /// <summary>Los pagos aplicados al total, o el motivo si no alcanzan o no se permiten.</summary>
    public static (PagosCobro? Pagos, string? Motivo) Pagos(IReadOnlyList<PagoCafeteria> pagos, decimal total, bool admiteCredito)
    {
        var lista = pagos.Where(p => p.Monto != 0).ToList();
        if (lista.Count == 0) return (null, "Falta el pago.");
        foreach (var p in lista)
        {
            if (!Formas.ContainsKey(p.FormaPago)) return (null, $"Forma de pago {p.FormaPago} no permitida en la cafetería.");
            if (p.Monto < 0) return (null, "Los montos no pueden ser negativos.");
            if (p.Monto != Math.Round(p.Monto, 2)) return (null, "Los montos van con 2 decimales.");
            if (p.FormaPago == Tarjeta && p.TipoTarjeta is null or <= 0) return (null, "Elija el banco de la tarjeta.");
        }
        if (lista.Any(p => p.FormaPago == Credito) && !admiteCredito)
            return (null, "El crédito es solo para un cliente de CxC: elija el cliente (no el de contado).");

        var otros = lista.Where(p => p.FormaPago != Efectivo).Sum(p => p.Monto);
        if (otros > total) return (null, "La tarjeta y el crédito no pueden pasar del total: la devuelta solo sale del efectivo.");
        var recibido = lista.Sum(p => p.Monto);
        if (recibido < total) return (null, $"Faltan {total - recibido:N2} para el total.");

        var aplicados = lista.Where(p => p.FormaPago != Efectivo)
            .Select(p => new PagoAplicado(p.FormaPago, Formas[p.FormaPago], p.Monto, p.FormaPago == Tarjeta ? p.TipoTarjeta : null)).ToList();
        var efectivo = total - otros;
        if (efectivo > 0) aplicados.Insert(0, new PagoAplicado(Efectivo, Formas[Efectivo], efectivo, null));
        return (new PagosCobro(aplicados, recibido, recibido - total), null);
    }

    public static bool EsCredito(IEnumerable<PagoAplicado> aplicados) => aplicados.Any(p => p.FormaPago == Credito);
}
