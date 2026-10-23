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

    [Fact]
    public void Si_se_respeta_el_precio_reservado_el_aviso_lo_dice()
    {
        var lugares = ReglasCheckIn.Lugares(new[] { (5, 2, 1) });
        var aviso = Assert.Single(ReglasCheckIn.Avisos(lugares, new[] { new Asignacion(5, 1, 103) }, Libres, t => $"tipo {t}", _ => true));
        Assert.Contains("respeta el precio reservado", aviso);
    }

    // tipos: 103 y 104 matrimoniales (3), 107 y 109 dobles (2), 111 junior suite (5)
    private static int TipoDe(int n) => n switch { 103 or 104 => 3, 107 or 109 => 2, 111 => 5, _ => 0 };

    [Fact]
    public void Sin_cambios_la_linea_queda_igual()
    {
        var linea = new LineaReservada(10, 2, "A", 3125, 2);
        var f = RepartoCheckIn.Repartir(new[] { linea }, new[] { new Asignacion(10, 1, 107), new Asignacion(10, 2, 109) }, TipoDe);
        Assert.Single(f);
        Assert.True(RepartoCheckIn.SinCambios(f[0], linea));
    }

    [Fact]
    public void Una_junior_suite_que_pasa_a_matrimonial_con_categoria_D_cambia_la_linea()
    {
        // lo que vio el usuario: reservo junior suite, no habia, la paso a matrimonial categoria D
        var linea = new LineaReservada(10, 5, "A", 3906.25m, 1);
        var f = RepartoCheckIn.Repartir(new[] { linea }, new[] { new Asignacion(10, 1, 103, "D", 2500m) }, TipoDe);
        var x = Assert.Single(f);
        Assert.False(x.Nueva);                     // la misma linea, actualizada
        Assert.Equal((3, "D", 2500m), (x.Tipo, x.Categoria, x.Tarifa));
        Assert.False(RepartoCheckIn.SinCambios(x, linea));
    }

    [Fact]
    public void Si_de_dos_cambia_una_la_linea_se_separa()
    {
        var linea = new LineaReservada(10, 2, "A", 3125, 2);
        var f = RepartoCheckIn.Repartir(new[] { linea },
            new[] { new Asignacion(10, 1, 107), new Asignacion(10, 2, 103, "A", 3125m) }, TipoDe);
        Assert.Equal(2, f.Count);
        var queda = f.Single(x => !x.Nueva);
        var nueva = f.Single(x => x.Nueva);
        Assert.Equal((2, 1), (queda.Tipo, queda.Cantidad));
        Assert.Equal((3, 1, 103), (nueva.Tipo, nueva.Cantidad, nueva.Habitaciones[0].Habitacion));
    }

    [Fact]
    public void Otra_tarifa_negociada_tambien_cambia_la_linea()
    {
        var linea = new LineaReservada(10, 2, "A", 3125, 1);
        var f = RepartoCheckIn.Repartir(new[] { linea }, new[] { new Asignacion(10, 1, 107, "A", 2900m) }, TipoDe);
        Assert.Equal(2900m, Assert.Single(f).Tarifa);
    }

    [Theory]
    [InlineData(2026, 10, 22, "HOSPEDAJE -> 22/10/2026")]
    [InlineData(2026, 7, 1, "HOSPEDAJE -> 1/7/2026")]
    public void Texto_de_la_noche_como_Delphi(int a, int m, int d, string texto) =>
        Assert.Equal(texto, ReglasHospedaje.Descripcion(new DateTime(a, m, d)));
}
