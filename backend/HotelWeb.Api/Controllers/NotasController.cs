using HotelWeb.Api.Catalogos;
using HotelWeb.Api.Ecf;
using HotelWeb.Api.Seguridad;
using HotelWeb.Core.Ecf;
using Microsoft.AspNetCore.Mvc;

namespace HotelWeb.Api.Controllers;

/// <param name="Codigo">1 anulacion total, 3 nota por un monto.</param>
public sealed record SolicitudNota(int Codigo, decimal Monto, string? Motivo, string? Responsable, string? Clave);

/// <summary>
/// Notas de credito E34 (etapas 7 y 8d) a las facturas del check-out ("hospedaje"), del walk-in ("walkin") y de la
/// cafeteria ("cafeteria"):
/// preparar, crear (anulacion total o por un monto, con un responsable), imprimir, estado y reenvio.
/// </summary>
[ApiController]
[Route("api/notas/{origen}")]
public sealed class NotasController : ControllerBase
{
    private readonly RepositorioNotas _notas;
    private readonly ServicioEcf _ecf;
    private readonly ILogger<NotasController> _log;

    public NotasController(RepositorioNotas notas, ServicioEcf ecf, ILogger<NotasController> log)
    {
        _notas = notas;
        _ecf = ecf;
        _log = log;
    }

    private string Login => User.FindFirst("login")?.Value ?? Tokens.IdDe(User).ToString();

    /// <summary>La factura, sus notas, lo pendiente, su estado en la DGII y que se le puede hacer.</summary>
    [HttpGet("factura/{factura:long}")]
    public async Task<ActionResult<PreparacionNota>> Preparar(string origen, long factura)
    {
        try { return Ok(await _notas.Preparar(RepositorioNotas.OrigenDe(origen), factura, _ecf)); }
        catch (Exception e) when (e is ErrorCatalogo or ErrorEcf) { return BadRequest(e.Message); }
    }

    [HttpPost("factura/{factura:long}")]
    public async Task<ActionResult<ResultadoNota>> Crear(string origen, long factura, SolicitudNota s)
    {
        try
        {
            var r = await _notas.Crear(RepositorioNotas.OrigenDe(origen), factura, s.Codigo, s.Monto, s.Motivo, s.Responsable, s.Clave, Login,
                HttpContext.Connection.RemoteIpAddress?.ToString(), _ecf);
            _log.LogWarning("{Usuario} hizo la nota {Encf} ({Codigo}) por {Monto} a la factura {Origen} {Factura}; autorizo {Responsable}. DGII: {Estado}",
                Login, r.Encf, s.Codigo, r.Monto, origen, factura, s.Responsable, r.Dgii.Estado);
            return Ok(r);
        }
        catch (Exception e) when (e is ErrorCatalogo or ErrorEcf) { return BadRequest(e.Message); }
    }

    [HttpGet("{numero:long}")]
    public async Task<ActionResult<NotaImpresa>> Imprimible(string origen, long numero)
    {
        try { return Ok(await _notas.Imprimible(RepositorioNotas.DeNota(RepositorioNotas.OrigenDe(origen)), numero, _ecf)); }
        catch (ErrorCatalogo e) { return BadRequest(e.Message); }
    }

    [HttpPost("{numero:long}/enviar")]
    public async Task<ActionResult<EstadoEcf>> Reenviar(string origen, long numero)
    {
        try
        {
            var o = RepositorioNotas.DeNota(RepositorioNotas.OrigenDe(origen));
            var actual = await _ecf.Consultar(numero, o);
            if (actual.Aceptado) return BadRequest($"La nota {actual.Encf} ya fue aceptada por la DGII: no se vuelve a enviar.");
            var e = await _ecf.Enviar(numero, o);
            _log.LogWarning("{Usuario} reenvio la nota {Encf} ({Numero}): {Estado}", Login, e.Encf, numero, e.Estado);
            return Ok(e);
        }
        catch (Exception e) when (e is ErrorCatalogo or ErrorEcf) { return BadRequest(e.Message); }
    }
}
