using HotelWeb.Api.Catalogos;
using HotelWeb.Api.Ecf;
using HotelWeb.Api.Seguridad;
using Microsoft.AspNetCore.Mvc;

namespace HotelWeb.Api.Controllers;

public sealed record SolicitudSecuencia(string? Autorizacion, DateTime? FechaAutorizacion, long Desde, long Hasta, DateTime? Vence);

/// <summary>Secuencias e-NCF autorizadas por la DGII (etapa 9d). Ver: todos; cambiar: Permisos:ComprobantesEspeciales.</summary>
[ApiController]
[Route("api/secuencias")]
public sealed class SecuenciasController : ControllerBase
{
    private readonly RepositorioSecuencias _secuencias;
    private readonly PermisosWeb _permisos;
    private readonly ILogger<SecuenciasController> _log;

    public SecuenciasController(RepositorioSecuencias secuencias, PermisosWeb permisos, ILogger<SecuenciasController> log)
    {
        _secuencias = secuencias;
        _permisos = permisos;
        _log = log;
    }

    private string Login => User.FindFirst("login")?.Value ?? Tokens.IdDe(User).ToString();

    [HttpGet]
    public ActionResult<PantallaSecuencias> Estado()
    {
        var (restantes, dias) = _secuencias.Umbrales();
        return Ok(new PantallaSecuencias(_secuencias.Estado(), _permisos.ComprobantesEspeciales(Tokens.IdDe(User)), restantes, dias));
    }

    /// <summary>Los tipos que se usan y estan por agotarse, por vencer, agotados, vencidos o sin autorizacion.</summary>
    [HttpGet("avisos")]
    public ActionResult<IReadOnlyList<SecuenciaEcf>> Avisos() => Ok(_secuencias.Avisos());

    [HttpPut("{codigo}")]
    public ActionResult<string> Guardar(string codigo, SolicitudSecuencia s)
    {
        if (!_permisos.ComprobantesEspeciales(Tokens.IdDe(User))) return StatusCode(403, "No tiene permiso para cambiar las secuencias e-CF.");
        try
        {
            var m = _secuencias.Guardar(codigo, s.Autorizacion, s.FechaAutorizacion, s.Desde, s.Hasta, s.Vence);
            _log.LogWarning("{Usuario} cambio la secuencia autorizada del {Codigo}: {Desde}-{Hasta}, vence {Vence}, autorizacion {Autorizacion}",
                Login, codigo, s.Desde, s.Hasta, s.Vence, s.Autorizacion);
            return Ok(new { mensaje = m });
        }
        catch (ErrorCatalogo e) { return BadRequest(e.Message); }
    }
}
