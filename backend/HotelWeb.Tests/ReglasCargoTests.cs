using HotelWeb.Core.Hospedaje;
using HotelWeb.Core.Rack;

namespace HotelWeb.Tests;

public class ReglasCargoTests
{
    private static NuevoCargo C(int servicio = 5, string d = "piscina adultos", int cant = 2, decimal precio = 211.86m) =>
        ReglasCargo.Normalizar(new NuevoCargo(servicio, d, cant, precio));

    private static LineaCuenta L(string d, decimal importe, bool activo = true) => new(1, d, 1, importe, importe, activo, "u", null);

    [Fact]
    public void Importe_es_cantidad_por_precio_y_descripcion_en_mayusculas()
    {
        var c = C();
        Assert.Equal(423.72m, c.Importe);
        Assert.Equal("PISCINA ADULTOS", c.Descripcion);
        Assert.Null(ReglasCargo.Validar(c, false));
    }

    [Fact]
    public void No_carga_a_una_cuenta_transferida() => Assert.NotNull(ReglasCargo.Validar(C(), true));

    [Theory]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(11)]
    public void Los_del_sistema_no_se_cargan_a_mano(int servicio) => Assert.NotNull(ReglasCargo.Validar(C(servicio), false));

    [Theory]
    [InlineData("", 1, 10)]
    [InlineData("X", 0, 10)]
    [InlineData("X", 1, 0)]
    [InlineData("X", 1, 10.555)]
    public void Lo_que_no_vale(string d, int cant, decimal precio) => Assert.NotNull(ReglasCargo.Validar(C(5, d, cant, precio), false));

    [Fact]
    public void Que_se_puede_anular()
    {
        Assert.Null(ReglasCargo.ValidarAnular(L("HOSPEDAJE -> 22/10/2026", 2500)));      // una noche cargada dos veces
        Assert.Null(ReglasCargo.ValidarAnular(L("PISCINA ADULTOS", 211.86m)));
        Assert.NotNull(ReglasCargo.ValidarAnular(L("PISCINA ADULTOS", 211.86m, activo: false)));
        Assert.NotNull(ReglasCargo.ValidarAnular(L("PAGO RECIBO INGRESO # 0008048", -25900)));
        Assert.NotNull(ReglasCargo.ValidarAnular(L("Transf.de Hab.#103 Fact.#9829", 2734.37m)));
        Assert.NotNull(ReglasCargo.ValidarAnular(L("CAFETERIA - FACTURA # 12748", 132.81m)));
        Assert.NotNull(ReglasCargo.ValidarAnular(L("PAY-OUT TAXI", 300)));
    }

    [Fact]
    public void El_encabezado_como_el_saldo()
    {
        // 2734.37 de hospedaje, cliente con ITBIS y propina: lo que Delphi dejo en HotelFactura de la habitacion 102
        var e = ReglasCargo.Encabezado(new[] { L("HOSPEDAJE", 2734.37m) }, true, true, false);
        Assert.Equal(new TotalesEncabezado(2734.37m, 492.18m, 273.43m, 3500m), e);
        // regimen especial: sin ITBIS
        Assert.Equal(0m, ReglasCargo.Encabezado(new[] { L("HOSPEDAJE", 1000m) }, true, true, true).Itbis);
    }
}
