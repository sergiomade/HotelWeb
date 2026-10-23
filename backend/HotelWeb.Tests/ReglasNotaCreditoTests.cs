using HotelWeb.Core.Hospedaje;

namespace HotelWeb.Tests;

public class ReglasNotaCreditoTests
{
    private static string? V(int codigo, decimal monto = 0, string motivo = "error en el monto cobrado", string encf = "E320000006069",
        bool aceptada = true, decimal pendiente = 4000, bool tieneNotas = false) =>
        ReglasNotaCredito.Validar(codigo, monto, motivo, encf, aceptada, 4000, pendiente, tieneNotas);

    [Fact]
    public void Anulacion_total_solo_sin_notas_previas_y_aceptada()
    {
        Assert.Null(V(1));
        Assert.NotNull(V(1, tieneNotas: true));
        Assert.NotNull(V(1, aceptada: false));
        Assert.NotNull(V(1, encf: "E340000000001"));
        Assert.NotNull(V(1, encf: "B0200000001"));
        Assert.NotNull(V(1, motivo: "corto"));
    }

    [Fact]
    public void Nota_por_monto_hasta_lo_pendiente()
    {
        Assert.Null(V(3, 1000));
        Assert.Null(V(3, 4000));
        Assert.NotNull(V(3, 4000.01m));
        Assert.NotNull(V(3, 0));
        Assert.NotNull(V(3, 500, pendiente: 0));
        Assert.Null(V(3, 500, pendiente: 3000, tieneNotas: true));   // con notas previas si se puede, hasta lo que falta
        Assert.Equal(3000m, ReglasNotaCredito.Pendiente(4000, new[] { 600m, 400m }));
    }
}
