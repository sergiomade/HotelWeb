using HotelWeb.Core.Ecf;

namespace HotelWeb.Tests;

public class EcfHotelTests
{
    private static readonly EmisorEcf Caobas = new("122006044", "HOTEL LAS CAOBAS, SRL", "C/ Luis Enrique Carrón Esq. Av. S.Diego", "809-290-5858");

    // la factura 9653 (E320000006067, aceptada por la DGII el 22/10/2026), como esta en HotelTmpFactura
    private static FacturaEcf F9653(string encf = "E320000006067", string? rnc = "000B7199648", decimal itbis = 3642.18m,
        decimal propina = 2023.43m, bool credito = false, IReadOnlyList<ItemEcf>? items = null) =>
        new(encf, 9653, new DateTime(2026, 10, 22), rnc, "CARLOS MUÑOZ", 20234.37m, itbis, propina, credito,
            items ?? Enumerable.Range(25, 7).Select(d => new ItemEcf($"HOSPEDAJE -> {d}/7/2026", 1, 2500, 2500))
                .Append(new ItemEcf("Transf.de Hab.#103 Fact.#9829", 1, 2734.37m, 2734.37m)).ToList());

    [Fact]
    public void El_xml_es_igual_al_que_armo_Delphi_y_acepto_la_DGII()
    {
        var delphi = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Datos", "E320000006067.xml")).TrimEnd();
        var web = EcfHotel.Ecf(F9653(), Caobas, new DateTime(2028, 12, 31), new DateTime(2026, 10, 22, 15, 45, 26)).TrimEnd();
        Assert.Equal(delphi, web);
    }

    [Fact]
    public void Los_payout_no_van_como_items()
    {
        var items = EcfHotel.Items(new[]
        {
            ("HOSPEDAJE -> 1/7/2026", 1m, 2500m, 2500m, true),
            ("PAY-OUT #RECIBO # 0008050( TAXI )", 1m, 300m, 300m, true),
            ("PAGO RECIBO INGRESO # 0008050", 1m, -2000m, -2000m, true),
            ("BAR anulado", 1m, 500m, 500m, false),
        });
        Assert.Equal("HOSPEDAJE -> 1/7/2026", Assert.Single(items).Descripcion);
    }

    [Fact]
    public void E31_lleva_vencimiento_y_comprador_con_rnc()
    {
        var xml = EcfHotel.Ecf(F9653("E310000000237", "131246796"), Caobas, new DateTime(2028, 12, 31), DateTime.Now);
        Assert.Contains("<FechaVencimientoSecuencia>31-12-2028</FechaVencimientoSecuencia>", xml);
        Assert.Contains("<RNCComprador>131246796</RNCComprador>", xml);
        Assert.Null(EcfHotel.Validar(F9653("E310000000237", "131246796")));
        Assert.NotNull(EcfHotel.Validar(F9653("E310000000237", "000B7199648")));   // pasaporte no vale en E31
    }

    [Fact]
    public void E32_de_consumo_sin_rnc_va_sin_comprador_y_es_resumen()
    {
        var f = F9653(rnc: "000000000");
        Assert.True(EcfHotel.EsResumen(f));
        Assert.DoesNotContain("<Comprador>", EcfHotel.Ecf(f, Caobas, new DateTime(2028, 12, 31), DateTime.Now));
        Assert.DoesNotContain("<Comprador>", EcfHotel.Rfce(f, Caobas, "KmDYeU"));
        Assert.Contains("<CodigoSeguridadeCF>KmDYeU</CodigoSeguridadeCF>", EcfHotel.Rfce(f, Caobas, "KmDYeU"));
    }

    [Fact]
    public void E44_sin_itbis_con_forma_de_pago_y_telefono()
    {
        var f = F9653("E440000000010", "131246796", itbis: 0);
        var xml = EcfHotel.Ecf(f, Caobas, new DateTime(2028, 12, 31), DateTime.Now);
        Assert.Contains("<MontoPago>22257.80</MontoPago>", xml);
        Assert.Contains("<TelefonoEmisor>809-290-5858</TelefonoEmisor>", xml);
        Assert.Contains("<MontoExento>20234.37</MontoExento>", xml);
        Assert.Contains("<IndicadorFacturacion>4</IndicadorFacturacion>", xml);
        Assert.DoesNotContain("IndicadorMontoGravado", xml);
        Assert.NotNull(EcfHotel.Validar(F9653("E440000000010", "131246796")));   // con ITBIS no
    }

    [Fact]
    public void A_credito_lleva_fecha_limite()
    {
        var xml = EcfHotel.Ecf(F9653(credito: true), Caobas, new DateTime(2028, 12, 31), DateTime.Now);
        Assert.Contains("<TipoPago>2</TipoPago>", xml);
        Assert.Contains("<FechaLimitePago>21-11-2026</FechaLimitePago>", xml);
    }

    [Fact]
    public void No_arma_con_precio_menor_de_uno_ni_sin_ecf()
    {
        Assert.NotNull(EcfHotel.Validar(F9653(items: new[] { new ItemEcf("X", 1, 0.5m, 0.5m) })));
        Assert.NotNull(EcfHotel.Validar(F9653(encf: "B0200000001")));
    }
}

public class EcfCafeteriaTests
{
    private static readonly EmisorEcf Caobas = new("122006044", "HOTEL LAS CAOBAS, SRL", "C/ Luis Enrique Carrón Esq. Av. S.Diego", "809-290-5858");

    private static string Delphi(string archivo) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Datos", archivo)).TrimEnd();

    [Fact]
    public void E31_de_la_cafeteria_igual_a_la_de_Delphi_aceptada()
    {
        // FaTmpFactura 12790 (E310000000233): el total es 780.00 aunque 609.38 + 109.68 + 60.93 = 779.99
        var f = new FacturaEcf("E310000000233", 12790, new DateTime(2026, 10, 3), "132493931", "FERRECOMERCIAL MADE ALCANTARA, SRL",
            609.38m, 109.68m, 60.93m, false, [new ItemEcf("Filete en Salsa de Hongos", 1, 609.38m, 609.38m)], 780.00m);
        var web = EcfHotel.Ecf(f, Caobas, new DateTime(2028, 12, 31), new DateTime(2026, 10, 3, 18, 13, 7)).TrimEnd();
        Assert.Equal(Delphi("E310000000233.xml"), web);
    }

    [Fact]
    public void E32_de_consumo_y_su_resumen_iguales_a_los_de_Delphi()
    {
        // FaTmpFactura 12788 (E320000006062), cliente de contado (RNC en ceros): ECF completo y RFCE con su codigo
        var f = new FacturaEcf("E320000006062", 12788, new DateTime(2026, 10, 3), "00000000000", "CLIENTE DE CONTADO",
            124.99m, 22.49m, 12.49m, false, [new ItemEcf("Jamon", 1, 54.68m, 54.68m), new ItemEcf("JUGO", 1, 70.31m, 70.31m)], 159.98m);
        Assert.True(EcfHotel.EsResumen(f));
        Assert.Equal(Delphi("E320000006062.xml"), EcfHotel.Ecf(f, Caobas, new DateTime(2028, 12, 31), new DateTime(2026, 10, 3, 12, 1, 1)).TrimEnd());
        Assert.Equal(Delphi("E320000006062_rfce.xml"), EcfHotel.Rfce(f, Caobas, "PZW42A").TrimEnd());
    }
}
