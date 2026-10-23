namespace HotelWeb.Core.Hospedaje;

/// <summary>
/// Cambio de la fecha de salida de una habitacion hospedada. Delphi no lo tiene (la reserva hospedada no se edita) y
/// la carga de la noche no mira la salida: la fecha sirve para el rack, los reportes y la disponibilidad. Cada cambio
/// se guarda en HotelCambioSalida (MIG_24), por cuenta; la salida de la cuenta es la de su ultimo cambio.
/// </summary>
public static class ReglasSalida
{
    /// <summary>Motivo por el que no se puede poner esa salida, o null.</summary>
    public static string? Validar(DateTime nueva, DateTime entrada, DateTime hoy, DateTime? actual, bool transferida)
    {
        if (transferida) return "La cuenta esta transferida a otra habitacion: no se le cambia la salida.";
        if (nueva.Date <= entrada.Date) return "La salida tiene que ser despues del dia de entrada.";
        if (nueva.Date < hoy.Date) return "La salida no puede ser antes de hoy.";
        if (actual?.Date == nueva.Date) return "Esa ya es la fecha de salida.";
        return null;
    }

    /// <summary>
    /// La salida de la reserva solo cambia cuando todas sus cuentas abiertas quedan con la misma fecha (si una sola
    /// cambia, la reserva se queda con la de las demas). Devuelve esa fecha, o null si no hay una sola.
    /// </summary>
    public static DateTime? SalidaDeLaReserva(IEnumerable<DateTime> salidasAbiertas)
    {
        var distintas = salidasAbiertas.Select(s => s.Date).Distinct().ToList();
        return distintas.Count == 1 ? distintas[0] : null;
    }

    /// <summary>Aviso (no impide) de salida antes de lo cargado: las noches ya cargadas no se quitan solas.</summary>
    public static string? Aviso(DateTime nueva, int nochesCargadas, DateTime entrada)
    {
        var noches = (int)(nueva.Date - entrada.Date).TotalDays;
        return nochesCargadas > noches
            ? $"Ya tiene {nochesCargadas} noches cargadas y con la nueva salida son {noches}: anule las de mas si corresponde."
            : null;
    }
}
