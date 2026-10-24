using HotelWeb.Api.Seguridad;
using HotelWeb.Core.Seguridad;
using Microsoft.AspNetCore.Mvc;

namespace HotelWeb.Api.Controllers;

public sealed record PermisoVista(int Codigo, string Clave, string Nombre, string Tipo, string Ayuda, string? Area);

public sealed record PantallaSeguridad(bool Activada, IReadOnlyList<PermisoVista> Permisos,
    IReadOnlyList<GrupoSeguridad> Grupos, IReadOnlyList<UsuarioSeguridad> Usuarios);

public sealed record SolicitudUsuarioNuevo(string Login, string Nombre, string Clave, bool CancelaFactura, int[]? Grupos);
public sealed record SolicitudUsuarioExistente(string Login, int[]? Grupos);
public sealed record SolicitudUsuario(string Nombre, bool Activo, bool CancelaFactura, int[]? Grupos);
public sealed record SolicitudClave(string Clave);
public sealed record SolicitudGrupo(string Descripcion, bool Activo, int[]? Permisos);

/// <summary>
/// Seguridad de la web (etapa 10): usuarios, grupos y permisos del hotel sobre las tablas de Seguridad de Delphi.
/// Todo pide el permiso "Administrar seguridad" (antes de activar: los de Permisos:EditanCatalogos).
/// </summary>
[ApiController]
[Route("api/seguridad")]
public sealed class SeguridadController : ControllerBase
{
    private readonly RepositorioSeguridad _repo;
    private readonly PermisosWeb _permisos;
    private readonly ILogger<SeguridadController> _log;

    public SeguridadController(RepositorioSeguridad repo, PermisosWeb permisos, ILogger<SeguridadController> log)
    {
        _repo = repo;
        _permisos = permisos;
        _log = log;
    }

    private int Yo => Tokens.IdDe(User);
    private string Login => User.FindFirst("login")?.Value ?? Yo.ToString();

    private ActionResult? SinPermiso() =>
        _permisos.AdministraSeguridad(Yo) ? null : StatusCode(403, "Su usuario no puede administrar la seguridad.");

    [HttpGet]
    public ActionResult<PantallaSeguridad> Pantalla()
    {
        if (SinPermiso() is { } no) return no;
        var permisos = PermisosHotel.Todos.Select(p => new PermisoVista(p.Codigo, p.Clave, p.Nombre, p.Tipo.ToString(), p.Ayuda, p.Area)).ToList();
        return _permisos.Activada()
            ? Ok(new PantallaSeguridad(true, permisos, _repo.Grupos(), _repo.Usuarios()))
            : Ok(new PantallaSeguridad(false, permisos, Array.Empty<GrupoSeguridad>(), Array.Empty<UsuarioSeguridad>()));
    }

    /// <summary>Crea el modulo 40 y los grupos ADMIN HOTEL (con quien activa y la lista de catalogos) y RECEPCION.</summary>
    [HttpPost("activar")]
    public IActionResult Activar() => Hacer(() =>
    {
        _repo.Activar(_permisos.AdministradoresIniciales.Append(Yo).Distinct().ToList());
        _log.LogWarning("{Usuario} activo la seguridad de la web (modulo {Modulo})", Login, PermisosHotel.Modulo);
    });

    [HttpPost("usuarios")]
    public IActionResult CrearUsuario(SolicitudUsuarioNuevo s) => Hacer(() =>
    {
        var id = _repo.CrearUsuario(s.Login, s.Nombre, s.Clave, s.CancelaFactura, s.Grupos ?? Array.Empty<int>());
        _log.LogWarning("{Usuario} creo el usuario {Nuevo} ({Id})", Login, s.Login, id);
    });

    [HttpPost("usuarios/existente")]
    public IActionResult AgregarUsuario(SolicitudUsuarioExistente s) => Hacer(() =>
    {
        var id = _repo.AgregarUsuario(s.Login, s.Grupos ?? Array.Empty<int>());
        _log.LogWarning("{Usuario} agrego al hotel el usuario existente {Otro} ({Id})", Login, s.Login, id);
    });

    [HttpPut("usuarios/{id:int}")]
    public IActionResult ModificarUsuario(int id, SolicitudUsuario s) => Hacer(() =>
    {
        if (id == Yo && !s.Activo) throw new ErrorSeguridad("No puede desactivar su propio usuario.");
        _repo.ModificarUsuario(id, s.Nombre, s.Activo, s.CancelaFactura, s.Grupos ?? Array.Empty<int>());
        _log.LogWarning("{Usuario} modifico el usuario {Id}: activo {Activo}, grupos {Grupos}", Login, id, s.Activo,
            string.Join(",", s.Grupos ?? Array.Empty<int>()));
    });

    [HttpPut("usuarios/{id:int}/clave")]
    public IActionResult PonerClave(int id, SolicitudClave s) => Hacer(() =>
    {
        _repo.PonerClave(id, s.Clave);
        _log.LogWarning("{Usuario} puso una clave nueva al usuario {Id}", Login, id);
    });

    [HttpPost("grupos")]
    public IActionResult CrearGrupo(SolicitudGrupo s) => Hacer(() =>
    {
        var id = _repo.CrearGrupo(s.Descripcion, s.Permisos ?? Array.Empty<int>());
        _log.LogWarning("{Usuario} creo el grupo {Grupo} ({Id})", Login, s.Descripcion, id);
    });

    [HttpPut("grupos/{id:int}")]
    public IActionResult ModificarGrupo(int id, SolicitudGrupo s) => Hacer(() =>
    {
        _repo.ModificarGrupo(id, s.Descripcion, s.Activo, s.Permisos ?? Array.Empty<int>());
        _log.LogWarning("{Usuario} modifico el grupo {Id}: activo {Activo}, permisos {Permisos}", Login, id, s.Activo,
            string.Join(",", s.Permisos ?? Array.Empty<int>()));
    });

    /// <summary>
    /// Revisa el permiso, hace el cambio y olvida los permisos guardados (valen enseguida). Antes de activar no hay
    /// grupos del hotel, asi que los demas cambios fallan solos.
    /// </summary>
    private IActionResult Hacer(Action cambio)
    {
        if (SinPermiso() is { } no) return no;
        try
        {
            cambio();
            return NoContent();
        }
        catch (ErrorSeguridad e)
        {
            return BadRequest(e.Message);
        }
        finally
        {
            _permisos.Olvidar();
        }
    }
}
