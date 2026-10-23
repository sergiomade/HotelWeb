using HotelWeb.Api.Catalogos;
using HotelWeb.Api.Reportes;
using Microsoft.AspNetCore.Mvc;

namespace HotelWeb.Api.Controllers;

/// <summary>Reportes de ventas y comprobantes (etapa 9b). Solo lectura.</summary>
[ApiController]
[Route("api/reportes")]
public sealed class ReportesController : ControllerBase
{
    private readonly RepositorioReportes _reportes;

    public ReportesController(RepositorioReportes reportes) => _reportes = reportes;

    [HttpGet("ventas-hotel")]
    public ActionResult<IReadOnlyList<VentaHotel>> VentasHotel(DateTime desde, DateTime hasta) => Hacer(() => _reportes.VentasHotel(desde, hasta));

    [HttpGet("ventas-cafeteria")]
    public ActionResult<IReadOnlyList<VentaCafeteria>> VentasCafeteria(DateTime desde, DateTime hasta) => Hacer(() => _reportes.VentasCafeteria(desde, hasta));

    [HttpGet("comprobantes")]
    public ActionResult<ReporteComprobantes> Comprobantes(DateTime desde, DateTime hasta) => Hacer(() => _reportes.Comprobantes(desde, hasta));

    [HttpGet("clientes")]
    public ActionResult<IReadOnlyList<ClienteReporte>> Clientes(string? texto, bool conRnc = false, bool conCredito = false, bool inactivos = false) =>
        Hacer(() => _reportes.Clientes(texto, conRnc, conCredito, inactivos));

    [HttpGet("canceladas")]
    public ActionResult<IReadOnlyList<FacturaCancelada>> Canceladas(DateTime desde, DateTime hasta) => Hacer(() => _reportes.Canceladas(desde, hasta));

    private ActionResult<T> Hacer<T>(Func<T> f)
    {
        try { return Ok(f()); }
        catch (ErrorCatalogo e) { return BadRequest(e.Message); }
    }
}
