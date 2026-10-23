using HotelWeb.Api.Catalogos;
using HotelWeb.Api.Ecf;
using HotelWeb.Api.Seguridad;
using HotelWeb.Core.Ecf;
using Microsoft.AspNetCore.Mvc;

namespace HotelWeb.Api.Controllers;

public sealed record SolicitudGasto(DateTime? Fecha, string Descripcion, int BienServicio, decimal Cantidad, decimal Precio, string FormaPago,
    string TipoGasto, string Beneficiario, string? Cedula, string Detalle);

public sealed record PantallaGastos(IReadOnlyList<GastoLista> Gastos, bool PuedeCrear, IReadOnlyDictionary<string, string> FormasPago,
    IReadOnlyDictionary<string, string> TiposGasto);

public sealed record RespuestaGasto(long Numero, string Encf, EstadoEcf Dgii);

/// <summary>Gastos menores (E43, etapa 9d). Ver: todos; crear: Permisos:ComprobantesEspeciales.</summary>
[ApiController]
[Route("api/gastos")]
public sealed class GastosController : ControllerBase
{
    private readonly RepositorioGastos _gastos;
    private readonly ServicioEcf _ecf;
    private readonly PermisosWeb _permisos;
    private readonly ILogger<GastosController> _log;

    public GastosController(RepositorioGastos gastos, ServicioEcf ecf, PermisosWeb permisos, ILogger<GastosController> log)
    {
        _gastos = gastos;
        _ecf = ecf;
        _permisos = permisos;
        _log = log;
    }

    private string Login => User.FindFirst("login")?.Value ?? Tokens.IdDe(User).ToString();

    [HttpGet]
    public async Task<ActionResult<PantallaGastos>> Lista(DateTime? desde, DateTime? hasta)
    {
        try
        {
            return Ok(new PantallaGastos(await _gastos.Lista(desde ?? DateTime.Today.AddDays(-30), hasta ?? DateTime.Today, _ecf),
                _permisos.ComprobantesEspeciales(Tokens.IdDe(User)), GastoMenorEcf.FormasPago, GastoMenorEcf.TiposGasto));
        }
        catch (ErrorCatalogo e) { return BadRequest(e.Message); }
    }

    /// <summary>Crea el gasto (con su E43) y lo envia. Si el envio falla, ya esta grabado: se reenvia.</summary>
    [HttpPost]
    public async Task<ActionResult<RespuestaGasto>> Crear(SolicitudGasto s)
    {
        if (!_permisos.ComprobantesEspeciales(Tokens.IdDe(User))) return StatusCode(403, "No tiene permiso para emitir gastos menores.");
        (long Numero, string Encf) r;
        try
        {
            r = _gastos.Crear(new DatosGasto(s.Fecha ?? DateTime.Today, s.Descripcion ?? "", s.BienServicio, s.Cantidad, s.Precio, s.FormaPago ?? "",
                s.TipoGasto ?? "", s.Beneficiario ?? "", s.Cedula, s.Detalle ?? ""), Login, HttpContext.Connection.RemoteIpAddress?.ToString());
        }
        catch (ErrorCatalogo e) { return BadRequest(e.Message); }
        _log.LogWarning("{Usuario} hizo el gasto menor {Numero} ({Encf})", Login, r.Numero, r.Encf);
        EstadoEcf estado;
        try { estado = await _ecf.Enviar(r.Numero, OrigenFactura.GastoMenor); }
        catch (Exception e) when (e is ErrorCatalogo or ErrorEcf) { estado = new EstadoEcf(r.Encf, r.Numero, "Sin enviar", e.Message, false); }
        _log.LogInformation("{Encf} (gasto {Numero}): {Estado} {Mensaje}", r.Encf, r.Numero, estado.Estado, estado.Mensaje);
        return Ok(new RespuestaGasto(r.Numero, r.Encf, estado));
    }

    [HttpGet("{numero:long}/ticket")]
    public async Task<ActionResult<TicketGasto>> Ticket(long numero)
    {
        try { return Ok(await _gastos.Ticket(numero, _ecf)); }
        catch (ErrorCatalogo e) { return BadRequest(e.Message); }
    }

    [HttpPost("{numero:long}/enviar")]
    public async Task<ActionResult<EstadoEcf>> Reenviar(long numero)
    {
        try
        {
            var actual = await _ecf.Consultar(numero, OrigenFactura.GastoMenor);
            if (actual.Aceptado) return BadRequest($"El {actual.Encf} ya fue aceptado por la DGII: no se vuelve a enviar.");
            var e = await _ecf.Enviar(numero, OrigenFactura.GastoMenor);
            _log.LogWarning("{Usuario} reenvio el {Encf} (gasto {Numero}): {Estado}", Login, e.Encf, numero, e.Estado);
            return Ok(e);
        }
        catch (Exception e) when (e is ErrorCatalogo or ErrorEcf) { return BadRequest(e.Message); }
    }
}
