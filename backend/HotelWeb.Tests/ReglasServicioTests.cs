using HotelWeb.Core.Catalogos;

namespace HotelWeb.Tests;

public class ReglasServicioTests
{
    [Fact]
    public void Piscina_adultos_con_itbis_da_249_99_por_el_redondeo_a_2_decimales() =>
        // 250 / 1.18 = 211.864...; la columna lleva 2 decimales (211.86) y vuelve 249.99
        Assert.Equal(249.99m, ReglasServicio.ConItbis(211.86m));

    [Fact]
    public void Hospedaje_con_itbis_y_propina() => Assert.Equal(3500m, ReglasServicio.ConItbisYPropina(2734.375m));

    [Theory]
    [InlineData("", 1, false)]
    [InlineData("LAVANDERIA", -1, false)]
    [InlineData("LAVANDERIA", 10.555, false)]
    [InlineData("LAVANDERIA", 150, true)]
    public void Validacion(string d, decimal precio, bool vale) =>
        Assert.Equal(vale, ReglasServicio.Validar(ReglasServicio.Normalizar(new DatosServicio(d, precio, true))) is null);

    [Fact]
    public void En_mayusculas() =>
        Assert.Equal("MINIBAR", ReglasServicio.Normalizar(new DatosServicio(" minibar ", 1, true)).Descripcion);

    [Fact]
    public void El_siguiente_numero_cuenta_como_numero_no_como_texto() =>
        // "9" > "11" como texto; como numero el mayor es 11
        Assert.Equal("12", ReglasServicio.Siguiente(new[] { "1   ", "9   ", "11  ", "2   " }));

    [Fact]
    public void Los_del_sistema_estan_marcados()
    {
        Assert.True(ReglasServicio.DelSistema.ContainsKey(9));
        Assert.False(ReglasServicio.DelSistema.ContainsKey(5));
    }
}
