using HotelWeb.Api.Rack;
using Microsoft.AspNetCore.Mvc;

namespace HotelWeb.Api.Controllers;

/// <summary>Rack de habitaciones (etapa 1): solo lectura.</summary>
[ApiController]
[Route("api/rack")]
public sealed class RackController : ControllerBase
{
    private readonly RepositorioRack _rack;

    public RackController(RepositorioRack rack) => _rack = rack;

    /// <summary>Las habitaciones con su situacion, huesped, salida y saldo.</summary>
    [HttpGet]
    public ActionResult<RackHoy> Obtener() => Ok(_rack.Obtener(DateTime.Now));

    /// <summary>La cuenta abierta de una habitacion (cargos, pagos y saldo). 404 si esta libre.</summary>
    [HttpGet("{habitacion:int}/cuenta")]
    public ActionResult<CuentaHabitacion> Cuenta(int habitacion) =>
        _rack.Cuenta(habitacion) is { } c ? Ok(c) : NotFound("La habitacion no tiene cuenta abierta.");
}
