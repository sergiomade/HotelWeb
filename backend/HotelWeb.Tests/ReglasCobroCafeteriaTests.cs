using HotelWeb.Core.Cafeteria;

namespace HotelWeb.Tests;

public class ReglasCobroCafeteriaTests
{
    private static PagoCafeteria Ef(decimal m) => new(ReglasCobroCafeteria.Efectivo, m, null);
    private static PagoCafeteria Tj(decimal m, int? banco = 3) => new(ReglasCobroCafeteria.Tarjeta, m, banco);
    private static PagoCafeteria Cr(decimal m) => new(ReglasCobroCafeteria.Credito, m, null);

    [Fact]
    public void Efectivo_con_devuelta_graba_lo_aplicado()
    {
        // factura 12791: 60.00 pagada con 100 (Delphi grababa 100 y el cambio de turno contaba 40 de mas)
        var (p, m) = ReglasCobroCafeteria.Pagos([Ef(100)], 60, false);
        Assert.Null(m);
        var a = Assert.Single(p!.Aplicados);
        Assert.Equal((ReglasCobroCafeteria.Efectivo, 60m), (a.FormaPago, a.Monto));
        Assert.Equal((100m, 40m), (p.Recibido, p.Devuelta));
    }

    [Fact]
    public void Tarjeta_y_efectivo_juntos_la_devuelta_sale_del_efectivo()
    {
        var (p, _) = ReglasCobroCafeteria.Pagos([Tj(500), Ef(1000)], 1200, false);
        Assert.Equal([(1, 700m), (2, 500m)], p!.Aplicados.Select(x => (x.FormaPago, x.Monto)));
        Assert.Equal(300m, p.Devuelta);
        Assert.Equal(3, p.Aplicados[1].TipoTarjeta);
    }

    [Fact]
    public void La_tarjeta_no_pasa_del_total_y_pide_banco()
    {
        Assert.NotNull(ReglasCobroCafeteria.Pagos([Tj(785)], 780, false).Motivo);   // factura 12790: Delphi grabo 785 en tarjeta
        Assert.Null(ReglasCobroCafeteria.Pagos([Tj(780)], 780, false).Motivo);
        Assert.NotNull(ReglasCobroCafeteria.Pagos([Tj(780, null)], 780, false).Motivo);
    }

    [Fact]
    public void Credito_solo_a_cliente_de_cxc_y_sin_pasar_del_total()
    {
        Assert.NotNull(ReglasCobroCafeteria.Pagos([Cr(750)], 750, admiteCredito: false).Motivo);
        Assert.NotNull(ReglasCobroCafeteria.Pagos([Cr(751)], 750, admiteCredito: true).Motivo);   // factura 12722: 751 sobre 750
        var (p, _) = ReglasCobroCafeteria.Pagos([Cr(750)], 750, admiteCredito: true);
        Assert.True(ReglasCobroCafeteria.EsCredito(p!.Aplicados));

        Assert.False(ReglasCobroCafeteria.AdmiteCredito(1, 12));      // de contado
        Assert.False(ReglasCobroCafeteria.AdmiteCredito(2, 12));      // la habitacion
        Assert.False(ReglasCobroCafeteria.AdmiteCredito(12, 12));     // el comodin
        Assert.False(ReglasCobroCafeteria.AdmiteCredito(null, 12));
        Assert.True(ReglasCobroCafeteria.AdmiteCredito(2027, 12));    // BANCO BHD
    }

    [Fact]
    public void Lo_que_falta_y_lo_que_no_se_permite()
    {
        Assert.NotNull(ReglasCobroCafeteria.Pagos([Ef(50)], 60, false).Motivo);
        Assert.NotNull(ReglasCobroCafeteria.Pagos([], 60, false).Motivo);
        Assert.NotNull(ReglasCobroCafeteria.Pagos([new PagoCafeteria(8, 60, null)], 60, false).Motivo);   // la habitacion va aparte
        Assert.NotNull(ReglasCobroCafeteria.Pagos([new PagoCafeteria(4, 60, null)], 60, false).Motivo);   // dolares: no se usan
        Assert.NotNull(ReglasCobroCafeteria.Pagos([Ef(-10), Ef(70)], 60, false).Motivo);
        Assert.NotNull(ReglasCobroCafeteria.Pagos([Ef(60.001m)], 60, false).Motivo);
    }
}
