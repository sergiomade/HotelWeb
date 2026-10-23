using HotelWeb.Api.Caja;
using HotelWeb.Api.Catalogos;
using HotelWeb.Api.Seguridad;
using HotelWeb.Core.Caja;
using Microsoft.AspNetCore.Mvc;

namespace HotelWeb.Api.Controllers;

/// <param name="Movimientos">Cuantos movimientos vio el cajero al contar (si entro otro, no cierra).</param>
/// <param name="Ingresos">El total que vio.</param>
public sealed record SolicitudCierre(decimal Efectivo, decimal Dolares, string? Comentario, int Movimientos, decimal Ingresos);

/// <summary>Cuadre y cierre de caja por cajero (etapa 9a): hotel (recibos y adelantos) y cafeteria (lo cobrado en las mesas).</summary>
[ApiController]
[Route("api/caja")]
public sealed class CajaController : ControllerBase
{
    private readonly RepositorioCaja _caja;
    private readonly ILogger<CajaController> _log;

    public CajaController(RepositorioCaja caja, ILogger<CajaController> log)
    {
        _caja = caja;
        _log = log;
    }

    private string Login => User.FindFirst("login")?.Value ?? Tokens.IdDe(User).ToString();

    private static AmbitoCaja? AmbitoDe(string a) => a.ToLowerInvariant() switch
    {
        "hotel" => AmbitoCaja.Hotel,
        "cafeteria" => AmbitoCaja.Cafeteria,
        _ => null,
    };

    [HttpGet("{ambito}/pendiente")]
    public ActionResult<CuadreCaja> Pendiente(string ambito) =>
        AmbitoDe(ambito) is { } a ? Ok(_caja.Pendiente(a, Login)) : BadRequest("Caja no valida: hotel o cafeteria.");

    [HttpGet("{ambito}/cierres")]
    public ActionResult<IReadOnlyList<CierreResumen>> Cierres(string ambito) =>
        AmbitoDe(ambito) is { } a ? Ok(_caja.Cierres(a, Login)) : BadRequest("Caja no valida: hotel o cafeteria.");

    [HttpPost("{ambito}/cerrar")]
    public ActionResult<CierreResumen> Cerrar(string ambito, SolicitudCierre s)
    {
        if (AmbitoDe(ambito) is not { } a) return BadRequest("Caja no valida: hotel o cafeteria.");
        try
        {
            var c = _caja.Cerrar(a, Login, s.Efectivo, s.Dolares, s.Comentario, (s.Movimientos, s.Ingresos),
                HttpContext.Connection.RemoteIpAddress?.ToString());
            _log.LogWarning("{Usuario} cerro la caja {Ambito} (cierre {Cierre}): total {Total}, efectivo esperado {Esperado} contado {Contado}",
                Login, a, c.Cierre, c.Total, c.EsperadoEfectivo, c.ContadoEfectivo);
            return Ok(c);
        }
        catch (ErrorCatalogo e) { return BadRequest(e.Message); }
    }

    /// <summary>El cierre para imprimir (/cierre/{n}). Solo el cajero que lo hizo.</summary>
    [HttpGet("cierres/{cierre:int}")]
    public ActionResult<CierreImpreso> Imprimible(int cierre)
    {
        try
        {
            var c = _caja.Imprimible(cierre);
            var yo = (Login.Length > 10 ? Login[..10] : Login).Trim();
            return string.Equals(c.Cierre.Usuario, yo, StringComparison.OrdinalIgnoreCase) ? Ok(c) : StatusCode(403, "Ese cierre es de otro cajero.");
        }
        catch (ErrorCatalogo e) { return BadRequest(e.Message); }
    }
}
