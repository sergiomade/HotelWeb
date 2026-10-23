using HotelWeb.Api.Catalogos;
using HotelWeb.Api.Ecf;
using HotelWeb.Api.Seguridad;
using HotelWeb.Core.Ecf;
using Microsoft.AspNetCore.Mvc;

namespace HotelWeb.Api.Controllers;

/// <param name="Tipo">ComprobanteFiscal.RecordKey_id: 1 E31, 2 E32, 13 E44, 14 E45.</param>
public sealed record SolicitudCheckout(int Tipo, string? Rnc);

public sealed record RespuestaCheckout(ResultadoCheckout Factura, EstadoEcf? Dgii);

/// <summary>Check-out con e-CF (etapa 5) y las facturas cerradas: estado en la DGII, reenviar, imprimir.</summary>
[ApiController]
public sealed class CheckoutController : ControllerBase
{
    private readonly RepositorioCheckout _checkout;
    private readonly ServicioEcf _ecf;
    private readonly ILogger<CheckoutController> _log;

    public CheckoutController(RepositorioCheckout checkout, ServicioEcf ecf, ILogger<CheckoutController> log)
    {
        _checkout = checkout;
        _ecf = ecf;
        _log = log;
    }

    private string Login => User.FindFirst("login")?.Value ?? Tokens.IdDe(User).ToString();

    [HttpGet("api/cuentas/{habitacion:int}/checkout")]
    public ActionResult<PreparacionCheckout> Preparar(int habitacion) => Hacer(() => _checkout.Preparar(habitacion));

    /// <summary>Cierra la cuenta (e-NCF + Stp_CHECKOUT) y despues envia el e-CF. Si el envio falla, la factura ya esta cerrada.</summary>
    [HttpPost("api/cuentas/{habitacion:int}/checkout")]
    public async Task<ActionResult<RespuestaCheckout>> Cerrar(int habitacion, SolicitudCheckout s)
    {
        ResultadoCheckout r;
        try
        {
            r = _checkout.Hacer(habitacion, s.Tipo, s.Rnc, Login);
        }
        catch (Exception e) when (e is ErrorCatalogo or ErrorEcf)
        {
            return BadRequest(e.Message);
        }
        _log.LogWarning("{Usuario} hizo el check-out de la habitacion {Habitacion}: factura {Numero}, {Encf}, total {Total}",
            Login, habitacion, r.Numero, r.Encf ?? "sin e-NCF", r.Total);
        if (r.Transferida || r.Encf is null) return Ok(new RespuestaCheckout(r, null));
        try
        {
            var estado = await _ecf.Enviar(r.Numero);
            _log.LogInformation("{Encf} (factura {Numero}): {Estado} {Mensaje}", r.Encf, r.Numero, estado.Estado, estado.Mensaje);
            return Ok(new RespuestaCheckout(r, estado));
        }
        catch (Exception e) when (e is ErrorCatalogo or ErrorEcf)
        {
            return Ok(new RespuestaCheckout(r, new EstadoEcf(r.Encf, r.Numero, "Sin enviar", e.Message, false)));
        }
    }

    [HttpGet("api/facturas/{numero:long}/dgii")]
    public async Task<ActionResult<EstadoEcf>> Estado(long numero)
    {
        try { return Ok(await _ecf.Consultar(numero)); }
        catch (Exception e) when (e is ErrorCatalogo or ErrorEcf) { return BadRequest(e.Message); }
    }

    /// <summary>Vuelve a armar y enviar el e-CF de una factura cerrada que no llego o fue rechazada.</summary>
    [HttpPost("api/facturas/{numero:long}/enviar")]
    public async Task<ActionResult<EstadoEcf>> Reenviar(long numero)
    {
        try
        {
            var actual = await _ecf.Consultar(numero);
            if (actual.Aceptado) return BadRequest($"El {actual.Encf} ya fue aceptado por la DGII: no se vuelve a enviar.");
            var e = await _ecf.Enviar(numero);
            _log.LogWarning("{Usuario} reenvio el {Encf} (factura {Numero}): {Estado}", Login, e.Encf, numero, e.Estado);
            return Ok(e);
        }
        catch (Exception e) when (e is ErrorCatalogo or ErrorEcf) { return BadRequest(e.Message); }
    }

    [HttpGet("api/facturas/{numero:long}")]
    public async Task<ActionResult<FacturaImpresa>> Imprimible(long numero)
    {
        try { return Ok(await _checkout.Imprimible(numero, _ecf)); }
        catch (ErrorCatalogo e) { return BadRequest(e.Message); }
    }

    private ActionResult<T> Hacer<T>(Func<T> accion)
    {
        try { return Ok(accion()); }
        catch (Exception e) when (e is ErrorCatalogo or ErrorEcf) { return BadRequest(e.Message); }
    }
}
