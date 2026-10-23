using HotelWeb.Core.Hospedaje;
using HotelWeb.Core.Rack;

namespace HotelWeb.Tests;

public class ReglasTransferenciaTests
{
    private static (LineaCuenta, string) L(int sec, string desc, decimal importe, string servicio = "1", bool activo = true) =>
        (new LineaCuenta(sec, desc, 1, importe, importe, activo, "x", null), servicio);

    // el saldo con las reglas de dbo.BalanceActualHabitacion
    private static decimal Saldo(IEnumerable<LineaCuenta> l, bool itbis, bool propina, bool e44 = false) =>
        CalculoCuenta.Calcular(l, itbis, propina, e44).Saldo;

    [Fact]
    public void Solo_cargos_pasan_en_una_linea_y_el_origen_queda_en_cero()
    {
        var o = new[] { L(1, "HOSPEDAJE -> 17/6/2026", 2734.37m), L(2, "BAR", 500m, "5") };
        var p = ReglasTransferencia.Planear(o, 103, 9829, true, true, false);
        var d = Assert.Single(p.Destino);
        Assert.Equal(("9", 3234.37m), (d.Servicio, d.Importe));
        Assert.StartsWith("Transf.de Hab.#103", d.Descripcion);
        Assert.Equal(-4140.00m, p.Cierre);   // 3234.37 + 582.19 + 323.44
        Assert.Equal(0, Saldo(o.Select(x => x.Item1).Append(new LineaCuenta(9, "cierre", 1, p.Cierre, p.Cierre, true, null, null)), true, true));
    }

    [Fact]
    public void Los_pagos_y_adelantos_pasan_como_pagos_y_el_payout_aparte()
    {
        var o = new[]
        {
            L(1, "HOSPEDAJE -> 17/6/2026", 3125m), L(2, "ABONO/ADELANTO EN RESERVA DOC# 5", -1000m, "11"),
            L(3, "PAGO RECIBO INGRESO # 0008050( EFECTIVO", -2000m, "3"), L(4, "PAY-OUT taxi", 300m, "7"),
            L(5, "BAR anulado", 999m, "5", activo: false),
        };
        var p = ReglasTransferencia.Planear(o, 107, 9842, true, true, false);
        Assert.Equal((3125m, 300m, -3000m), (p.Cargos, p.PayOut, p.Pagos));
        Assert.Equal(new[] { 2, 3 }, p.PagosOrigen);
        Assert.Equal(4, p.Destino.Count);
        Assert.Equal(new[] { "9", "7", "11", "3" }, p.Destino.Select(x => x.Servicio));
        Assert.Equal(-1000m, p.Destino[2].Importe);
        // el origen: sin sus pagos (inactivos) y con el cierre, queda en 0
        var queda = o.Select(x => x.Item1).Select(l => p.PagosOrigen.Contains(l.Secuencia) ? l with { Activo = false } : l)
            .Append(new LineaCuenta(9, "cierre", 1, p.Cierre, p.Cierre, true, null, null));
        Assert.Equal(0, Saldo(queda, true, true));
    }

    [Theory]
    [InlineData(false, true, false)]   // solo propina: Delphi dejaba saldo
    [InlineData(true, true, true)]     // E44: sin ITBIS
    [InlineData(false, false, false)]  // sin impuestos
    public void El_origen_queda_en_cero_con_cualquier_cliente(bool itbis, bool propina, bool e44)
    {
        var o = new[] { L(1, "HOSPEDAJE -> 1/7/2026", 2734.375m), L(2, "LAVANDERIA", 333.33m, "4") };
        var p = ReglasTransferencia.Planear(o, 1, 1, itbis, propina, e44);
        Assert.Equal(0, Saldo(o.Select(x => x.Item1).Append(new LineaCuenta(9, "c", 1, p.Cierre, p.Cierre, true, null, null)), itbis, propina, e44));
    }

    [Fact]
    public void Valida()
    {
        Assert.NotNull(ReglasTransferencia.Validar(103, 103, false, false, true));
        Assert.NotNull(ReglasTransferencia.Validar(103, 221, true, false, true));
        Assert.NotNull(ReglasTransferencia.Validar(103, 221, false, true, true));
        Assert.NotNull(ReglasTransferencia.Validar(103, 221, false, false, false));
        Assert.Null(ReglasTransferencia.Validar(103, 221, false, false, true));
    }
}
