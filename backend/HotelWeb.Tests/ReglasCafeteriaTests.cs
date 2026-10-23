using HotelWeb.Core.Cafeteria;

namespace HotelWeb.Tests;

public class ReglasCafeteriaTests
{
    private static LineaMesa L(decimal cantidad, decimal precio, bool impreso = false, int sec = 1) =>
        new(sec, "2109", "AGUA", cantidad, precio, ReglasCafeteria.Importe(cantidad, precio), 1, impreso);

    private static TotalesMesa T(decimal subtotal, bool itbis = true, bool propina = true) =>
        ReglasCafeteria.Totales([L(1, subtotal)], itbis, propina, 0.18m, 0.10m);

    [Theory]
    // facturas reales de Delphi (FaTmpFactura 12791, 12790, 12788, 12784, 12785): subtotal, ITBIS, propina, total
    [InlineData(46.88, 8.43, 4.68, 60.00)]
    [InlineData(609.38, 109.68, 60.93, 780.00)]
    [InlineData(124.99, 22.49, 12.49, 159.98)]
    [InlineData(1335.96, 240.47, 133.59, 1710.02)]
    [InlineData(214065.20, 38531.73, 21406.52, 274003.45)]
    public void Totales_como_Delphi(decimal sub, decimal itbis, decimal propina, decimal total)
    {
        var t = T(sub);
        Assert.Equal(sub, t.SubTotal);
        Assert.Equal(itbis, t.Itbis);
        Assert.Equal(propina, t.Propina);
        Assert.Equal(total, t.Total);
    }

    [Fact]
    public void Sin_itbis_o_sin_propina_segun_el_cliente()
    {
        var t = T(100, itbis: false);
        Assert.Equal((0m, 10m, 110m), (t.Itbis, t.Propina, t.Total));
        t = T(100, propina: false);
        Assert.Equal((18m, 0m, 118m), (t.Itbis, t.Propina, t.Total));
    }

    [Fact]
    public void Importe_es_cantidad_por_precio()
    {
        Assert.Equal(281.25m, ReglasCafeteria.Importe(1, 93.75m) * 3);
        Assert.Equal(1172.50m, ReglasCafeteria.Importe(5, 234.50m));
        var t = ReglasCafeteria.Totales([L(2, 93.75m), L(1, 46.88m, sec: 2)], true, true, 0.18m, 0.10m);
        Assert.Equal(234.38m, t.SubTotal);
    }

    [Fact]
    public void Articulo_con_fracciones_entra_por_trago()
    {
        // DEWARS: botella 1687.50, trago 234.38, 11 tragos; costo 1100
        var dewars = new PrecioArticulo("0203", "DEWARS", 1687.50m, 234.38m, 1100m, 11);
        Assert.Equal(new PrecioLinea(234.38m, 2, 100m), ReglasCafeteria.Precio(dewars));
        Assert.Equal(new PrecioLinea(1687.50m, 1, 1100m), ReglasCafeteria.Precio(dewars, porFraccion: false));

        var refresco = new PrecioArticulo("0704", "REFRESCO", 93.75m, 0, 20m, 1);
        Assert.Equal(new PrecioLinea(93.75m, 1, 20m), ReglasCafeteria.Precio(refresco));
        Assert.Null(ReglasCafeteria.Precio(refresco, porFraccion: true));   // no se vende por trago

        Assert.Null(ReglasCafeteria.Precio(new PrecioArticulo("0102", "0", 0, 0, 0, 11), porFraccion: false));   // sin precio
    }

    [Fact]
    public void Cantidad_entera_de_1_a_999()
    {
        Assert.Null(ReglasCafeteria.ValidarCantidad(1));
        Assert.Null(ReglasCafeteria.ValidarCantidad(500));   // factura 12785: 500 DEWARS
        Assert.NotNull(ReglasCafeteria.ValidarCantidad(0));
        Assert.NotNull(ReglasCafeteria.ValidarCantidad(1.5m));
        Assert.NotNull(ReglasCafeteria.ValidarCantidad(1000));
    }

    [Fact]
    public void Articulo_especial_pide_descripcion_y_precio_de_1_o_mas()
    {
        Assert.Null(ReglasCafeteria.ValidarEspecial("Pescado del dia", 450));
        Assert.NotNull(ReglasCafeteria.ValidarEspecial("", 450));
        Assert.NotNull(ReglasCafeteria.ValidarEspecial("ab", 450));
        Assert.NotNull(ReglasCafeteria.ValidarEspecial("Pescado del dia", 0.50m));
        Assert.Equal("PECHUGA DE POLLO A LA PLA".Length, ReglasCafeteria.Descripcion("PECHUGA DE POLLO A LA PLANCHA CON TOSTONES").Length);
    }

    [Fact]
    public void Lo_impreso_queda_fijo_y_para_cobrar_todo_debe_estar_impreso()
    {
        Assert.Null(ReglasCafeteria.PuedeCambiar(L(1, 50)));
        Assert.NotNull(ReglasCafeteria.PuedeCambiar(L(1, 50, impreso: true)));
        Assert.Null(ReglasCafeteria.PuedeBorrar(L(1, 50)));
        Assert.NotNull(ReglasCafeteria.PuedeBorrar(L(1, 50, impreso: true)));

        Assert.True(ReglasCafeteria.PreCuentaAlDia([L(1, 50, true), L(1, 60, true, 2)]));
        Assert.False(ReglasCafeteria.PreCuentaAlDia([L(1, 50, true), L(1, 60, false, 2)]));   // se agrego algo despues
        Assert.False(ReglasCafeteria.PreCuentaAlDia([]));
    }

    [Fact]
    public void Unir_cuentas_pone_las_lineas_al_final()
    {
        var destino = new[] { L(1, 50, sec: 1), L(1, 60, sec: 3) };
        var origen = new[] { L(2, 10, true, 2), L(1, 20, false, 1) };
        var u = ReglasCafeteria.Unir(destino, origen);
        Assert.Equal([1, 3, 4, 5], u.Select(l => l.Secuencia));
        Assert.Equal([50m, 60m, 20m, 10m], u.Select(l => l.Precio));
        Assert.True(u[3].Impreso);
    }
}
