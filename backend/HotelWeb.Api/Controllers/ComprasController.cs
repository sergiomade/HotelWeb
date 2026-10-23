using HotelWeb.Api.Catalogos;
using HotelWeb.Api.Ecf;
using HotelWeb.Api.Seguridad;
using HotelWeb.Core.Ecf;
using Microsoft.AspNetCore.Mvc;

namespace HotelWeb.Api.Controllers;

public sealed record SolicitudLineaCompra(string Descripcion, int BienServicio, decimal Cantidad, decimal Precio, bool Gravado);

public sealed record SolicitudCompra(DateTime? Fecha, DateTime? FechaPago, string Cedula, string Proveedor, string? Direccion, bool Verificado,
    string TipoIsr, string FormaPago, string TipoGasto, string? Observacion, IReadOnlyList<SolicitudLineaCompra> Lineas);

public sealed record PantallaCompras(IReadOnlyList<CompraLista> Compras, bool PuedeCrear, IReadOnlyDictionary<string, string> FormasPago,
    IReadOnlyDictionary<string, string> TiposGasto, IReadOnlyDictionary<string, string> RetencionesIsr);

public sealed record RespuestaCompra(long Numero, string Encf, EstadoEcf Dgii);

/// <summary>Compras a proveedores informales (E41, etapa 9d). Ver: todos; crear: Permisos:ComprobantesEspeciales.</summary>
[ApiController]
[Route("api/compras")]
public sealed class ComprasController : ControllerBase
{
    private readonly RepositorioCompras _compras;
    private readonly ServicioEcf _ecf;
    private readonly PermisosWeb _permisos;
    private readonly ILogger<ComprasController> _log;

    public ComprasController(RepositorioCompras compras, ServicioEcf ecf, PermisosWeb permisos, ILogger<ComprasController> log)
    {
        _compras = compras;
        _ecf = ecf;
        _permisos = permisos;
        _log = log;
    }

    private string Login => User.FindFirst("login")?.Value ?? Tokens.IdDe(User).ToString();

    private static List<LineaCompra> Lineas(SolicitudCompra s) =>
        (s.Lineas ?? []).Select(l => new LineaCompra(l.Descripcion ?? "", l.BienServicio, l.Cantidad, l.Precio, l.Gravado)).ToList();

    [HttpGet]
    public async Task<ActionResult<PantallaCompras>> Lista(DateTime? desde, DateTime? hasta)
    {
        try
        {
            return Ok(new PantallaCompras(await _compras.Lista(desde ?? DateTime.Today.AddDays(-30), hasta ?? DateTime.Today, _ecf),
                _permisos.ComprobantesEspeciales(Tokens.IdDe(User)), GastoMenorEcf.FormasPago, GastoMenorEcf.TiposGasto,
                CompraInformalEcf.RetencionesIsr.ToDictionary(x => x.Key, x => x.Value.Nombre)));
        }
        catch (ErrorCatalogo e) { return BadRequest(e.Message); }
    }

    /// <summary>Los montos y las retenciones antes de emitir (los mismos calculos del SP), con lo que falta.</summary>
    [HttpPost("calcular")]
    public ActionResult<object> Calcular(SolicitudCompra s)
    {
        var l = Lineas(s);
        var c = CompraInformalEcf.Calcular(l, s.TipoIsr ?? "N");
        var motivo = CompraInformalEcf.Validar(s.Fecha ?? DateTime.Today, s.FechaPago ?? s.Fecha ?? DateTime.Today, s.Cedula, s.Proveedor, s.Direccion,
            s.Verificado, s.TipoIsr, s.FormaPago, s.TipoGasto, s.Observacion, l, DateTime.Today);
        return Ok(new { calculo = c, motivo });
    }

    /// <summary>Crea la compra (con su E41) y la envia. Si el envio falla, ya esta grabada: se reenvia.</summary>
    [HttpPost]
    public async Task<ActionResult<RespuestaCompra>> Crear(SolicitudCompra s)
    {
        if (!_permisos.ComprobantesEspeciales(Tokens.IdDe(User))) return StatusCode(403, "No tiene permiso para emitir compras a informales.");
        (long Numero, string Encf) r;
        try
        {
            var fecha = s.Fecha ?? DateTime.Today;
            r = _compras.Crear(new DatosCompra(fecha, s.FechaPago ?? fecha, s.Cedula ?? "", s.Proveedor ?? "", s.Direccion, s.Verificado, s.TipoIsr ?? "",
                s.FormaPago ?? "", s.TipoGasto ?? "", s.Observacion, Lineas(s)), Login, HttpContext.Connection.RemoteIpAddress?.ToString());
        }
        catch (ErrorCatalogo e) { return BadRequest(e.Message); }
        _log.LogWarning("{Usuario} hizo la compra a informal {Numero} ({Encf})", Login, r.Numero, r.Encf);
        EstadoEcf estado;
        try { estado = await _ecf.Enviar(r.Numero, OrigenFactura.CompraInformal); }
        catch (Exception e) when (e is ErrorCatalogo or ErrorEcf) { estado = new EstadoEcf(r.Encf, r.Numero, "Sin enviar", e.Message, false); }
        _log.LogInformation("{Encf} (compra {Numero}): {Estado} {Mensaje}", r.Encf, r.Numero, estado.Estado, estado.Mensaje);
        return Ok(new RespuestaCompra(r.Numero, r.Encf, estado));
    }

    [HttpGet("{numero:long}/ticket")]
    public async Task<ActionResult<TicketCompra>> Ticket(long numero)
    {
        try { return Ok(await _compras.Ticket(numero, _ecf)); }
        catch (ErrorCatalogo e) { return BadRequest(e.Message); }
    }

    [HttpPost("{numero:long}/enviar")]
    public async Task<ActionResult<EstadoEcf>> Reenviar(long numero)
    {
        try
        {
            var actual = await _ecf.Consultar(numero, OrigenFactura.CompraInformal);
            if (actual.Aceptado) return BadRequest($"El {actual.Encf} ya fue aceptado por la DGII: no se vuelve a enviar.");
            var e = await _ecf.Enviar(numero, OrigenFactura.CompraInformal);
            _log.LogWarning("{Usuario} reenvio el {Encf} (compra {Numero}): {Estado}", Login, e.Encf, numero, e.Estado);
            return Ok(e);
        }
        catch (Exception e) when (e is ErrorCatalogo or ErrorEcf) { return BadRequest(e.Message); }
    }
}
