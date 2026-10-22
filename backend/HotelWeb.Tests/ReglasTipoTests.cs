using HotelWeb.Core.Catalogos;

namespace HotelWeb.Tests;

public class ReglasTipoTests
{
    private static readonly (int, string)[] Hay = { (1, "SENCILLA"), (2, "DOBLE") };

    private static string? V(string desc, string? siglas, int min, int max, int? id = null) =>
        ReglasTipo.Validar(ReglasTipo.Normalizar(new DatosTipo(desc, siglas, min, max)), Hay, id);

    [Fact]
    public void Se_guarda_en_mayusculas_como_Delphi()
    {
        var d = ReglasTipo.Normalizar(new DatosTipo("  junior suite ", " js ", 2, 4));
        Assert.Equal("JUNIOR SUITE", d.Descripcion);
        Assert.Equal("JS", d.Siglas);
    }

    [Fact]
    public void Un_tipo_normal_vale() => Assert.Null(V("Familiar", "FAM", 2, 5));

    [Theory]
    [InlineData("", "X", 1, 1)]
    [InlineData("X", "ABCD", 1, 1)]
    [InlineData("X", "X", 0, 1)]
    [InlineData("X", "X", 3, 2)]
    [InlineData("X", "X", 1, 21)]
    public void Lo_que_no_vale(string d, string s, int min, int max) => Assert.NotNull(V(d, s, min, max));

    [Fact]
    public void No_repite_el_nombre_de_otro_tipo()
    {
        Assert.NotNull(V("doble", "DBL", 2, 2));            // nuevo con el nombre de otro
        Assert.Null(V("doble", "DBL", 2, 2, id: 2));        // el mismo tipo, guardado de nuevo
    }
}
