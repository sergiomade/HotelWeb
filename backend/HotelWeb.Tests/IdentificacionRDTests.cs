using HotelWeb.Core.Identificacion;

namespace HotelWeb.Tests;

public class IdentificacionRDTests
{
    [Theory]
    [InlineData("130798362")]     // cliente 3 de Hotel_new
    [InlineData("131032494")]     // Ferrecomercial Sanchez
    [InlineData("133367076")]     // Massoti
    [InlineData("1-30-79836-2")]  // con guiones
    public void RNC_validos(string rnc) => Assert.Equal(TipoIdentificacion.Rnc, IdentificacionRD.Determinar(rnc, out _));

    [Theory]
    [InlineData("130798363")]
    [InlineData("13079836")]
    public void RNC_con_el_digito_malo_o_corto(string rnc) => Assert.False(IdentificacionRD.EsRncOCedulaValido(rnc));

    [Fact]
    public void Cedula_con_su_digito_verificador()
    {
        Assert.Equal(TipoIdentificacion.Cedula, IdentificacionRD.Determinar("001-1391820-5", out var limpio));
        Assert.Equal("00113918205", limpio);
        Assert.Equal(TipoIdentificacion.Invalido, IdentificacionRD.Determinar("00113918204", out _));
    }

    [Theory]
    [InlineData("000000000")]
    [InlineData("00000000000")]
    [InlineData("")]
    [InlineData(null)]
    public void Ceros_o_vacio_es_sin_identificacion(string? v)
    {
        Assert.True(IdentificacionRD.SinIdentificacion(v));
        Assert.False(IdentificacionRD.EsRncOCedulaValido(v));
    }

    [Theory]
    [InlineData("B7199648")]
    [InlineData("ab123456")]
    public void Pasaporte(string v) => Assert.Equal(TipoIdentificacion.Pasaporte, IdentificacionRD.Determinar(v, out _));
}
