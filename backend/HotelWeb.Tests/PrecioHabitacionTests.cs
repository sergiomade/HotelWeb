using HotelWeb.Core.Catalogos;

namespace HotelWeb.Tests;

public class PrecioHabitacionTests
{
    [Theory]
    [InlineData(3500, 2734.375)]
    [InlineData(2500, 1953.125)]
    [InlineData(4500, 3515.625)]
    [InlineData(5120, 4000)]
    [InlineData(0, 0)]
    public void Del_precio_final_al_que_se_guarda(decimal final, decimal guardado)
    {
        Assert.Equal(guardado, PrecioHabitacion.DesdeFinal(final));
        Assert.Equal(final, PrecioHabitacion.ConImpuestos(guardado));
    }

    [Theory]
    [InlineData(1953.13, 2500.01)]    // asi quedo en Delphi la DOBLE C: el huesped paga 1 centavo de mas
    [InlineData(3515.62, 4499.99)]    // MATRIMONIAL G
    public void Los_redondeos_de_Delphi_se_ven_en_el_precio_final(decimal guardado, decimal final) =>
        Assert.Equal(final, PrecioHabitacion.ConImpuestos(guardado));

    [Fact]
    public void Un_final_cualquiera_vuelve_a_si_mismo()
    {
        for (var final = 1000m; final <= 9000m; final += 0.01m * 37)
        {
            var f = Math.Round(final, 2);
            Assert.Equal(f, PrecioHabitacion.ConImpuestos(PrecioHabitacion.DesdeFinal(f)));
        }
    }

    [Theory]
    [InlineData(-1, 0, false)]
    [InlineData(100, -1, false)]
    [InlineData(2734.375, 0, true)]
    [InlineData(2734.3751, 0, false)]
    [InlineData(100, 10.5, true)]
    [InlineData(100, 10.555, false)]
    public void Validacion(decimal precio, decimal adicional, bool vale) =>
        Assert.Equal(vale, PrecioHabitacion.Validar(precio, adicional) is null);
}
