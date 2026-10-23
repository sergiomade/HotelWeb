using HotelWeb.Core.Ecf;
using HotelWeb.Core.Identificacion;

namespace HotelWeb.Core.Hospedaje;

public sealed record LineaWalkIn(int Servicio, string Descripcion, int Cantidad, decimal Precio)
{
    public decimal Importe => Math.Round(Cantidad * Precio, 2, MidpointRounding.AwayFromZero);
}

/// <summary>Lo que se escribe en la pantalla del walk-in.</summary>
public sealed record DatosWalkIn(string Nombre, string? Cedula, string? Rnc, string? Telefono, string? Direccion, int Tipo,
    bool ConPropina, IReadOnlyList<LineaWalkIn> Lineas, IReadOnlyList<PagoRecibo> Pagos);

public sealed record TotalesWalkIn(decimal SubTotal, decimal Itbis, decimal Propina)
{
    public decimal Total => SubTotal + Itbis + Propina;
}

/// <summary>Los pagos como se graban (lo aplicado) y la devuelta.</summary>
public sealed record PagosWalkIn(IReadOnlyList<PagoRecibo> Aplicados, decimal Recibido, decimal Devuelta);

/// <summary>
/// El walk-in (venta sin reserva: piscina, salon, hospedaje de paso), como UfrmWalking de Delphi: ITBIS 18 % siempre,
/// salvo E44; propina 10 % solo si "con propina"; RNC en ceros para el consumo sin identificacion. Diferencia (decision
/// del usuario 2026-10-23): se graba lo APLICADO; Delphi grababa lo entregado y el cuadre contaba la devuelta.
/// </summary>
public static class ReglasWalkIn
{
    public const int Efectivo = 1;

    public static DatosWalkIn Normalizar(DatosWalkIn d) => d with
    {
        Nombre = (d.Nombre ?? "").Trim().ToUpperInvariant(),
        Cedula = Limpio(d.Cedula),
        Rnc = Limpio(d.Rnc),
        Telefono = string.IsNullOrWhiteSpace(d.Telefono) ? null : d.Telefono.Trim(),
        Direccion = string.IsNullOrWhiteSpace(d.Direccion) ? null : d.Direccion.Trim(),
        Lineas = (d.Lineas ?? Array.Empty<LineaWalkIn>()).Select(l => l with { Descripcion = (l.Descripcion ?? "").Trim() }).ToList(),
        Pagos = (d.Pagos ?? Array.Empty<PagoRecibo>()).Select(ReglasRecibo.Normalizar).ToList(),
    };

    private static string? Limpio(string? s) => string.IsNullOrWhiteSpace(s) || IdentificacionRD.SinIdentificacion(s) ? null : s.Trim();

    /// <summary>
    /// Como CalcularTotales: ITBIS y propina TRUNCADOS a 2 decimales (asi quedan en la base: 2,734.37 x 18 % = 492.18).
    /// </summary>
    public static TotalesWalkIn Totales(IEnumerable<LineaWalkIn> lineas, int tipo, bool conPropina)
    {
        var sub = lineas.Sum(l => l.Importe);
        return new TotalesWalkIn(sub, tipo == 13 ? 0 : Truncar(sub * 0.18m), conPropina ? Truncar(sub * 0.10m) : 0);
    }

    private static decimal Truncar(decimal v) => Math.Truncate(v * 100) / 100;

    /// <summary>El RNC que va en el e-CF: el RNC; si no tiene, la cedula (CrearFacturaXmlWalking); si nada, en ceros.</summary>
    public static string RncComprobante(DatosWalkIn d) => d.Rnc ?? d.Cedula ?? "000000000";

