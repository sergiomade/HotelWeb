using HotelWeb.Core.Hospedaje;

namespace HotelWeb.Tests;

public class ReglasCambioHabitacionTests
{
    [Fact]
    public void Mismo_tipo_categoria_y_tarifa_solo_cambia_el_numero()
    {
        var l = new LineaReservada(10, 2, "A", 3125, 2);
        Assert.Equal(AccionLinea.SoloNumero, ReglasCambioHabitacion.Accion(l, 2, "a", 3125));
    }

    [Fact]
    public void Otro_tipo_con_linea_de_una_la_actualiza_y_de_varias_la_separa()
    {
        Assert.Equal(AccionLinea.Actualizar, ReglasCambioHabitacion.Accion(new LineaReservada(10, 5, "A", 3906.25m, 1), 3, "A", 3125));
        Assert.Equal(AccionLinea.Separar, ReglasCambioHabitacion.Accion(new LineaReservada(10, 2, "A", 3125, 2), 3, "D", 2500));
    }

    [Fact]
    public void Mismo_tipo_y_categoria_con_dos_tarifas_es_conflicto()
    {
        Assert.NotNull(ReglasCambioHabitacion.Conflicto(new[] { (3, "A", 3125m), (3, "a ", 3000m) }, t => $"tipo {t}"));
        Assert.Null(ReglasCambioHabitacion.Conflicto(new[] { (3, "A", 3125m), (3, "A", 3125m), (3, "D", 2500m) }, t => $"tipo {t}"));
    }
}
