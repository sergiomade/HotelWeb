using HotelWeb.Core.Hospedaje;

namespace HotelWeb.Tests;

public class ReglasReciboTests
{
    private static PagoRecibo P(int forma, decimal monto, string d = "efectivo", decimal? us = null) =>
        ReglasRecibo.Normalizar(new PagoRecibo(forma, d, monto, us, null, null, null));

    [Fact]
    public void Un_pago_en_efectivo_vale() => Assert.Null(ReglasRecibo.Validar(new[] { P(1, 3500) }, null));

    [Fact]
    public void Varias_formas_y_el_total()
    {
        var pagos = new[] { P(1, 2000), P(4, 1500, "visa") };
        Assert.Null(ReglasRecibo.Validar(pagos, null));
        Assert.Equal(3500m, ReglasRecibo.Total(pagos));
    }

    [Fact]
    public void El_pay_out_va_en_negativo_y_el_total_tiene_que_quedar_positivo()
    {
        Assert.Null(ReglasRecibo.Validar(new[] { P(1, 1000), P(7, -300, "taxi") }, null));
        Assert.NotNull(ReglasRecibo.Validar(new[] { P(7, 300, "taxi") }, null));         // positivo no
        Assert.NotNull(ReglasRecibo.Validar(new[] { P(7, -300, "taxi") }, null));        // total negativo
        Assert.NotNull(ReglasRecibo.Validar(new[] { P(1, -300) }, null));                // negativo sin ser PAY-OUT
    }

    [Fact]
    public void Dolares_piden_us()
    {
        Assert.NotNull(ReglasRecibo.Validar(new[] { P(2, 6000, "dolares") }, null));
        Assert.Null(ReglasRecibo.Validar(new[] { P(2, 6000, "dolares", 100) }, null));
    }

    [Theory]
    [InlineData(8048, "0008048")]
    [InlineData(7, "0000007")]
    [InlineData(1234567, "01234567")]
    public void Numero_como_lo_escribe_Delphi_en_la_cuenta(long n, string texto) =>
        Assert.Equal(texto, ReglasRecibo.NumeroEnCuenta(n));

    [Fact]
    public void Prefijos_del_pago_y_del_pay_out()
    {
        var p = ReglasRecibo.PrefijosEnCuenta(8048);
        Assert.Contains("PAGO RECIBO INGRESO # 0008048(", p);
        Assert.Contains("PAY-OUT #RECIBO # 0008048(", p);
    }

    [Fact]
    public void Cuando_se_anula()
    {
        Assert.Null(ReglasRecibo.ValidarAnular("Activo    ", false, true));
        Assert.NotNull(ReglasRecibo.ValidarAnular("Anulado", false, true));
        Assert.NotNull(ReglasRecibo.ValidarAnular("Activo", true, true));
        Assert.NotNull(ReglasRecibo.ValidarAnular("Activo", false, false));
    }
}
