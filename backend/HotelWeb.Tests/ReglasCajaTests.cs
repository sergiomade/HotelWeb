using HotelWeb.Core.Caja;

namespace HotelWeb.Tests;

public class ReglasCajaTests
{
    private static MovimientoCaja H(int forma, decimal monto, decimal us = 0, long numero = 1) =>
        new('R', numero, DateTime.Today, "Hab. 102", "HUESPED", forma, $"F{forma}", monto, us);

    private static MovimientoCaja C(int forma, decimal monto, long factura = 1) =>
        new('F', factura, DateTime.Today, "Mesa 1", "CLIENTE", forma, $"F{forma}", monto, 0);

    [Fact]
    public void Hotel_efectivo_tarjeta_y_dolares_con_payout()
    {
        var r = ReglasCaja.Resumen(AmbitoCaja.Hotel, [
            H(1, 3500), H(1, 2300, numero: 2),   // efectivo
            H(4, 25900, numero: 3),              // tarjeta
            H(2, 5600, 100, numero: 4),          // 100 dolares (5,600 en pesos)
            H(7, 300, numero: 5),                // PAY-OUT: sale de la gaveta
            H(6, 37334, numero: 6),              // credito: no es dinero
            H(5, 8000, numero: 7),               // nota de credito: no es dinero
        ]);
        Assert.Equal(5500m, r.Efectivo);                       // 5,800 - 300
        Assert.Equal(100m, r.Dolares);                         // en US$, aparte (Delphi los sumaba con los pesos)
        Assert.Equal(5800m + 25900m + 5600m - 300m, r.Ingresos);
        Assert.Equal(45334m, r.SinDinero);
        Assert.Equal(2, r.Formas.Single(f => f.Forma == 1).Cantidad);
    }

    [Fact]
    public void Cafeteria_usa_sus_formas_de_pago()
    {
        // faFormadePago: 1 efectivo, 2 tarjeta, 3 credito, 8 cargo a la habitacion
        var r = ReglasCaja.Resumen(AmbitoCaja.Cafeteria, [C(1, 280), C(2, 500), C(3, 149.99m, 2), C(8, 562.50m, 3), C(1, 7596, 4)]);
        Assert.Equal(7876m, r.Efectivo);
        Assert.Equal(7876m + 500m, r.Ingresos);
        Assert.Equal(149.99m + 562.50m, r.SinDinero);
        Assert.Equal(0m, r.Dolares);
        Assert.Equal(ClaseForma.SinDinero, ReglasCaja.Clase(AmbitoCaja.Cafeteria, 8));
        Assert.Equal(ClaseForma.OtroDinero, ReglasCaja.Clase(AmbitoCaja.Hotel, 8));   // en el hotel el 8 es deposito
    }

    [Fact]
    public void Para_cerrar_con_diferencia_hay_que_explicarla()
    {
        var r = ReglasCaja.Resumen(AmbitoCaja.Hotel, [H(1, 1000), H(2, 560, 10, 2)]);
        Assert.Null(ReglasCaja.ValidarCierre(r, 2, 1000, 10, null));
        Assert.NotNull(ReglasCaja.ValidarCierre(r, 2, 950, 10, null));
        Assert.Null(ReglasCaja.ValidarCierre(r, 2, 950, 10, "faltan 50 de una devuelta"));
        Assert.NotNull(ReglasCaja.ValidarCierre(r, 2, 1000, 9, ""));
        Assert.NotNull(ReglasCaja.ValidarCierre(r, 0, 0, 0, null));       // nada que cerrar
        Assert.NotNull(ReglasCaja.ValidarCierre(r, 2, -1, 10, "x"));
        Assert.NotNull(ReglasCaja.ValidarCierre(r, 2, 1000.001m, 10, null));
    }
}
