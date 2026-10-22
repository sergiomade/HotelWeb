namespace HotelWeb.Core.Reservas;

/// <summary>
/// Adelantos de reserva (HotelReservaAdelanto). Monedas_id es en realidad la forma de pago (ConMonedas: 1 efectivo,
/// 2 dolares, 3 cheque, 4 tarjeta, 8 deposito...). Monto va en pesos; con dolares, Montous es lo recibido en US$.
/// Desde MIG_23 una reserva puede tener varios y el check-in suma los activos.
/// </summary>
public static class ReglasAdelanto
{
    public const string Activa = "Activa";
    public const string Anulada = "Anulada";
    public const int Dolares = 2;

    public static string? Validar(decimal monto, int formaPago, decimal? montoUs, int estadoReserva)
    {
        if (estadoReserva != EstadoReserva.Activa) return "Solo se registran adelantos en reservas activas (antes del check-in).";
        if (monto <= 0) return "El monto del adelanto tiene que ser mayor que cero.";
        if (monto > 99_999_999m) return "El monto es demasiado grande.";
        if (decimal.Round(monto, 2) != monto) return "El monto lleva 2 decimales.";
        if (formaPago <= 0) return "Elija la forma de pago.";
        if (formaPago == Dolares && (montoUs is null or <= 0)) return "Con dolares, escriba tambien lo recibido en US$.";
        if (montoUs < 0) return "El monto en dolares no puede ser negativo.";
        return null;
    }

    /// <summary>Se anula antes del check-in y si esta activo; queda con Estatus Anulada (no se borra).</summary>
    public static string? ValidarAnular(string? estatusAdelanto, int estadoReserva)
    {
        if (estadoReserva != EstadoReserva.Activa) return "La reserva ya no esta activa (ya entro o se cancelo): el adelanto no se anula aqui.";
        if (!string.Equals(estatusAdelanto?.Trim(), Activa, StringComparison.OrdinalIgnoreCase)) return "Ese adelanto ya esta anulado.";
        return null;
    }
}
