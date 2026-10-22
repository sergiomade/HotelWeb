using HotelWeb.Api.Catalogos;
using HotelWeb.Api.Seguridad;
using HotelWeb.Core.Catalogos;
using Microsoft.AspNetCore.Mvc;

namespace HotelWeb.Api.Controllers;

public sealed record SolicitudTipo(string Descripcion, string? Siglas, int Minimo, int Maximo)
{
    public DatosTipo Datos() => new(Descripcion, Siglas, Minimo, Maximo);
}

public sealed record PantallaTipos(IReadOnlyList<TipoHabitacion> Tipos, bool PuedeEditar);

/// <summary>Tipos de habitacion (etapa 2). Todos consultan; crean y modifican los de Permisos.</summary>
[ApiController]
[Route("api/catalogos/tipos")]
public sealed class TiposController : ControllerBase
{
    private readonly RepositorioTipos _tipos;
    private readonly PermisosWeb _permisos;
    private readonly ILogger<TiposController> _log;

    public TiposController(RepositorioTipos tipos, PermisosWeb permisos, ILogger<TiposController> log)
    {
        _tipos = tipos;
        _permisos = permisos;
        _log = log;
    }

    [HttpGet]
    public ActionResult<PantallaTipos> Obtener() =>
        Ok(new PantallaTipos(_tipos.Obtener(), _permisos.EditaCatalogos(Tokens.IdDe(User))));

    [HttpPut("{id:int}")]
    public ActionResult<TipoHabitacion> Modificar(int id, SolicitudTipo s) => Escribir(login =>
    {
        var t = _tipos.Modificar(id, s.Datos(), login);
        _log.LogInformation("{Usuario} modifico el tipo {Id} {Descripcion}", login, id, t.Descripcion);
        return t;
    });

    [HttpPost]
    public ActionResult<TipoHabitacion> Crear(SolicitudTipo s) => Escribir(login =>
    {
        var t = _tipos.Crear(s.Datos(), login);
        _log.LogInformation("{Usuario} creo el tipo {Id} {Descripcion}", login, t.Id, t.Descripcion);
        return t;
    });

    private ActionResult<TipoHabitacion> Escribir(Func<string, TipoHabitacion> accion)
    {
        var id = Tokens.IdDe(User);
        if (!_permisos.EditaCatalogos(id)) return StatusCode(403, "Su usuario no puede modificar los tipos de habitacion.");
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
