namespace HotelWeb.Core.Hospedaje;

/// <summary>
/// Una forma de pago del recibo (HotelReciboDetalle). Monedas_id es la forma de pago de ConMonedas: 1 efectivo,
/// 2 dolares, 3 cheque, 4 tarjeta, 5 nota de credito, 6 credito, 7 PAY-OUT, 8 deposito.
/// </summary>
public sealed record PagoRecibo(int FormaPago, string Descripcion, decimal Monto, decimal? MontoUs, string? NoCheque,
    DateTime? FechaCheque, string? Banco);

/// <summary>
/// Reglas de los recibos de pago a la cuenta de la habitacion (UFrmMntRecibos de Delphi). Cada linea es una forma de
/// pago; todas van a la habitacion del recibo. El PAY-OUT (dinero que el hotel adelanta por el huesped) se escribe en
/// negativo y entra a la cuenta como cargo. STP_CARGARPAGOAHABITACION pasa cada linea a la cuenta.
/// </summary>
public static class ReglasRecibo
{
    public const int Dolares = 2;
    public const int Cheque = 3;
    public const int Tarjeta = 4;
    public const int PayOut = 7;
    public const string ConceptoPorOmision = "Abono o Saldo a Cuenta ";

    public static PagoRecibo Normalizar(PagoRecibo p) => p with
    {
        Descripcion = (p.Descripcion ?? "").Trim().ToUpperInvariant(),
        NoCheque = string.IsNullOrWhiteSpace(p.NoCheque) ? null : p.NoCheque.Trim(),
        Banco = string.IsNullOrWhiteSpace(p.Banco) ? null : p.Banco.Trim().ToUpperInvariant(),
    };

    public static decimal Total(IEnumerable<PagoRecibo> pagos) => pagos.Sum(p => p.Monto);

    /// <summary>Motivo por el que no se puede guardar el recibo, o null (como Grabar de Delphi, mas los largos).</summary>
    public static string? Validar(IReadOnlyList<PagoRecibo> pagos, string? concepto)
    {
        if (pagos.Count == 0) return "Agregue al menos una forma de pago.";
        foreach (var p in pagos)
        {
            if (p.FormaPago <= 0) return "Elija la forma de pago de cada linea.";
            if (p.Descripcion.Length == 0) return "Falta la descripcion de una forma de pago.";
            if (p.Descripcion.Length > 40) return "La descripcion de cada forma de pago lleva a lo sumo 40 letras.";
            if (p.Monto == 0) return "Una forma de pago no tiene monto.";
            if (p.Monto < 0 && p.FormaPago != PayOut) return "Solo el PAY-OUT va en negativo.";
            if (p.FormaPago == PayOut && p.Monto > 0) return "El PAY-OUT se escribe en negativo (es dinero que sale para el huesped).";
            if (Math.Abs(p.Monto) > 99_999_999m || decimal.Round(p.Monto, 2) != p.Monto) return "Revise los montos (2 decimales).";
            if (p.FormaPago == Dolares && (p.MontoUs is null or <= 0)) return "Con dolares, escriba tambien lo recibido en US$.";
            if ((p.NoCheque?.Length ?? 0) > 15) return "El numero de cheque lleva a lo sumo 15 caracteres.";
            if ((p.Banco?.Length ?? 0) > 50) return "El banco o tarjeta lleva a lo sumo 50 letras.";
        }
        if (Total(pagos) <= 0) return "El total del recibo tiene que ser mayor que cero.";
        if ((concepto?.Length ?? 0) > 500) return "El concepto lleva a lo sumo 500 letras.";
        return null;
    }

    /// <summary>
    /// El numero del recibo como lo escribe STP_CARGARPAGOAHABITACION en la cuenta: 7 digitos con ceros delante
    /// (8048 -> 0008048); de un millon en adelante, un solo cero delante.
    /// </summary>
    public static string NumeroEnCuenta(long recibo) => recibo < 1_000_000 ? recibo.ToString("D7") : "0" + recibo;

    /// <summary>Las lineas de la cuenta que puso ese recibo empiezan asi (pago o PAY-OUT).</summary>
    public static IReadOnlyList<string> PrefijosEnCuenta(long recibo)
    {
        var n = NumeroEnCuenta(recibo);
        return new[] { $"PAGO RECIBO INGRESO # {n}(", $"PAY-OUT #RECIBO # {n}(" };
    }

    /// <summary>Se anula si esta activo, si no paso por el cuadre de caja (Posteado) y si la cuenta sigue abierta.</summary>
    public static string? ValidarAnular(string? estatus, bool posteado, bool cuentaAbierta)
    {
        if (!string.Equals(estatus?.Trim(), "Activo", StringComparison.OrdinalIgnoreCase)) return "El recibo ya esta anulado.";
        if (posteado) return "El recibo ya paso por el cuadre de caja: no se anula aqui.";
        if (!cuentaAbierta) return "La cuenta de la habitacion ya se cerro: el recibo no se anula aqui.";
        return null;
    }
}
