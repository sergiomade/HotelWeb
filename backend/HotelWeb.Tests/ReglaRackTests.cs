using HotelWeb.Core.Rack;

namespace HotelWeb.Tests;

public class ReglaRackTests
{
    private static readonly DateTime Hoy = new(2026, 10, 22, 15, 30, 0);

    private static DatosHabitacion H(bool disponible = true, bool cuenta = false, DateTime? salida = null, bool llega = false) =>
        new(103, disponible, cuenta, salida, llega);

    [Fact]
    public void Sin_cuenta_ni_reserva_esta_libre() => Assert.Equal(Situacion.Libre, ReglaRack.SituacionDe(H()));

    [Fact]
    public void Con_cuenta_abierta_esta_ocupada() => Assert.Equal(Situacion.Ocupada, ReglaRack.SituacionDe(H(cuenta: true)));

    [Fact]
    public void Libre_con_reserva_de_hoy_llega_hoy() => Assert.Equal(Situacion.LlegaHoy, ReglaRack.SituacionDe(H(llega: true)));

    [Fact]
    public void Bloqueada_sin_cuenta_esta_bloqueada_aunque_tenga_reserva() =>
        Assert.Equal(Situacion.Bloqueada, ReglaRack.SituacionDe(H(disponible: false, llega: true)));

    [Fact]
    public void La_cuenta_abierta_manda_sobre_el_bloqueo_y_avisa()
    {
        var h = H(disponible: false, cuenta: true, salida: Hoy.AddDays(2));
        Assert.Equal(Situacion.Ocupada, ReglaRack.SituacionDe(h));
        Assert.True(ReglaRack.AvisosDe(h, Hoy).OcupadaEstandoBloqueada);
    }

    [Fact]
    public void Avisa_si_sale_hoy_o_si_la_salida_ya_paso()
    {
        Assert.True(ReglaRack.AvisosDe(H(cuenta: true, salida: Hoy.Date), Hoy).SaleHoy);
        Assert.False(ReglaRack.AvisosDe(H(cuenta: true, salida: Hoy.Date), Hoy).SalidaVencida);
        Assert.True(ReglaRack.AvisosDe(H(cuenta: true, salida: Hoy.Date.AddDays(-1)), Hoy).SalidaVencida);
        Assert.Equal(new Avisos(false, false, false), ReglaRack.AvisosDe(H(cuenta: true, salida: Hoy.AddDays(1)), Hoy));
    }

    [Fact]
    public void Una_libre_no_tiene_avisos_de_salida() =>
        Assert.Equal(new Avisos(false, false, false), ReglaRack.AvisosDe(H(salida: Hoy.Date.AddDays(-3)), Hoy));

    [Theory]
    [InlineData(103, 1)]
    [InlineData(125, 1)]
    [InlineData(214, 2)]
    [InlineData(7, 0)]
    public void Piso_por_el_numero(int numero, int piso) => Assert.Equal(piso, ReglaRack.PisoDe(numero));
}
