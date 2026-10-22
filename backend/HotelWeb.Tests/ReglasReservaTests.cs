using HotelWeb.Core.Reservas;

namespace HotelWeb.Tests;

public class ReglasReservaTests
{
    private static readonly DateTime Hoy = new(2026, 10, 22, 16, 0, 0);

    private static LineaReserva L(int tipo = 2, int cant = 1, int adultos = 2, decimal tarifa = 3125m) =>
        new(tipo, cant, adultos, 0, 0, true, "a", tarifa);

    private static DatosReserva R(DateTime? entrada = null, DateTime? salida = null, params LineaReserva[] lineas) => new(
        " empresa x ", null, null, null, " juan ", null, null, null,
        entrada ?? Hoy.AddDays(1), salida ?? Hoy.AddDays(3), null, 1, "A",
        lineas.Length == 0 ? new[] { L() } : lineas);

    private static string? V(DatosReserva d, bool nueva = true) => ReglasReserva.Validar(ReglasReserva.Normalizar(d), Hoy, nueva);

    [Fact]
    public void Noches_e_importe_como_Delphi()
    {
        Assert.Equal(2, ReglasReserva.Noches(Hoy.AddDays(1), Hoy.AddDays(3)));
        Assert.Equal(12500m, ReglasReserva.Importe(L(cant: 2), 2));   // 2 hab x 3125 x 2 noches
    }

    [Fact]
    public void Cuenta_habitaciones_y_personas()
    {
        var lineas = new[] { L(cant: 2, adultos: 2), L(tipo: 1, cant: 1, adultos: 1) };
        Assert.Equal(3, ReglasReserva.Habitaciones(lineas));
        Assert.Equal(5, ReglasReserva.Personas(lineas));
    }

    [Fact]
    public void Normaliza_en_mayusculas_y_sin_horas()
    {
        var d = ReglasReserva.Normalizar(R());
        Assert.Equal("EMPRESA X", d.NombreCliente);
        Assert.Equal("JUAN", d.Huesped);
        Assert.Equal("A", d.Lineas[0].Categoria);
        Assert.Equal(TimeSpan.Zero, d.Entrada.TimeOfDay);
    }

    [Fact]
    public void Una_reserva_normal_vale() => Assert.Null(V(R()));

    [Fact]
    public void La_salida_despues_de_la_entrada() => Assert.NotNull(V(R(Hoy.AddDays(3), Hoy.AddDays(3))));

    [Fact]
    public void No_se_reserva_para_el_pasado_pero_se_puede_modificar_una_vieja()
    {
        Assert.NotNull(V(R(Hoy.AddDays(-2), Hoy.AddDays(1))));
        Assert.Null(V(R(Hoy.AddDays(-2), Hoy.AddDays(1)), nueva: false));
        Assert.Null(V(R(Hoy.Date, Hoy.AddDays(1))));   // hoy si
    }

    [Theory]
    [InlineData(0, 1, 2)]
    [InlineData(2, 0, 2)]
    [InlineData(2, 1, 0)]
    public void Lineas_que_no_valen(int tipo, int cant, int adultos) =>
        Assert.NotNull(V(R(null, null, new LineaReserva(tipo, cant, adultos, 0, 0, true, "A", 100))));

    [Fact]
    public void Sin_lineas_no_vale() =>
        Assert.NotNull(ReglasReserva.Validar(R() with { Lineas = Array.Empty<LineaReserva>() }, Hoy, true));

    [Fact]
    public void Se_cruzan_las_fechas_pero_el_dia_de_salida_queda_libre()
    {
        var e = Hoy.Date;
        Assert.True(ReglasReserva.SeCruzan(e, e.AddDays(3), e.AddDays(2), e.AddDays(5)));
        Assert.False(ReglasReserva.SeCruzan(e, e.AddDays(3), e.AddDays(3), e.AddDays(5)));
    }

    [Fact]
    public void Avisa_la_sobreventa()
    {
        var libres = new[] { new Disponibilidad(2, "DOBLE", 26, 20, 5) };   // queda 1
        Assert.Empty(ReglasReserva.Sobreventa(R(null, null, L(cant: 1)), libres));
        Assert.Single(ReglasReserva.Sobreventa(R(null, null, L(cant: 2)), libres));
        Assert.Single(ReglasReserva.Sobreventa(R(null, null, L(tipo: 9, cant: 1)), libres));   // tipo sin datos: 0 libres
    }

    [Fact]
    public void Adelantos()
    {
        Assert.Null(ReglasAdelanto.Validar(2000, 1, null, EstadoReserva.Activa));
        Assert.NotNull(ReglasAdelanto.Validar(0, 1, null, EstadoReserva.Activa));
        Assert.NotNull(ReglasAdelanto.Validar(2000, 2, null, EstadoReserva.Activa));   // dolares sin US$
        Assert.Null(ReglasAdelanto.Validar(6000, 2, 100, EstadoReserva.Activa));
        Assert.NotNull(ReglasAdelanto.Validar(2000, 1, null, EstadoReserva.Hospedado));
        Assert.Null(ReglasAdelanto.ValidarAnular("Activa    ", EstadoReserva.Activa));
        Assert.NotNull(ReglasAdelanto.ValidarAnular("Anulada", EstadoReserva.Activa));
        Assert.NotNull(ReglasAdelanto.ValidarAnular("Activa", EstadoReserva.Hospedado));
    }
}
