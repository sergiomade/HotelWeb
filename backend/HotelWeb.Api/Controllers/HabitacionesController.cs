using HotelWeb.Api.Catalogos;
using HotelWeb.Api.Seguridad;
using HotelWeb.Core.Catalogos;
using Microsoft.AspNetCore.Mvc;

namespace HotelWeb.Api.Controllers;

public sealed record SolicitudHabitacion(int Tipo, int Estado, string? Comentario, string? Extension,
    bool Television, bool AireAcondicionado, bool Cabana)
{
    public CambiosHabitacion Cambios() => new(Tipo, Estado, Comentario, Extension, Television, AireAcondicionado, Cabana);
}

/// <summary>Para crear: el numero y, si se quiere, la posicion en el plano (si no, la sugiere la API).</summary>
public sealed record SolicitudNuevaHabitacion(int Numero, int? X, int? Y, int Tipo, int Estado, string? Comentario,
    string? Extension, bool Television, bool AireAcondicionado, bool Cabana)
{
    public CambiosHabitacion Cambios() => new(Tipo, Estado, Comentario, Extension, Television, AireAcondicionado, Cabana);
}

public sealed record PantallaHabitaciones(CatalogoHabitaciones Catalogo, bool PuedeEditar);

/// <summary>Catalogo de habitaciones (etapa 2). Todos consultan; crean y modifican los de Permisos.</summary>
[ApiController]
[Route("api/catalogos/habitaciones")]
public sealed class HabitacionesController : ControllerBase
{
    private readonly RepositorioHabitaciones _habitaciones;
    private readonly PermisosWeb _permisos;
    private readonly ILogger<HabitacionesController> _log;

    public HabitacionesController(RepositorioHabitaciones habitaciones, PermisosWeb permisos, ILogger<HabitacionesController> log)
    {
        _habitaciones = habitaciones;
        _permisos = permisos;
        _log = log;
    }

    [HttpGet]
    public ActionResult<PantallaHabitaciones> Obtener() =>
        Ok(new PantallaHabitaciones(_habitaciones.Obtener(), _permisos.EditaCatalogos(Tokens.IdDe(User))));

    [HttpGet("posicion/{numero:int}")]
    public IActionResult Posicion(int numero)
    {
        var (x, y) = _habitaciones.PosicionSugerida(numero);
        return Ok(new { x, y });
    }

    [HttpPut("{numero:int}")]
    public ActionResult<Habitacion> Modificar(int numero, SolicitudHabitacion s) => Escribir(login =>
    {
        var h = _habitaciones.Modificar(numero, s.Cambios(), login);
        _log.LogInformation("{Usuario} modifico la habitacion {Numero}: tipo {Tipo}, estado {Estado}", login, numero, h.Tipo, h.Estado);
        return h;
    });

    [HttpPost]
    public ActionResult<Habitacion> Crear(SolicitudNuevaHabitacion s) => Escribir(login =>
    {
        var h = _habitaciones.Crear(s.Numero, s.X, s.Y, s.Cambios(), login);
        _log.LogInformation("{Usuario} creo la habitacion {Numero}", login, h.Numero);
        return h;
    });

    private ActionResult<Habitacion> Escribir(Func<string, Habitacion> accion)
    {
        var id = Tokens.IdDe(User);
        if (!_permisos.EditaCatalogos(id)) return StatusCode(403, "Su usuario no puede modificar las habitaciones.");
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
