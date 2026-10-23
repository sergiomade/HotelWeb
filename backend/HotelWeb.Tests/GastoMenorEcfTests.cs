using HotelWeb.Core.Ecf;

namespace HotelWeb.Tests;

public class GastoMenorEcfTests
{
    [Fact]
    public void El_E43_es_igual_al_que_acepto_la_DGII()
    {
        // el E430000000001 de Sanchez (11/10/2026), armado con el mismo codigo de Delphi que el hotel
        var delphi = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Datos", "E430000000001_sanchez.xml")).TrimEnd();
        var emisor = new EmisorEcf("131032494", "Ferrecentro Emilio Sanchez, Srl", "Aut. Duarte Km. 14 Bo. Independencia", null);
        var g = new GastoMenor("E430000000001", 1, new DateTime(2026, 10, 11), "pago de peajes, viajes a san juan", 2, 1, 65, 65, "01", "02",
            "CHOFER", null, "para registrar el gasto de pago de peajes.", null);
        Assert.Equal(delphi, GastoMenorEcf.Xml(g, emisor, new DateTime(2028, 12, 31), new DateTime(2026, 10, 11, 14, 48, 55)).TrimEnd());
    }

    [Fact]
    public void Las_revisiones_de_Stp_CrearGastoMenor()
    {
        var hoy = new DateTime(2026, 10, 23);
        string? V(string desc = "peaje autopista", int bs = 2, decimal c = 1, decimal p = 100, string fp = "01", string tg = "02",
            string ben = "JUAN PEREZ", string det = "peaje del viaje a Santiago", int dias = 0) =>
            GastoMenorEcf.Validar(desc, bs, c, p, fp, tg, ben, det, hoy.AddDays(dias), hoy);
        Assert.Null(V());
        Assert.NotNull(V(desc: "ab"));
        Assert.NotNull(V(bs: 3));
        Assert.NotNull(V(c: 0));
        Assert.NotNull(V(p: 0));
        Assert.NotNull(V(fp: "05"));
        Assert.NotNull(V(tg: "12"));
        Assert.NotNull(V(ben: "x"));
        Assert.NotNull(V(det: "corto"));
        Assert.NotNull(V(dias: 1));
        Assert.Equal(337.50m, GastoMenorEcf.Total(2.5m, 135));
    }
}
