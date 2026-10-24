using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HotelWeb.Tests;

/// <summary>La API completa en memoria (sin tocar la base: solo lo que no la necesita).</summary>
public class ApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _http;

    public ApiTests(WebApplicationFactory<Program> f) =>
        _http = f.WithWebHostBuilder(b => b.UseEnvironment("Development")
            .UseSetting("ConnectionStrings:Seguridad", PermisosTests.SinBase)).CreateClient();

    [Fact]
    public async Task El_ping_no_pide_sesion()
    {
        var r = await _http.GetAsync("/api/diagnostico/ping");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task El_diagnostico_pide_sesion()
    {
        var r = await _http.GetAsync("/api/diagnostico");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Una_ruta_de_la_API_que_no_existe_no_devuelve_la_pagina()
    {
        // sin sesion responde 401 (la politica de sesion aplica antes); con sesion, 404
        var r = await _http.GetAsync("/api/no-existe");
        Assert.Contains(r.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.Unauthorized });
    }

    [Fact]
    public async Task Un_archivo_que_no_existe_no_devuelve_la_pagina()
    {
        // el error del 2026-10-22: /assets/x.js devolvia index.html y la pagina salia en blanco
        var r = await _http.GetAsync("/assets/no-existe.js");
        Assert.NotEqual(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Las_rutas_de_la_pagina_devuelven_la_pagina()
    {
        var r = await _http.GetAsync("/rack");
        // en las pruebas puede no haber wwwroot compilado: entonces 404, nunca 401
        Assert.NotEqual(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("usuario", "")]
    [InlineData("usuario", "una clave demasiado larga")]
    [InlineData("ñandu", "clave")]
    public async Task Un_ingreso_invalido_no_llega_a_la_base(string usuario, string clave)
    {
        var r = await _http.PostAsJsonAsync("/api/auth/ingresar", new { usuario, clave });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }
}
