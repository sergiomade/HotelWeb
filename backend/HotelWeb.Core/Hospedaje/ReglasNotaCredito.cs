namespace HotelWeb.Core.Hospedaje;

/// <summary>
/// Las notas de credito (E34) a una factura del check-out o del walk-in. Decisiones del usuario (2026-10-23): las dos de
/// Delphi, la anulacion total (codigo 1, UFrmAnularEcf + Stp_AnularFacturaHotelNC) y la nota por un monto (codigo 3,
/// UFrmMntNCFrontDesk / NCWalking), autorizadas por un responsable (Seg_Usuario.CancelaFactura) con motivo.
/// </summary>
public static class ReglasNotaCredito
{
    public const int Anulacion = 1;
    public const int CorrigeMonto = 3;
    public const int MotivoMinimo = 10;   // Stp_AnularFacturaHotelNC
    public const int MotivoMaximo = 90;   // RazonModificacion

    /// <summary>Lo que falta por acreditar: el total menos las notas que ya tiene (QBalPendiente de Delphi).</summary>
    public static decimal Pendiente(decimal totalOriginal, IEnumerable<decimal> notas) => totalOriginal - notas.Sum();

    /// <summary>Motivo por el que no se puede hacer la nota, o null.</summary>
    public static string? Validar(int codigo, decimal monto, string? motivo, string encfOriginal, bool aceptada, decimal totalOriginal,
        decimal pendiente, bool tieneNotas)
    {
        if (encfOriginal.Length != 13 || encfOriginal[0] != 'E') return "La factura no tiene un comprobante electronico (e-NCF).";
        if (encfOriginal[1..3] is "33" or "34") return "Una nota de credito o debito no lleva nota de credito.";
        if (!aceptada) return $"El {encfOriginal} no esta aceptado por la DGII: no lleva nota de credito.";
        var m = (motivo ?? "").Trim();
        if (m.Length < MotivoMinimo) return $"Escriba el motivo (minimo {MotivoMinimo} letras).";
        if (m.Length > MotivoMaximo) return $"El motivo lleva a lo sumo {MotivoMaximo} letras (va en la nota a la DGII).";
        if (codigo == Anulacion)
        {
            if (tieneNotas) return "La factura ya tiene una nota de credito: no admite la anulacion total.";
            if (totalOriginal <= 0) return "La factura no tiene monto.";
            return null;
        }
        if (codigo != CorrigeMonto) return "Escoja anulacion total o nota por un monto.";
        if (monto <= 0) return "Escriba el monto a acreditar.";
        if (decimal.Round(monto, 2) != monto) return "El monto lleva 2 decimales.";
        if (pendiente <= 0) return "La factura ya no tiene balance para acreditar.";
        if (monto > pendiente) return $"El monto ({monto:N2}) es mayor que lo pendiente de la factura ({pendiente:N2}).";
        return null;
    }
}
