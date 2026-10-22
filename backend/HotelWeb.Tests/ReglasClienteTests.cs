using HotelWeb.Core.Catalogos;

namespace HotelWeb.Tests;

public class ReglasClienteTests
{
    private static DatosCliente D(string nombre = "Cliente", string? rnc = null, int tipo = 2, string? correo = null,
        bool itbis = true) =>
        new(nombre, rnc, null, null, null, null, null, null, null, correo, tipo, itbis, true, 0, null, false, true);

    private static ResultadoValidacion V(DatosCliente d, int? actual = null) => ReglasCliente.Validar(ReglasCliente.Normalizar(d), actual);

    [Fact]
    public void Como_Delphi_nombre_en_mayusculas_y_rnc_vacio_en_ceros()
    {
        var d = ReglasCliente.Normalizar(D(" juan perez ", rnc: ""));
        Assert.Equal("JUAN PEREZ", d.Nombre);
        Assert.Equal("000000000", d.Rnc);
        Assert.Equal("sin direccion", d.Direccion);
        Assert.Equal("30 Dias", d.Condiciones);
    }

    [Fact]
    public void Consumo_sin_rnc_vale() => Assert.Null(V(D()).Error);

    [Fact]
    public void Credito_fiscal_pide_rnc_valido()
    {
        Assert.NotNull(V(D(tipo: 1)).Error);
        Assert.NotNull(V(D(rnc: "130798363", tipo: 1)).Error);
        Assert.Null(V(D(rnc: "1-30-79836-2", tipo: 1)).Error);
    }

    [Fact]
    public void Consumo_con_rnc_de_digito_malo_solo_avisa()
    {
        var r = V(D(rnc: "130798363"));
        Assert.Null(r.Error);
        Assert.Single(r.Avisos);
    }

    [Fact]
    public void El_rnc_lleva_9_u_11_digitos() => Assert.NotNull(V(D(rnc: "12345")).Error);

    [Fact]
    public void Un_correo_que_no_es_correo_solo_avisa()
    {
        var r = V(D(correo: "N/A"));
        Assert.Null(r.Error);
        Assert.Contains(r.Avisos, a => a.Contains("correo"));
        Assert.Empty(V(D(correo: "reservas@lascaobas.com.do")).Avisos);
    }

    [Fact]
    public void Un_tipo_que_no_es_de_la_lista_solo_si_ya_lo_tenia()
    {
        // el cliente 2 (CLIENTE HABITACION) tiene el 16
        Assert.NotNull(V(D(tipo: 16)).Error);
        Assert.Null(V(D(tipo: 16), actual: 16).Error);
    }

    [Fact]
    public void Regimen_especial_con_itbis_avisa() =>
        Assert.Contains(V(D(rnc: "130798362", tipo: 13)).Avisos, a => a.Contains("E44"));

    [Theory]
    [InlineData("Activo", true)]
    [InlineData("A", true)]
    [InlineData("Inactivo", false)]
    [InlineData("0", true)]    // 54 clientes viejos: Delphi solo avisa si dice "Inactivo"
    public void Estatus(string e, bool activo) => Assert.Equal(activo, ReglasCliente.EsActivo(e));
}
