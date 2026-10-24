using HotelWeb.Api.Catalogos;
using HotelWeb.Api.Seguridad;
using HotelWeb.Core.Catalogos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelWeb.Api.Controllers;

public sealed record SolicitudTipo(string Descripcion, string? Siglas, int Minimo, int Maximo)
{
    public DatosTipo Datos() => new(Descripcion, Siglas, Minimo, Maximo);
}

public sealed record PantallaTipos(IReadOnlyList<TipoHabitacion> Tipos, bool PuedeEditar);

/// <summary>La foto ya achicada por el navegador, en base64.</summary>
public sealed record SolicitudFoto(string Base64);

/// <summary>Tipos de habitacion (etapa 2). Todos consultan; crean y modifican los de Permisos.</summary>
[ApiController]
[Route("api/catalogos/tipos")]
public sealed class TiposController : ControllerBase
{
    private readonly RepositorioTipos _tipos;
    private readonly RepositorioFotosTipo _fotos;
    private readonly PermisosWeb _permisos;
    private readonly ILogger<TiposController> _log;

    public TiposController(RepositorioTipos tipos, RepositorioFotosTipo fotos, PermisosWeb permisos, ILogger<TiposController> log)
    {
        _tipos = tipos;
        _fotos = fotos;
        _permisos = permisos;
        _log = log;
    }

    /// <summary>Que tipos tienen foto (y su version).</summary>
    [HttpGet("fotos")]
    public ActionResult<IReadOnlyList<FotoTipo>> Fotos() => Ok(_fotos.Lista());

    /// <summary>
    /// La foto del tipo. Sin sesion: una etiqueta img no manda el token, y son fotos de las habitaciones (nada privado).
    /// Con ?v= (la version) el navegador la guarda; al cambiar la foto cambia la version.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("{id:int}/foto")]
    public IActionResult Foto(int id, [FromQuery] long? v)
    {
        if (_fotos.Leer(id) is not { } f) return NotFound();
        Response.Headers.CacheControl = v is null ? "no-cache" : "public, max-age=31536000, immutable";
        return File(f.Bytes, f.TipoContenido);
    }

    [HttpPut("{id:int}/foto")]
    public IActionResult GuardarFoto(int id, SolicitudFoto s)
    {
        var usuario = Tokens.IdDe(User);
        if (!_permisos.EditaCatalogos(usuario)) return StatusCode(403, "Su usuario no puede cambiar las fotos de los tipos.");
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(s.Base64 ?? "");
        }
        catch (FormatException)
        {
            return BadRequest("La foto no llego bien.");
        }
        try
        {
            var login = User.FindFirst("login")?.Value ?? usuario.ToString();
            _fotos.Guardar(id, bytes, login);
            _log.LogInformation("{Usuario} cambio la foto del tipo {Id} ({Bytes} bytes)", login, id, bytes.Length);
            return NoContent();
        }
        catch (ErrorCatalogo e)
        {
            return BadRequest(e.Message);
        }
    }

    [HttpDelete("{id:int}/foto")]
    public IActionResult QuitarFoto(int id)
    {
        var usuario = Tokens.IdDe(User);
        if (!_permisos.EditaCatalogos(usuario)) return StatusCode(403, "Su usuario no puede cambiar las fotos de los tipos.");
        if (!_fotos.Quitar(id)) return NotFound($"El tipo {id} no tiene foto.");
        _log.LogInformation("{Usuario} quito la foto del tipo {Id}", User.FindFirst("login")?.Value ?? usuario.ToString(), id);
        return NoContent();
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
