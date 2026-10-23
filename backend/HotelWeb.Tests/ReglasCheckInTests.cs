using HotelWeb.Core.Hospedaje;

namespace HotelWeb.Tests;

public class ReglasCheckInTests
{
    // libres: 101 sencilla bloqueada, 103 y 104 matrimoniales, 107 doble, 109 doble
    private static readonly HabitacionLibre[] Libres =
    {
        new(101, 1, false), new(103, 3, true), new(104, 3, true), new(107, 2, true), new(109, 2, true),
    };

    [Fact]
    public void Cada_linea_se_reparte_en_su_cantidad()
    {
        var l = ReglasCheckIn.Lugares(new[] { (5, 2, 2), (6, 3, 1) });
        Assert.Equal(3, l.Count);
        Assert.Equal(new Lugar(5, 2, 2), l[1]);
    }

    [Fact]
    public void Sugiere_libres_del_tipo_reservado_sin_repetir()
    {
        var lugares = ReglasCheckIn.Lugares(new[] { (5, 2, 2), (6, 3, 1) });
        var s = ReglasCheckIn.Sugerir(lugares, Libres);
        Assert.Equal(new[] { 107, 109, 103 }, s.Select(a => a.Habitacion));
    }

    [Fact]
    public void No_sugiere_bloqueadas_ni_inventa_si_no_hay_del_tipo()
    {
        var s = ReglasCheckIn.Sugerir(ReglasCheckIn.Lugares(new[] { (1, 1, 1) }), Libres);
        Assert.Empty(s);   // la unica sencilla esta bloqueada
    }

    [Fact]
    public void Valida_lo_elegido()
    {
        var lugares = ReglasCheckIn.Lugares(new[] { (5, 2, 2) });
        Assert.Null(ReglasCheckIn.Validar(lugares, new[] { new Asignacion(5, 1, 107), new Asignacion(5, 2, 109) }, Libres));
        Assert.NotNull(ReglasCheckIn.Validar(lugares, new[] { new Asignacion(5, 1, 107) }, Libres));                              // falta una
        Assert.NotNull(ReglasCheckIn.Validar(lugares, new[] { new Asignacion(5, 1, 107), new Asignacion(5, 2, 107) }, Libres));   // repetida
        Assert.NotNull(ReglasCheckIn.Validar(lugares, new[] { new Asignacion(5, 1, 107), new Asignacion(5, 2, 102) }, Libres));   // no libre
        Assert.NotNull(ReglasCheckIn.Validar(ReglasCheckIn.Lugares(new[] { (1, 1, 1) }), new[] { new Asignacion(1, 1, 101) }, Libres)); // bloqueada
    }

    [Fact]
    public void Otro_tipo_se_permite_pero_avisa()
    {
        var lugares = ReglasCheckIn.Lugares(new[] { (5, 2, 1) });
        var elegidas = new[] { new Asignacion(5, 1, 103) };
        Assert.Null(ReglasCheckIn.Validar(lugares, elegidas, Libres));
        Assert.Single(ReglasCheckIn.Avisos(lugares, elegidas, Libres, t => $"tipo {t}"));
    }

    [Theory]
    [InlineData(2026, 10, 22, "HOSPEDAJE -> 22/10/2026")]
    [InlineData(2026, 7, 1, "HOSPEDAJE -> 1/7/2026")]
    public void Texto_de_la_noche_como_Delphi(int a, int m, int d, string texto) =>
        Assert.Equal(texto, ReglasHospedaje.Descripcion(new DateTime(a, m, d)));
}
