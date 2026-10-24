using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HotelWeb.Api.Seguridad;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HotelWeb.Tests;

/// <summary>
/// Permisos de edicion (Permisos:EditanCatalogos) y de seguridad. El permiso se revisa antes de tocar los datos; con la
/// seguridad sin activar en la base (modulo 40) valen las listas de appsettings.
/// </summary>
public class PermisosTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Clave = "clave-de-las-pruebas-de-permisos-0123456789";
    private readonly WebApplicationFactory<Program> _f;

    public PermisosTests(WebApplicationFactory<Program> f) =>
        _f = f.WithWebHostBuilder(b => b.UseEnvironment("Development").UseSetting("Jwt:Key", Clave)
            // una base de seguridad que no existe: las pruebas nunca escriben en la de desarrollo (al ver la
            // seguridad activada, PermisosWeb sincroniza las opciones) y siempre valen las listas de appsettings
            .UseSetting("ConnectionStrings:Seguridad", SinBase));

    public const string SinBase =
        "Server=.;Database=NoExiste_PruebasHotelWeb;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=3";

    private HttpClient Cliente(int usuarioId)
    {
        var c = _f.CreateClient();
        var token = new Tokens(Clave).Crear(new Usuario(usuarioId, "prueba", "Prueba", false, false));
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    [Fact]
    public async Task Un_usuario_que_no_esta_en_la_lista_no_cambia_precios()
    {
        var r = await Cliente(99).PutAsJsonAsync("/api/catalogos/precios/1/A", new { conImpuestos = 3500m, personaAdicional = 0m });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Ni_habitaciones()
    {
        var c = Cliente(99);
        var datos = new { numero = 999, tipo = 1, estado = 1, television = true, aireAcondicionado = true, cabana = false };
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PutAsJsonAsync("/api/catalogos/habitaciones/101", datos)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/catalogos/habitaciones", datos)).StatusCode);
    }

    [Fact]
    public async Task Ni_tipos()
    {
        var c = Cliente(99);
        var datos = new { descripcion = "PRUEBA", siglas = "PRU", minimo = 1, maximo = 2 };
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PutAsJsonAsync("/api/catalogos/tipos/1", datos)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/catalogos/tipos", datos)).StatusCode);
    }

    [Fact]
    public async Task Ni_servicios()
    {
        var c = Cliente(99);
        var datos = new { descripcion = "MINIBAR", precio = 100m, impuesto = true };
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PutAsJsonAsync("/api/catalogos/servicios/5", datos)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/catalogos/servicios", datos)).StatusCode);
    }

    [Fact]
    public async Task Ni_clientes()
    {
        var c = Cliente(99);
        var datos = new { nombre = "PRUEBA", tipoComprobante = 2, itbis = true, propina = true, limiteCredito = 0m, extranjero = false, activo = true };
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PutAsJsonAsync("/api/catalogos/clientes/3", datos)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/catalogos/clientes", datos)).StatusCode);
    }

    [Fact]
    public async Task Ni_anular_adelantos()
    {
        var r = await Cliente(99).PostAsync("/api/reservas/7324/adelantos/10773/anular", null);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public void Anular_adelantos_usa_la_lista_de_catalogos_si_no_tiene_la_suya()
    {
        var p = (PermisosWeb)_f.Services.GetService(typeof(PermisosWeb))!;
        Assert.True(p.AnulaAdelantos(4));
        Assert.False(p.AnulaAdelantos(2));
    }

    [Fact]
    public async Task Ni_anular_cargos()
    {
        var r = await Cliente(99).PostAsync("/api/cuentas/102/cargos/1/anular", null);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Ni_anular_recibos()
    {
        var r = await Cliente(99).PostAsync("/api/recibos/8048/anular", null);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Ni_administrar_la_seguridad()
    {
        var c = Cliente(99);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/seguridad")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync("/api/seguridad/activar", null)).StatusCode);
        var usuario = new { login = "prueba", nombre = "PRUEBA", clave = "clave1", cancelaFactura = false, grupos = new[] { 1 } };
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/seguridad/usuarios", usuario)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/seguridad/usuarios/existente", new { login = "x", grupos = new[] { 1 } })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PutAsJsonAsync("/api/seguridad/usuarios/1", new { nombre = "X", activo = false, cancelaFactura = false, grupos = Array.Empty<int>() })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PutAsJsonAsync("/api/seguridad/usuarios/1/clave", new { clave = "nueva1" })).StatusCode);
        var grupo = new { descripcion = "PRUEBA", activo = true, permisos = new[] { 4020 } };
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/seguridad/grupos", grupo)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PutAsJsonAsync("/api/seguridad/grupos/1", grupo)).StatusCode);
    }

    [Fact]
    public async Task Una_clave_nueva_invalida_no_se_guarda()
    {
        var r = await Cliente(99).PutAsJsonAsync("/api/auth/clave", new { actual = "x", nueva = "ab" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Sin_sesion_tampoco()
    {
        var r = await _f.CreateClient().PutAsJsonAsync("/api/catalogos/precios/1/A", new { conImpuestos = 3500m, personaAdicional = 0m });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(4, true)]
    [InlineData(6, true)]
    [InlineData(2, false)]
    public void La_lista_de_appsettings(int usuario, bool edita)
    {
        var p = _f.Services.GetService(typeof(PermisosWeb)) as PermisosWeb;
        Assert.NotNull(p);
        Assert.Equal(edita, p!.EditaCatalogos(usuario));
    }
}
