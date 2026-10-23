using HotelWeb.Core.Ecf;

namespace HotelWeb.Tests;

public class NotaCreditoEcfTests
{
    private static readonly EmisorEcf Caobas = new("122006044", "HOTEL LAS CAOBAS, SRL", "C/ Luis Enrique Carrón Esq. Av. S.Diego", "809-290-5858");

    // walk-in 312 (E320000006058) y su nota 44 (E340000000001), aceptada por la DGII el 29/09/2026
    private static readonly OriginalNota W312 = new("E320000006058", new DateTime(2026, 9, 29), "000000000", "SERGIO", 338.98m, 61.01m, 0);

    [Fact]
    public void La_E34_es_igual_a_la_que_armo_Delphi_y_acepto_la_DGII()
    {
        var delphi = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Datos", "E340000000001.xml")).TrimEnd();
        var nota = new NotaCredito("E340000000001", 44, new DateTime(2026, 9, 29), 399.99m, 1, "para corregir error en monto");
        var web = NotaCreditoEcf.Xml(nota, W312, Caobas, new DateTime(2026, 9, 29, 14, 42, 50)).TrimEnd();
        Assert.Equal(delphi, web);
    }

    [Fact]
    public void Nota_parcial_en_proporcion_a_la_factura()
    {
        // factura 3,125 + 562.50 + 312.50 = 4,000; se acreditan 1,000 (la cuarta parte)
        var o = new OriginalNota("E310000000237", new DateTime(2026, 10, 23), "130122441", "AUTOFICINA SRL", 3125, 562.50m, 312.50m);
        var d = NotaCreditoEcf.Desglose(1000, 3, o);
        Assert.Equal((781.24m, 0m, 140.63m, 78.13m, 1000m), (d.Gravado, d.Exento, d.Itbis, d.Propina, d.Monto));
        var xml = NotaCreditoEcf.Xml(new NotaCredito("E340000000002", 45, new DateTime(2026, 10, 23), 1000, 3, "descuento acordado"), o, Caobas, DateTime.Now);
        Assert.Contains("<RNCComprador>130122441</RNCComprador>", xml);
        Assert.Contains("<CodigoModificacion>3</CodigoModificacion>", xml);
        Assert.Contains("<OtrosImpuestosAdicionales>78.13</OtrosImpuestosAdicionales>", xml);
        Assert.Contains("<IndicadorNotaCredito>0</IndicadorNotaCredito>", xml);
    }

    [Fact]
    public void Anulacion_total_con_los_montos_del_original_y_despues_de_30_dias_indicador_1()
    {
        var d = NotaCreditoEcf.Desglose(123, 1, W312);   // el monto escrito no importa: va el total original
        Assert.Equal((338.98m, 61.01m, 399.99m), (d.Gravado, d.Itbis, d.Monto));
        Assert.True(NotaCreditoEcf.SinItbis(new DateTime(2026, 10, 30), new DateTime(2026, 9, 29)));
        Assert.False(NotaCreditoEcf.SinItbis(new DateTime(2026, 10, 29), new DateTime(2026, 9, 29)));
        var xml = NotaCreditoEcf.Xml(new NotaCredito("E340000000009", 50, new DateTime(2026, 11, 2), 399.99m, 1, "anulada"), W312, Caobas, DateTime.Now);
        Assert.Contains("<IndicadorNotaCredito>1</IndicadorNotaCredito>", xml);
    }

    [Fact]
    public void No_arma_sin_E34_ni_codigo()
    {
        Assert.NotNull(NotaCreditoEcf.Validar(new NotaCredito("E320000000001", 1, DateTime.Today, 10, 1, null), W312));
        Assert.NotNull(NotaCreditoEcf.Validar(new NotaCredito("E340000000001", 1, DateTime.Today, 10, 7, null), W312));
    }
}

public class NotaCreditoCafeteriaTests
{
    private static readonly EmisorEcf Caobas = new("122006044", "HOTEL LAS CAOBAS, SRL", "C/ Luis Enrique Carrón Esq. Av. S.Diego", "809-290-5858");

    // la factura 12795 (E310000000239): 609.38 + 109.68 + 60.93 = 779.99, pero se cobro y se envio 780.00
    private static readonly OriginalNota F12795 = new("E310000000239", new DateTime(2026, 10, 23), "132493931", "FERRECOMERCIAL MADE ALCANTARA, SRL",
        609.38m, 109.68m, 60.93m, 780.00m);

    [Fact]
    public void La_anulacion_total_acredita_lo_cobrado_con_el_desglose_del_original()
    {
        var d = NotaCreditoEcf.Desglose(780, 1, F12795);
        Assert.Equal(new DesgloseNota(609.38m, 0, 109.68m, 60.93m, 780.00m), d);
        var xml = NotaCreditoEcf.Xml(new NotaCredito("E340000000009", 7, new DateTime(2026, 10, 23), 780, 1, "cliente no consumio"), F12795, Caobas,
            new DateTime(2026, 10, 23, 15, 0, 0));
        Assert.Contains("<MontoTotal>780.00</MontoTotal>", xml);
        Assert.Contains("<MontoGravadoTotal>609.38</MontoGravadoTotal>", xml);
        Assert.Contains("<TotalITBIS>109.68</TotalITBIS>", xml);
        Assert.Contains("<NCFModificado>E310000000239</NCFModificado>", xml);
    }

    [Fact]
    public void La_nota_por_monto_reparte_sobre_lo_cobrado()
    {
        // 390 de 780: la mitad exacta del ITBIS y de la propina
        var d = NotaCreditoEcf.Desglose(390, 3, F12795);
        Assert.Equal((54.84m, 30.47m, 390m), (d.Itbis, d.Propina, d.Monto));
        Assert.Equal(390m - 54.84m - 30.47m, d.Gravado);
    }
}
