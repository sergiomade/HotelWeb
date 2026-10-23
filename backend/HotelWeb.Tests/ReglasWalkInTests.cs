using HotelWeb.Core.Hospedaje;

namespace HotelWeb.Tests;

public class ReglasWalkInTests
{
    private static PagoRecibo P(int forma, decimal monto) => new(forma, forma == 1 ? "EFECTIVO" : "TARJETA", monto, null, null, null, null);

    private static DatosWalkIn D(int tipo = 2, string? rnc = null, string? cedula = null, bool propina = false, decimal precio = 2734.37m,
        params PagoRecibo[] pagos) =>
        ReglasWalkIn.Normalizar(new DatosWalkIn("juan perez", cedula, rnc, null, null, tipo, propina,
            new[] { new LineaWalkIn(1, "HOSPEDAJE -> 17/6/2026", 1, precio) }, pagos.Length > 0 ? pagos : new[] { P(1, 5000) }));

    [Fact]
    public void Totales_como_Delphi_con_itbis_truncado()
    {
        // walk-in 316: 2,734.37 -> ITBIS 492.18 (no 492.19), total 3,226.55
        var t = ReglasWalkIn.Totales(new[] { new LineaWalkIn(1, "x", 1, 2734.37m) }, 2, false);
        Assert.Equal((2734.37m, 492.18m, 0m, 3226.55m), (t.SubTotal, t.Itbis, t.Propina, t.Total));
        // walk-in 317: piscina 169.49 -> 30.50, 199.99
        Assert.Equal(199.99m, ReglasWalkIn.Totales(new[] { new LineaWalkIn(2, "x", 1, 169.49m) }, 2, false).Total);
        Assert.Equal(0m, ReglasWalkIn.Totales(new[] { new LineaWalkIn(1, "x", 1, 1000) }, 13, true).Itbis);   // E44
        Assert.Equal(100m, ReglasWalkIn.Totales(new[] { new LineaWalkIn(1, "x", 1, 1000) }, 2, true).Propina);
    }

    [Fact]
    public void Se_graba_lo_aplicado_y_la_devuelta_sale_del_efectivo()
    {
        // walk-in 314: 3,226.55 pagado con 3,500 -> se graban 3,226.55; devuelta 273.45
        var p = ReglasWalkIn.Pagos(new[] { P(1, 3500) }, 3226.55m)!;
        Assert.Equal((3500m, 273.45m, 3226.55m), (p.Recibido, p.Devuelta, Assert.Single(p.Aplicados).Monto));
        // tarjeta + efectivo de mas: la tarjeta queda igual
        var m = ReglasWalkIn.Pagos(new[] { P(4, 2000), P(1, 1500) }, 3226.55m)!;
        Assert.Equal(new[] { 2000m, 1226.55m }, m.Aplicados.Select(x => x.Monto));
        Assert.Null(ReglasWalkIn.Pagos(new[] { P(1, 3000) }, 3226.55m));        // no alcanza
        Assert.Null(ReglasWalkIn.Pagos(new[] { P(4, 3500) }, 3226.55m));        // la tarjeta no da devuelta
    }

    [Fact]
    public void Validaciones_de_Delphi()
    {
        Assert.Null(ReglasWalkIn.Validar(D()));                                                   // consumo sin identificacion
        Assert.NotNull(ReglasWalkIn.Validar(D() with { Nombre = "" }));
        Assert.NotNull(ReglasWalkIn.Validar(D(tipo: 1)));                                         // E31 sin RNC
        Assert.Null(ReglasWalkIn.Validar(D(tipo: 1, rnc: "131246796")));
        Assert.NotNull(ReglasWalkIn.Validar(D(rnc: "1234")));                                     // largo
        Assert.Contains("menor", ReglasWalkIn.Validar(D(pagos: P(1, 100))));                      // no alcanza
        Assert.Contains("devuelta", ReglasWalkIn.Validar(D(pagos: P(4, 5000))));                  // tarjeta de mas
        Assert.NotNull(ReglasWalkIn.Validar(D(precio: 0.5m)));                                     // precio < 1
        Assert.NotNull(ReglasWalkIn.Validar(D(precio: 220000m, pagos: P(1, 300000))));            // E32 >= 250k sin identificar
        Assert.Null(ReglasWalkIn.Validar(D(precio: 220000m, cedula: "00113918239", pagos: P(1, 300000))));
    }

    [Fact]
    public void Sin_rnc_el_ecf_lleva_la_cedula_y_si_nada_ceros()
    {
        Assert.Equal("000000000", ReglasWalkIn.RncComprobante(D(cedula: "00000000000")));
        Assert.Equal("00113918239", ReglasWalkIn.RncComprobante(D(cedula: "00113918239")));
        Assert.Equal("131246796", ReglasWalkIn.RncComprobante(D(rnc: "131246796", cedula: "00113918239")));
    }
}
