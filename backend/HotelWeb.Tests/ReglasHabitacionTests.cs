using HotelWeb.Core.Catalogos;

namespace HotelWeb.Tests;

public class ReglasHabitacionTests
{
    private static CambiosHabitacion C(int tipo = 1, int estado = 1, string? comentario = null, string? ext = null) =>
        new(tipo, estado, comentario, ext, true, true, false);

    [Fact]
    public void Una_habitacion_normal_vale() => Assert.Null(ReglasHabitacion.Validar(C(comentario: "aire dañado", ext: "103")));

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void Pide_tipo_y_estado(int tipo, int estado) => Assert.NotNull(ReglasHabitacion.Validar(C(tipo, estado)));

    [Fact]
    public void Extension_de_4_y_comentario_de_500()
    {
        Assert.NotNull(ReglasHabitacion.Validar(C(ext: "12345")));
        Assert.Null(ReglasHabitacion.Validar(C(ext: " 1234 ")));
        Assert.NotNull(ReglasHabitacion.Validar(C(comentario: new string('a', 501))));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100_000)]
    public void Numero_de_la_nueva(int numero) => Assert.NotNull(ReglasHabitacion.ValidarNueva(numero, 5, 65, C()));

    [Fact]
    public void Vacio_queda_NULL() => Assert.Null(ReglasHabitacion.Limpio("   "));

    [Fact]
    public void La_nueva_va_al_final_de_la_fila_de_su_piso()
    {
        var hay = new List<(int, int, int)> { (101, 5, 65), (102, 88, 65), (103, 171, 65), (201, 5, 330) };
        Assert.Equal((254, 65), ReglasHabitacion.PosicionSugerida(104, hay));
        Assert.Equal((88, 330), ReglasHabitacion.PosicionSugerida(202, hay));
    }

    [Fact]
    public void Un_piso_sin_fila_abre_una_fila_nueva_debajo()
    {
        var hay = new List<(int, int, int)> { (101, 5, 65), (201, 5, 330) };
        Assert.Equal((5, 420), ReglasHabitacion.PosicionSugerida(301, hay));
    }
}
