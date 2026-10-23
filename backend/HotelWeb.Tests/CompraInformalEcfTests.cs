using HotelWeb.Core.Ecf;

namespace HotelWeb.Tests;

public class CompraInformalEcfTests
{
    private static readonly EmisorEcf Caobas = new("122006044", "HOTEL LAS CAOBAS, SRL", "C/ Luis Enrique Carrón Esq. Av. S.Diego", "809-290-5858");

    private static string Delphi(string archivo) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Datos", archivo)).TrimEnd();

    private static CompraInformal Compra(string encf, long numero, string cedula, string direccion, string isr, params LineaCompra[] lineas)
    {
        var c = CompraInformalEcf.Calcular(lineas, isr);
        return new CompraInformal(encf, numero, new DateTime(2026, 10, 17), new DateTime(2026, 10, 17), cedula, "SERGIO DE JESUS MADE MONTAS", direccion,
            isr, "01", "02", null, c.Lineas, c.MontoGravado, c.MontoExento, c.Itbis, c.Total, c.ItbisRetenido, c.IsrRetenido, c.NetoPagar);
    }

    [Fact]
    public void E41_con_bienes_gravados_y_un_servicio_exento_al_3_por_ciento()
    {
        // HotelCompraInformal 1 (E410000000900, aceptada el 17/10/2026)
        var c = Compra("E410000000900", 1, "00101488674", "bella viasta", "T",
            new LineaCompra("azucar crema", 1, 1, 85.00m, true),
            new LineaCompra("CAfe molidoSantoDomingo", 1, 1, 125.36m, true),
            new LineaCompra("Chapeo del patio trasero de la casa", 2, 1, 375.25m, false));
        Assert.Equal((210.36m, 375.25m, 37.86m, 623.47m, 37.86m, 11.26m), (c.MontoGravado, c.MontoExento, c.Itbis, c.Total, c.ItbisRetenido, c.IsrRetenido));
        Assert.Equal(623.47m - 37.86m - 11.26m, c.NetoPagar);
        Assert.Equal(Delphi("E410000000900.xml"),
            CompraInformalEcf.Xml(c, Caobas, new DateTime(2028, 12, 31), new DateTime(2026, 10, 17, 11, 22, 27)).TrimEnd());
    }

    [Fact]
    public void E41_de_un_servicio_gravado_con_15_por_ciento_de_ISR()
    {
        // HotelCompraInformal 2 (E410000000901, aceptada): retiene el ITBIS y el ISR en la misma linea
        var c = Compra("E410000000901", 2, "00112253265", "principal 23-a, dona carmen, altos del arroyo hondo.", "P",
            new LineaCompra("planchao de ropa", 2, 1, 250.00m, true));
        Assert.Equal((45.00m, 37.50m, 295.00m - 45.00m - 37.50m), (c.ItbisRetenido, c.IsrRetenido, c.NetoPagar));
        Assert.Equal(Delphi("E410000000901.xml"),
            CompraInformalEcf.Xml(c, Caobas, new DateTime(2028, 12, 31), new DateTime(2026, 10, 17, 11, 37, 38)).TrimEnd());
    }

    [Fact]
    public void Las_revisiones_de_Stp_CrearCompraInformal()
    {
        var hoy = new DateTime(2026, 10, 23);
        var bien = new LineaCompra("aguacates", 1, 10, 25, false);
        var servicio = new LineaCompra("pintura de la fachada", 2, 1, 5000, false);
        string? V(string isr = "N", bool ver = true, string ced = "00113918239", LineaCompra[]? l = null, int dias = 0, int pago = 0) =>
            CompraInformalEcf.Validar(hoy.AddDays(dias), hoy.AddDays(dias + pago), ced, "JUAN PEREZ", null, ver, isr, "01", "09", null,
                l ?? [bien], hoy);
        Assert.Null(V());
        Assert.Null(V("T", l: [bien, servicio]));
        Assert.NotNull(V(ver: false));                      // sin verificar en la DGII
        Assert.NotNull(V(ced: "131475639"));                // un RNC: no es informal
        Assert.NotNull(V("N", l: [servicio]));              // servicio sin ISR: la DGII lo rechaza
        Assert.NotNull(V("P", l: [bien]));                  // ISR sin servicios
        Assert.NotNull(V(dias: 1));
        Assert.NotNull(V(pago: -1));
        Assert.NotNull(V(l: []));
        Assert.NotNull(V(l: [new LineaCompra("ab", 1, 1, 10, false)]));
    }
}

public class LineasCompraXmlTests
{
    [Fact]
    public void Las_lineas_van_como_las_lee_el_SP_y_escapan_las_comillas()
    {
        var xml = HotelWeb.Api.Ecf.RepositorioCompras.LineasXml([
            new LineaCompra("tubo 1/2\" & codo", 1, 2, 75.5m, true),
            new LineaCompra("mano de obra", 2, 1, 1500, false)]);
        Assert.Equal("<L d=\"tubo 1/2&quot; &amp; codo\" t=\"1\" c=\"2.00\" p=\"75.50\" g=\"1\" /><L d=\"mano de obra\" t=\"2\" c=\"1.00\" p=\"1500.00\" g=\"0\" />", xml);
    }
}
