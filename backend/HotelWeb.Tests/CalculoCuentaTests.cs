using HotelWeb.Core.Rack;

namespace HotelWeb.Tests;

public class CalculoCuentaTests
{
    private static LineaCuenta L(string d, decimal importe, bool activo = true) =>
        new(1, d, 1, importe, importe, activo, "u", null);

    [Fact]
    public void Hospedaje_con_itbis_y_propina_como_la_habitacion_102()
    {
        // factura 9824 de Hotel_new: 2734.37 -> BalanceActualHabitacion = 3500.00
        var t = CalculoCuenta.Calcular(new[] { L("HOSPEDAJE -> 17/6/2026", 2734.37m) }, true, true, false);
        Assert.Equal(2734.37m, t.Cargos);
        Assert.Equal(492.19m, t.Itbis);
        Assert.Equal(273.44m, t.Propina);
        Assert.Equal(3500m, t.Saldo);
    }

    [Fact]
    public void Los_pagos_restan_sin_impuestos()
    {
        var t = CalculoCuenta.Calcular(new[] { L("HOSPEDAJE", 2500m), L("PAGO RECIBO INGRESO # 1", -3200m) }, true, true, false);
        Assert.Equal(-3200m, t.PagosYPayOut);
        Assert.Equal(0m, t.Saldo);   // 2500 * 1.28 = 3200
    }

    [Fact]
    public void Regimen_especial_no_lleva_itbis_pero_si_propina()
    {
        var t = CalculoCuenta.Calcular(new[] { L("HOSPEDAJE", 1000m) }, true, true, regimenEspecial: true);
        Assert.Equal(0m, t.Itbis);
        Assert.Equal(100m, t.Propina);
        Assert.Equal(1100m, t.Saldo);
    }

    [Fact]
    public void El_pay_out_y_las_lineas_inactivas_no_llevan_impuestos()
    {
        var t = CalculoCuenta.Calcular(new[] { L("HOSPEDAJE", 1000m), L("PAY-OUT TAXI", 300m), L("ANULADA", 999m, activo: false) },
            true, false, false);
        Assert.Equal(1000m, t.Cargos);
        Assert.Equal(300m, t.PagosYPayOut);
        Assert.Equal(1480m, t.Saldo);
    }

    [Fact]
    public void Se_redondea_a_pesos_como_TSQL()
    {
        // 1.25 * 1.18 = 1.475 -> 1 ; 0.5 exacto -> 1 (lejos del cero)
        Assert.Equal(1m, CalculoCuenta.Calcular(new[] { L("X", 1.25m) }, true, false, false).Saldo);
        Assert.Equal(1m, CalculoCuenta.Calcular(new[] { L("X", 0.5m) }, false, false, false).Saldo);
    }
}
