using HotelWeb.Api.Catalogos;
using HotelWeb.Api.Seguridad;
using HotelWeb.Core.Catalogos;
using Microsoft.AspNetCore.Mvc;

namespace HotelWeb.Api.Controllers;

public sealed record SolicitudServicio(string Descripcion, decimal Precio, bool Impuesto)
{
    public DatosServicio Datos() => new(Descripcion, Precio, Impuesto);
}

public sealed record PantallaServicios(IReadOnlyList<Servicio> Servicios, bool PuedeEditar);

/// <summary>Servicios que se cargan a las cuentas (etapa 2). Todos consultan; crean y modifican los de Permisos.</summary>
[ApiController]
[Route("api/catalogos/servicios")]
public sealed class ServiciosController : ControllerBase
{
    private readonly RepositorioServicios _servicios;
    private readonly PermisosWeb _permisos;
    private readonly ILogger<ServiciosController> _log;

    public ServiciosController(RepositorioServicios servicios, PermisosWeb permisos, ILogger<ServiciosController> log)
    {
        _servicios = servicios;
        _permisos = permisos;
        _log = log;
    }

    [HttpGet]
    public ActionResult<PantallaServicios> Obtener() =>
        Ok(new PantallaServicios(_servicios.Obtener(), _permisos.EditaCatalogos(Tokens.IdDe(User))));

    [HttpPut("{id:int}")]
    public ActionResult<Servicio> Modificar(int id, SolicitudServicio s) => Escribir(login =>
    {
        var r = _servicios.Modificar(id, s.Datos(), login);
        _log.LogInformation("{Usuario} modifico el servicio {Id} {Descripcion}: {Precio}", login, id, r.Descripcion, r.Precio);
        return r;
    });

    [HttpPost]
    public ActionResult<Servicio> Crear(SolicitudServicio s) => Escribir(login =>
    {
        var r = _servicios.Crear(s.Datos(), login);
        _log.LogInformation("{Usuario} creo el servicio {Id} {Descripcion}", login, r.Id, r.Descripcion);
        return r;
    });

    private ActionResult<Servicio> Escribir(Func<string, Servicio> accion)
    {
        var id = Tokens.IdDe(User);
        if (!_permisos.EditaCatalogos(id)) return StatusCode(403, "Su usuario no puede modificar los servicios.");
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
