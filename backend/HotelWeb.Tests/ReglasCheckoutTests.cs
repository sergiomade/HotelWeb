using HotelWeb.Core.Hospedaje;

namespace HotelWeb.Tests;

public class ReglasCheckoutTests
{
    [Fact]
    public void Sin_rnc_solo_consumo_y_el_sugerido_es_el_del_cliente()
    {
        Assert.Equal(new[] { 2 }, ReglasCheckout.Permitidos("000000000").Select(t => t.Id));
        Assert.Equal(4, ReglasCheckout.Permitidos("131246796").Count);
        Assert.Equal(1, ReglasCheckout.Sugerido(1, "131246796"));
        Assert.Equal(2, ReglasCheckout.Sugerido(1, ""));     // credito fiscal sin RNC no se puede
        Assert.Equal(2, ReglasCheckout.Sugerido(7, "131246796"));
    }

    [Fact]
    public void E32_menor_sin_identificacion_si_y_de_250_mil_no()
    {
        Assert.Null(ReglasCheckout.Validar(2, "", "", 3500, 492));
        Assert.Null(ReglasCheckout.Validar(2, "000B7199648", "CARLOS", 25900, 3642));   // pasaporte
        Assert.NotNull(ReglasCheckout.Validar(2, "", "X", 250_000, 1));
        Assert.NotNull(ReglasCheckout.Validar(2, "131246796", " ", 250_000, 1));
        Assert.Null(ReglasCheckout.Validar(2, "131246796", "EMPRESA", 300_000, 1));
    }

    [Fact]
    public void E31_E44_E45_piden_rnc_valido_y_E44_sin_itbis()
    {
        Assert.NotNull(ReglasCheckout.Validar(1, "", "X", 100, 18));
        Assert.NotNull(ReglasCheckout.Validar(1, "000B7199648", "X", 100, 18));
        Assert.Null(ReglasCheckout.Validar(1, "131246796", "X", 100, 18));
        Assert.NotNull(ReglasCheckout.Validar(13, "131246796", "X", 100, 18));
        Assert.Null(ReglasCheckout.Validar(13, "131246796", "X", 100, 0));
        Assert.NotNull(ReglasCheckout.Validar(14, "000000000", "X", 100, 18));
        Assert.NotNull(ReglasCheckout.Validar(99, "131246796", "X", 100, 18));
    }

    [Fact]
    public void Solo_con_balance_en_cero()
    {
        Assert.NotNull(ReglasCheckout.PuedeCerrar(10, 100, true, false));
        Assert.NotNull(ReglasCheckout.PuedeCerrar(0, 0, true, false));
        Assert.Null(ReglasCheckout.PuedeCerrar(0, 0, true, true));   // transferida: en 0
        Assert.Null(ReglasCheckout.PuedeCerrar(0, 3500, true, false));
    }
}
