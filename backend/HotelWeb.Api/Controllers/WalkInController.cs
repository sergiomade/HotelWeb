using HotelWeb.Api.Catalogos;
using HotelWeb.Api.Ecf;
using HotelWeb.Api.Seguridad;
using HotelWeb.Core.Ecf;
using HotelWeb.Core.Hospedaje;
using Microsoft.AspNetCore.Mvc;

namespace HotelWeb.Api.Controllers;

public sealed record SolicitudLineaWalkIn(int Servicio, string Descripcion, int Cantidad, decimal Precio);

public sealed record SolicitudWalkIn(string Nombre, string? Cedula, string? Rnc, string? Telefono, string? Direccion, int Tipo,
    bool ConPropina, IReadOnlyList<SolicitudLineaWalkIn> Lineas, IReadOnlyList<SolicitudPago> Pagos);

public sealed record RespuestaWalkIn(ResultadoWalkIn WalkIn, EstadoEcf Dgii);

/// <summary>Walk-in (etapa 6): venta sin reserva con e-CF; ticket, estado en la DGII y reenvio. Cualquier usuario, como Delphi.</summary>
[ApiController]
[Route("api/walkins")]
public sealed class WalkInController : ControllerBase
{
    private readonly RepositorioWalkIn _walkins;
    private readonly ServicioEcf _ecf;
    private readonly ILogger<WalkInController> _log;

    public WalkInController(RepositorioWalkIn walkins, ServicioEcf ecf, ILogger<WalkInController> log)
    {
        _walkins = walkins;
        _ecf = ecf;
        _log = log;
    }

    private string Login => User.FindFirst("login")?.Value ?? Tokens.IdDe(User).ToString();

    /// <summary>Graba el walk-in (e-NCF y recibo) y despues lo envia. Si el envio falla, ya esta grabado: se reenvia.</summary>
    [HttpPost]
    public async Task<ActionResult<RespuestaWalkIn>> Crear(SolicitudWalkIn s)
    {
        ResultadoWalkIn r;
        try
        {
            r = _walkins.Crear(new DatosWalkIn(s.Nombre, s.Cedula, s.Rnc, s.Telefono, s.Direccion, s.Tipo, s.ConPropina,
                (s.Lineas ?? Array.Empty<SolicitudLineaWalkIn>()).Select(l => new LineaWalkIn(l.Servicio, l.Descripcion, l.Cantidad, l.Precio)).ToList(),
                (s.Pagos ?? Array.Empty<SolicitudPago>()).Select(p => new PagoRecibo(p.FormaPago, p.Descripcion, p.Monto, p.MontoUs, p.NoCheque,
                    p.FechaCheque, p.Banco)).ToList()), Login);
        }
        catch (Exception e) when (e is ErrorCatalogo or ErrorEcf)
        {
            return BadRequest(e.Message);
        }
        _log.LogWarning("{Usuario} hizo el walk-in {Numero} ({Encf}) por {Total}, recibo {Recibo}", Login, r.Numero, r.Encf, r.Total, r.Recibo);
        EstadoEcf estado;
        try { estado = await _ecf.Enviar(r.Numero, OrigenFactura.WalkIn); }
        catch (Exception e) when (e is ErrorCatalogo or ErrorEcf) { estado = new EstadoEcf(r.Encf, r.Numero, "Sin enviar", e.Message, false); }
        _log.LogInformation("{Encf} (walk-in {Numero}): {Estado} {Mensaje}", r.Encf, r.Numero, estado.Estado, estado.Mensaje);
        return Ok(new RespuestaWalkIn(r, estado));
    }

    [HttpGet("{numero:long}/ticket")]
    public async Task<ActionResult<TicketWalkIn>> Ticket(long numero)
    {
        try { return Ok(await _walkins.Ticket(numero, _ecf)); }
        catch (ErrorCatalogo e) { return BadRequest(e.Message); }
    }

    [HttpGet("{numero:long}/dgii")]
    public async Task<ActionResult<EstadoEcf>> Estado(long numero)
    {
        try { return Ok(await _ecf.Consultar(numero, OrigenFactura.WalkIn)); }
        catch (Exception e) when (e is ErrorCatalogo or ErrorEcf) { return BadRequest(e.Message); }
    }

    [HttpPost("{numero:long}/enviar")]
    public async Task<ActionResult<EstadoEcf>> Reenviar(long numero)
    {
        try
        {
            var actual = await _ecf.Consultar(numero, OrigenFactura.WalkIn);
            if (actual.Aceptado) return BadRequest($"El {actual.Encf} ya fue aceptado por la DGII: no se vuelve a enviar.");
            var e = await _ecf.Enviar(numero, OrigenFactura.WalkIn);
            _log.LogWarning("{Usuario} reenvio el {Encf} (walk-in {Numero}): {Estado}", Login, e.Encf, numero, e.Estado);
            return Ok(e);
        }
        catch (Exception e) when (e is ErrorCatalogo or ErrorEcf) { return BadRequest(e.Message); }
    }
}
