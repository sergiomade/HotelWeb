using HotelWeb.Api.Seguridad;
using HotelWeb.Core.Seguridad;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelWeb.Api.Controllers;

public sealed record SolicitudIngreso(string Usuario, string Clave);

/// <summary>Permisos: las claves de PermisosHotel que tiene (las areas que no tiene no salen en el menu).</summary>
public sealed record UsuarioVista(int Id, string Login, string Nombre, bool Supervisor, bool EditaCatalogos,
    IReadOnlyList<string> Permisos, bool SeguridadActivada);
public sealed record Sesion(string Token, DateTime Vence, UsuarioVista Usuario);
public sealed record SolicitudCambioClave(string Actual, string Nueva);

/// <summary>Ingreso con los usuarios de Delphi.</summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly RepositorioUsuarios _usuarios;
    private readonly RepositorioSeguridad _seguridad;
    private readonly Tokens _tokens;
    private readonly PermisosWeb _permisos;
    private readonly ILogger<AuthController> _log;

    public AuthController(RepositorioUsuarios usuarios, RepositorioSeguridad seguridad, Tokens tokens, PermisosWeb permisos,
        ILogger<AuthController> log)
    {
        _usuarios = usuarios;
        _seguridad = seguridad;
        _tokens = tokens;
        _permisos = permisos;
        _log = log;
    }

    [AllowAnonymous]
    [HttpPost("ingresar")]
    public ActionResult<Sesion> Ingresar(SolicitudIngreso s)
    {
        var u = _usuarios.Validar(s.Usuario, s.Clave, out var motivo);
        if (u is null)
        {
            _log.LogWarning("Ingreso fallido de {Usuario}: {Motivo}", s.Usuario, motivo);
            return Unauthorized(motivo);
        }
        _log.LogInformation("Ingreso de {Usuario}", u.Login);
        return Ok(new Sesion(_tokens.Crear(u), DateTime.Now + Tokens.Duracion, Vista(u)));
    }

    [HttpGet("yo")]
    public ActionResult<UsuarioVista> Yo() =>
        _usuarios.Obtener(Tokens.IdDe(User)) is { } u ? Ok(Vista(u)) : Unauthorized("La sesion ya no es valida.");

    /// <summary>Cada usuario cambia su clave (la misma de Delphi): debe dar la actual.</summary>
    [HttpPut("clave")]
    public IActionResult CambiarClave(SolicitudCambioClave s)
    {
        try
        {
            if (!_seguridad.CambiarMiClave(Tokens.IdDe(User), s.Actual, s.Nueva)) return BadRequest("La clave actual no es correcta.");
            _log.LogInformation("{Usuario} cambio su clave", User.FindFirst("login")?.Value);
            return NoContent();
        }
        catch (ErrorSeguridad e)
        {
            return BadRequest(e.Message);
        }
    }

    private UsuarioVista Vista(Usuario u)
    {
        var p = _permisos.De(u.Id);
        return new(u.Id, u.Login, u.Nombre, u.Supervisor, _permisos.EditaCatalogos(u.Id),
            PermisosHotel.Todos.Where(x => p.Contains(x.Codigo)).Select(x => x.Clave).ToList(), _permisos.Activada());
    }
}