    /// <summary>Motivo por el que no se puede grabar, o null (Salvar de Delphi + lo que la DGII rechazaria).</summary>
    public static string? Validar(DatosWalkIn d)
    {
        if (d.Nombre.Length == 0) return "Falta el nombre del cliente.";
        if (d.Nombre.Length > 50) return "El nombre lleva a lo sumo 50 letras.";
        var t = ReglasCheckout.Tipos.FirstOrDefault(x => x.Id == d.Tipo);
        if (t is null) return "Seleccione el tipo de comprobante.";
        if (d.Cedula is { } c && (c.Length != 11 || IdentificacionRD.Determinar(c, out _) == TipoIdentificacion.Invalido))
            return "La cedula debe tener 11 digitos validos (o dejela en blanco).";
        if (d.Rnc is { } r && r.Length is not (9 or 11)) return "El RNC debe tener 9 u 11 caracteres (o dejelo en blanco).";
        if ((d.Telefono?.Length ?? 0) > 13) return "El telefono lleva a lo sumo 13 caracteres.";
        if ((d.Direccion?.Length ?? 0) > 50) return "La direccion lleva a lo sumo 50 letras.";
        if (d.Lineas.Count == 0) return "Agregue al menos un servicio.";
        foreach (var l in d.Lineas)
        {
            if (l.Servicio <= 0 || l.Cantidad <= 0 || l.Precio <= 0 || l.Descripcion.Length == 0)
                return "Alguno de los servicios no tiene servicio, descripcion, cantidad o precio.";
            if (l.Descripcion.Length > 50) return "La descripcion de cada servicio lleva a lo sumo 50 letras.";
            if (l.Precio < 1) return "El precio unitario no puede ser menor de 1.00 (la DGII lo rechaza).";
            if (decimal.Round(l.Precio, 2) != l.Precio) return "El precio lleva 2 decimales.";
        }
        var tot = Totales(d.Lineas, d.Tipo, d.ConPropina);
        if (ReglasCheckout.Validar(d.Tipo, RncComprobante(d), d.Nombre, tot.Total, tot.Itbis) is { } m) return m;
        if (d.Pagos.Any(p => p.FormaPago == ReglasRecibo.PayOut)) return "El walk-in no lleva PAY-OUT.";
        if (ReglasRecibo.Validar(d.Pagos, null) is { } mp) return mp.Replace("recibo", "pago");
        var recibido = d.Pagos.Sum(p => p.Monto);
        if (recibido < tot.Total) return $"El pago ({recibido:N2}) es menor que el total del walk-in ({tot.Total:N2}).";
        return Pagos(d.Pagos, tot.Total) is null
            ? $"Pagaron {recibido - tot.Total:N2} de mas y la devuelta solo sale del efectivo en pesos: ajuste el monto de la tarjeta u otra forma de pago."
            : null;
    }

    /// <summary>
    /// Lo que se graba en HotelReciboDetalle: si pagaron de mas, la devuelta sale del efectivo en pesos (las otras formas
    /// de pago no dan devuelta). Null si no alcanza o si la devuelta es mayor que el efectivo.
    /// </summary>
    public static PagosWalkIn? Pagos(IReadOnlyList<PagoRecibo> pagos, decimal total)
    {
        var recibido = pagos.Sum(p => p.Monto);
        if (recibido < total) return null;
        var devuelta = recibido - total;
        var efectivo = pagos.Where(p => p.FormaPago == Efectivo).Sum(p => p.Monto);
        if (devuelta > efectivo) return null;
        var aplicados = new List<PagoRecibo>();
        var resta = devuelta;
        foreach (var p in pagos)
        {
            if (p.FormaPago == Efectivo && resta > 0)
            {
                var quita = Math.Min(resta, p.Monto);
                resta -= quita;
                if (p.Monto - quita > 0) aplicados.Add(p with { Monto = p.Monto - quita });
            }
            else aplicados.Add(p);
        }
        return new PagosWalkIn(aplicados, recibido, devuelta);
    }

    /// <summary>El walk-in como lo lleva el e-CF (el mismo armador del check-out).</summary>
    public static FacturaEcf Ecf(string encf, long numero, DateTime fecha, DatosWalkIn d, TotalesWalkIn t, bool credito) =>
        new(encf, numero, fecha, RncComprobante(d), d.Nombre, t.SubTotal, t.Itbis, t.Propina, credito,
            d.Lineas.Select(l => new ItemEcf(l.Descripcion, l.Cantidad, l.Precio, l.Importe)).ToList());
}
