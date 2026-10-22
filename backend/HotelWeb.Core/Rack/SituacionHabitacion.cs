namespace HotelWeb.Core.Rack;

/// <summary>Lo que el rack muestra de una habitacion (un color por situacion).</summary>
public enum Situacion
{
    Libre,
    Ocupada,
    /// <summary>Libre, con una reserva activa que llega hoy a esa habitacion.</summary>
    LlegaHoy,
    /// <summary>Estado de la habitacion sin "Disponible" (Bloqueada, No usar): no se puede ocupar.</summary>
    Bloqueada,
}

/// <summary>Datos de una habitacion para decidir su situacion (los arma el repositorio del rack).</summary>
public sealed record DatosHabitacion(
    int Numero,
    bool EstadoDisponible,
    bool TieneCuentaAbierta,
    DateTime? FechaSalida,
    bool TieneLlegadaHoy);

/// <summary>Avisos que se pintan sobre la habitacion, ademas de su color.</summary>
public sealed record Avisos(bool SaleHoy, bool SalidaVencida, bool OcupadaEstandoBloqueada);

/// <summary>
/// La regla del rack. Ocupada = tiene cuenta abierta en HotelFactura (como VW_EstadodelasHabitaciones de Delphi);
/// eso manda sobre el estado de la habitacion: si una bloqueada tiene cuenta, se muestra ocupada con un aviso.
/// </summary>
public static class ReglaRack
{
    public static Situacion SituacionDe(DatosHabitacion h)
    {
        if (h.TieneCuentaAbierta) return Situacion.Ocupada;
        if (!h.EstadoDisponible) return Situacion.Bloqueada;
        return h.TieneLlegadaHoy ? Situacion.LlegaHoy : Situacion.Libre;
    }

    public static Avisos AvisosDe(DatosHabitacion h, DateTime hoy)
    {
        var dia = hoy.Date;
        var sale = h.TieneCuentaAbierta && h.FechaSalida?.Date == dia;
        var vencida = h.TieneCuentaAbierta && h.FechaSalida?.Date < dia;
        return new Avisos(sale, vencida, h.TieneCuentaAbierta && !h.EstadoDisponible);
    }

    /// <summary>Piso por el numero de la habitacion: 101-199 = 1, 201-299 = 2...</summary>
    public static int PisoDe(int numero) => numero >= 100 ? numero / 100 : 0;
}
