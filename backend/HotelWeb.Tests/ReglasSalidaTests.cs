using HotelWeb.Core.Hospedaje;

namespace HotelWeb.Tests;

public class ReglasSalidaTests
{
    private static readonly DateTime Entrada = new(2026, 10, 20);
    private static readonly DateTime Hoy = new(2026, 10, 23);

    [Fact]
    public void Extender_o_adelantar_hasta_hoy_se_permite()
    {
        Assert.Null(ReglasSalida.Validar(new DateTime(2026, 10, 27), Entrada, Hoy, new DateTime(2026, 10, 25), false));
        Assert.Null(ReglasSalida.Validar(Hoy, Entrada, Hoy, new DateTime(2026, 10, 25), false));   // sale hoy
    }

    [Fact]
    public void No_antes_de_hoy_ni_de_la_entrada_ni_igual_ni_transferida()
    {
        Assert.NotNull(ReglasSalida.Validar(new DateTime(2026, 10, 22), Entrada, Hoy, new DateTime(2026, 10, 25), false));
        Assert.NotNull(ReglasSalida.Validar(Entrada, Entrada, Entrada, new DateTime(2026, 10, 25), false));
        Assert.NotNull(ReglasSalida.Validar(new DateTime(2026, 10, 25), Entrada, Hoy, new DateTime(2026, 10, 25), false));
        Assert.NotNull(ReglasSalida.Validar(new DateTime(2026, 10, 27), Entrada, Hoy, new DateTime(2026, 10, 25), true));
    }

    [Fact]
    public void La_reserva_cambia_solo_si_todas_quedan_iguales()
    {
        Assert.Equal(new DateTime(2026, 10, 27), ReglasSalida.SalidaDeLaReserva(new[] { new DateTime(2026, 10, 27), new DateTime(2026, 10, 27, 12, 0, 0) }));
        Assert.Null(ReglasSalida.SalidaDeLaReserva(new[] { new DateTime(2026, 10, 27), new DateTime(2026, 10, 25) }));
    }

    [Fact]
    public void Avisa_si_ya_hay_mas_noches_cargadas()
    {
        Assert.NotNull(ReglasSalida.Aviso(new DateTime(2026, 10, 22), 3, Entrada));
        Assert.Null(ReglasSalida.Aviso(new DateTime(2026, 10, 27), 3, Entrada));
    }
}
