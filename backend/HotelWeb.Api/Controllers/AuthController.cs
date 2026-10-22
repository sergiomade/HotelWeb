using HotelWeb.Api.Seguridad;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelWeb.Api.Controllers;

public sealed record SolicitudIngreso(string Usuario, string Clave);
public sealed record UsuarioVista(int Id, string Login, string Nombre, bool Supervisor, bool EditaCatalogos);
public sealed record Sesion(string Token, DateTime Vence, UsuarioVista Usuario);

/// <summary>Ingreso con los usuarios de Delphi.</summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly RepositorioUsuarios _usuarios;
    private readonly Tokens _tokens;
    private readonly PermisosWeb _permisos;
    private readonly ILogger<AuthController> _log;

    public AuthController(RepositorioUsuarios usuarios, Tokens tokens, PermisosWeb permisos, ILogger<AuthController> log)
    {
        _usuarios = usuarios;
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

    private UsuarioVista Vista(Usuario u) => new(u.Id, u.Login, u.Nombre, u.Supervisor, _permisos.EditaCatalogos(u.Id));
}
