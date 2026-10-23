namespace HotelWeb.Core.Hospedaje;

/// <summary>Que se hace con la linea de la reserva al mover una cuenta a otra habitacion libre.</summary>
public enum AccionLinea
{
    /// <summary>Mismo tipo, categoria y tarifa: solo cambia el numero de habitacion.</summary>
    SoloNumero,
    /// <summary>La linea es solo de esta habitacion (cantidad 1): se le cambia tipo, categoria y tarifa.</summary>
    Actualizar,
    /// <summary>La linea tiene mas habitaciones: se le quita una y se agrega una linea nueva para esta.</summary>
    Separar,
}

/// <summary>
/// Cambio a una habitacion LIBRE. Delphi (UFrmMntTransferirHuesped, "no ocupada") solo cambia HotelFactura.Habitacion_id
/// y sigue cobrando el tipo y precio de antes. Decision del usuario (2026-10-23): como el check-in, se cobra el precio
/// del tipo nuevo desde la proxima noche (las noches ya cargadas no cambian), con "Respetar precio reservado" si la
/// nueva es mas cara.
/// </summary>
public static class ReglasCambioHabitacion
{
    public static AccionLinea Accion(LineaReservada actual, int tipo, string categoria, decimal tarifa) =>
        actual.Tipo == tipo && Igual(actual.Categoria, categoria) && actual.Tarifa == tarifa ? AccionLinea.SoloNumero
        : actual.Cantidad <= 1 ? AccionLinea.Actualizar
        : AccionLinea.Separar;

    /// <summary>
    /// La carga de la noche (STP_CargarHospedajeDiario) busca la tarifa por reserva + tipo + categoria y toma la
    /// primera: dos lineas con el mismo tipo y categoria y distinta tarifa cobrarian cualquiera de las dos. Motivo, o null.
    /// </summary>
    public static string? Conflicto(IEnumerable<(int Tipo, string Categoria, decimal Tarifa)> lineas, Func<int, string> nombreTipo)
    {
        var g = lineas.GroupBy(l => (l.Tipo, Categoria: l.Categoria.Trim().ToUpperInvariant()))
            .FirstOrDefault(x => x.Select(l => l.Tarifa).Distinct().Count() > 1);
        if (g is null) return null;
        var tarifas = string.Join(" y ", g.Select(l => l.Tarifa.ToString("N2")).Distinct());
        return $"La reserva quedaria con {nombreTipo(g.Key.Tipo)} categoria {g.Key.Categoria} a dos tarifas ({tarifas}): la carga de " +
               "la noche no sabria cual cobrar. Use la misma tarifa u otra categoria.";
    }

    private static bool Igual(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
