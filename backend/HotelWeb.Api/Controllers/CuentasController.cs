using HotelWeb.Api.Catalogos;
using HotelWeb.Api.Hospedaje;
using HotelWeb.Api.Rack;
using HotelWeb.Api.Seguridad;
using HotelWeb.Core.Hospedaje;
using Microsoft.AspNetCore.Mvc;

namespace HotelWeb.Api.Controllers;

public sealed record SolicitudCargo(int Servicio, string Descripcion, int Cantidad, decimal Precio);

/// <summary>
/// Cargos a la cuenta de una habitacion (etapa 4). Cualquier usuario carga, como en Delphi; anular un cargo lo hacen
/// los de Permisos:AnulanCargos.
/// </summary>
[ApiController]
[Route("api/cuentas/{habitacion:int}")]
public sealed class CuentasController : ControllerBase
{
    private readonly RepositorioCuentas _cuentas;
    private readonly PermisosWeb _permisos;
    private readonly ILogger<CuentasController> _log;

    public CuentasController(RepositorioCuentas cuentas, PermisosWeb permisos, ILogger<CuentasController> log)
    {
        _cuentas = cuentas;
        _permisos = permisos;
        _log = log;
    }

    private string Login => User.FindFirst("login")?.Value ?? Tokens.IdDe(User).ToString();

    /// <summary>Lo que el usuario de la sesion puede hacer en las cuentas.</summary>
    [HttpGet("~/api/cuentas/permisos")]
    public IActionResult Permisos() => Ok(new { anulaCargos = _permisos.AnulaCargos(Tokens.IdDe(User)) });

    [HttpPost("cargos")]
    public ActionResult<CuentaHabitacion> Cargar(int habitacion, SolicitudCargo s) => Hacer(() =>
    {
        var c = _cuentas.Cargar(habitacion, new NuevoCargo(s.Servicio, s.Descripcion, s.Cantidad, s.Precio), Login);
        _log.LogInformation("{Usuario} cargo {Cantidad} x {Descripcion} ({Precio}) a la habitacion {Habitacion}",
            Login, s.Cantidad, s.Descripcion, s.Precio, habitacion);
        return c;
    });

    [HttpPost("cargos/{secuencia:int}/anular")]
    public ActionResult<CuentaHabitacion> Anular(int habitacion, int secuencia)
    {
        if (!_permisos.AnulaCargos(Tokens.IdDe(User))) return StatusCode(403, "Su usuario no puede anular cargos.");
        return Hacer(() =>
        {
            var c = _cuentas.Anular(habitacion, secuencia);
            _log.LogWarning("{Usuario} anulo la linea {Secuencia} de la cuenta de la habitacion {Habitacion}", Login, secuencia, habitacion);
            return c;
        });
    }

    private ActionResult<T> Hacer<T>(Func<T> accion)
    {
        try
        {
            return Ok(accion());
        }
        catch (ErrorCatalogo e)
        {
            return BadRequest(e.Message);
        }
    }
}
