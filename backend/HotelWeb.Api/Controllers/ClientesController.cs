using HotelWeb.Api.Catalogos;
using HotelWeb.Api.Seguridad;
using HotelWeb.Core.Catalogos;
using Microsoft.AspNetCore.Mvc;

namespace HotelWeb.Api.Controllers;

public sealed record SolicitudCliente(string Nombre, string? Rnc, string? Cedula, string? Pasaporte, string? Direccion,
    string? Sector, string? Ciudad, string? Telefono, string? Contacto, string? Correo, int TipoComprobante, bool Itbis,
    bool Propina, decimal LimiteCredito, string? Condiciones, bool Extranjero, bool Activo)
{
    public DatosCliente Datos() => new(Nombre, Rnc, Cedula, Pasaporte, Direccion, Sector, Ciudad, Telefono, Contacto,
        Correo, TipoComprobante, Itbis, Propina, LimiteCredito, Condiciones, Extranjero, Activo);
}

public sealed record TipoComprobante(int Id, string Nombre, bool PideRnc);

public sealed record PantallaClientes(PaginaClientes Pagina, IReadOnlyList<TipoComprobante> Comprobantes, bool PuedeEditar);

/// <summary>Clientes (etapa 2). Todos buscan y consultan; crean y modifican los de Permisos. No se borran.</summary>
[ApiController]
[Route("api/catalogos/clientes")]
public sealed class ClientesController : ControllerBase
{
    private readonly RepositorioClientes _clientes;
    private readonly PermisosWeb _permisos;
    private readonly ILogger<ClientesController> _log;

    public ClientesController(RepositorioClientes clientes, PermisosWeb permisos, ILogger<ClientesController> log)
    {
        _clientes = clientes;
        _permisos = permisos;
        _log = log;
    }

    [HttpGet]
    public ActionResult<PantallaClientes> Buscar([FromQuery] string? buscar) => Ok(new PantallaClientes(
        _clientes.Buscar(buscar),
        ReglasCliente.Comprobantes.Select(c => new TipoComprobante(c.Key, c.Value, ReglasCliente.PideRnc(c.Key))).ToList(),
        _permisos.EditaClientes(Tokens.IdDe(User))));

    [HttpGet("{numero:long}")]
    public ActionResult<Cliente> Obtener(long numero) =>
        _clientes.Obtener(numero) is { } c ? Ok(c) : NotFound($"No existe el cliente {numero}.");

    [HttpPut("{numero:long}")]
    public ActionResult<ClienteGuardado> Modificar(long numero, SolicitudCliente s) => Escribir(login =>
    {
        var r = _clientes.Modificar(numero, s.Datos(), login);
        _log.LogInformation("{Usuario} modifico el cliente {Numero} {Nombre}", login, numero, r.Cliente.Nombre);
        return r;
    });

    [HttpPost]
    public ActionResult<ClienteGuardado> Crear(SolicitudCliente s) => Escribir(login =>
    {
        var r = _clientes.Crear(s.Datos(), login);
        _log.LogInformation("{Usuario} creo el cliente {Numero} {Nombre}", login, r.Cliente.Numero, r.Cliente.Nombre);
        return r;
    });

    private ActionResult<ClienteGuardado> Escribir(Func<string, ClienteGuardado> accion)
    {
        var id = Tokens.IdDe(User);
        if (!_permisos.EditaClientes(id)) return StatusCode(403, "Su usuario no puede modificar los clientes.");
        try
        {
            return Ok(accion(User.FindFirst("login")?.Value ?? id.ToString()));
        }
        catch (ErrorCatalogo e)
        {
            return BadRequest(e.Message);
        }
    }
}
