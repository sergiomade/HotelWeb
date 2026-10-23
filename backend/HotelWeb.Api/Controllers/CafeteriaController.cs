using HotelWeb.Api.Cafeteria;
using HotelWeb.Api.Catalogos;
using HotelWeb.Api.Ecf;
using HotelWeb.Api.Seguridad;
using HotelWeb.Core.Cafeteria;
using HotelWeb.Core.Ecf;
using Microsoft.AspNetCore.Mvc;

namespace HotelWeb.Api.Controllers;

/// <param name="PorFraccion">true = por trago (Precio2); false = por unidad; null = como entra el articulo.</param>
public sealed record SolicitudArticulo(string Referencia, decimal Cantidad, bool? PorFraccion);

public sealed record SolicitudEspecial(bool Bebida, string Descripcion, decimal Precio);

public sealed record SolicitudCambioLinea(decimal Cantidad, bool? PorFraccion);

public sealed record SolicitudMover(int Destino);

public sealed record SolicitudPagoCafeteria(int FormaPago, decimal Monto, int? TipoTarjeta);

public sealed record SolicitudCobro(int Tipo, long? Cliente, string? Rnc, string? Nombre, IReadOnlyList<SolicitudPagoCafeteria> Pagos);

public sealed record RespuestaCobro(ResultadoCobro Cobro, EstadoEcf Dgii);

public sealed record SolicitudCargoHabitacion(int Habitacion);

/// <summary>Cafeteria (etapa 8a): mesas, cuenta abierta de cada una, pre-cuenta y mover de mesa. Cualquier usuario, como Delphi.</summary>
[ApiController]
[Route("api/cafeteria")]
public sealed class CafeteriaController : ControllerBase
{
    private readonly RepositorioCafeteria _cafe;
    private readonly ServicioEcf _ecf;
    private readonly ILogger<CafeteriaController> _log;

    public CafeteriaController(RepositorioCafeteria cafe, ServicioEcf ecf, ILogger<CafeteriaController> log)
    {
        _cafe = cafe;
        _ecf = ecf;
        _log = log;
    }

    private string Login => User.FindFirst("login")?.Value ?? Tokens.IdDe(User).ToString();

    [HttpGet("mesas")]
    public ActionResult<IReadOnlyList<MesaCafeteria>> Mesas() => Ok(_cafe.Mesas());

    [HttpGet("articulos")]
    public ActionResult<IReadOnlyList<ArticuloCafeteria>> Articulos(long cliente = 1) => Ok(_cafe.Articulos(cliente));

    /// <summary>La cuenta abierta de la mesa; 204 si esta libre.</summary>
    [HttpGet("mesas/{mesa:int}")]
    public ActionResult<CuentaMesa> Cuenta(int mesa) => _cafe.Cuenta(mesa) is { } c ? Ok(c) : NoContent();

    [HttpPost("mesas/{mesa:int}/lineas")]
    public ActionResult<CuentaMesa> Agregar(int mesa, SolicitudArticulo s) =>
        Hacer(() => _cafe.Agregar(mesa, s.Referencia ?? "", s.Cantidad, s.PorFraccion, Login));

    [HttpPost("mesas/{mesa:int}/especial")]
    public ActionResult<CuentaMesa> Especial(int mesa, SolicitudEspecial s) =>
        Hacer(() => _cafe.Especial(mesa, s.Bebida, s.Descripcion ?? "", s.Precio, Login));

    [HttpPut("mesas/{mesa:int}/lineas/{secuencia:int}")]
    public ActionResult<CuentaMesa> Cambiar(int mesa, int secuencia, SolicitudCambioLinea s) =>
        Hacer(() => _cafe.Cambiar(mesa, secuencia, s.Cantidad, s.PorFraccion, Login));

    /// <summary>Borra una linea no impresa; 204 si la mesa quedo libre.</summary>
    [HttpDelete("mesas/{mesa:int}/lineas/{secuencia:int}")]
    public ActionResult<CuentaMesa> Borrar(int mesa, int secuencia)
    {
        try
        {
            var c = _cafe.Borrar(mesa, secuencia, Login);
            _log.LogInformation("{Usuario} borro la linea {Secuencia} de la mesa {Mesa}", Login, secuencia, mesa);
            return c is null ? NoContent() : Ok(c);
        }
        catch (ErrorCatalogo e) { return BadRequest(e.Message); }
    }

    [HttpPost("mesas/{mesa:int}/precuenta")]
    public ActionResult<PreCuentaImpresa> PreCuenta(int mesa)
    {
        try { return Ok(_cafe.PreCuenta(mesa, Login)); }
        catch (ErrorCatalogo e) { return BadRequest(e.Message); }
    }

    /// <summary>La pre-cuenta de una cuenta abierta, para la pagina de impresion /precuenta/{factura}.</summary>
    [HttpGet("cuentas/{factura:long}/precuenta")]
    public ActionResult<PreCuentaImpresa> Imprimible(long factura)
    {
        try { return Ok(_cafe.Imprimible(factura)); }
        catch (ErrorCatalogo e) { return BadRequest(e.Message); }
    }

    [HttpPost("mesas/{mesa:int}/mover")]
    public ActionResult<CuentaMesa> Mover(int mesa, SolicitudMover s)
    {
        try
        {
            var c = _cafe.Mover(mesa, s.Destino, Login);
            _log.LogWarning("{Usuario} movio la cuenta de la mesa {Mesa} a la {Destino}", Login, mesa, s.Destino);
            return Ok(c);
        }
        catch (ErrorCatalogo e) { return BadRequest(e.Message); }
    }

    // ---- etapa 8b: cobro con e-CF ----

    [HttpGet("bancos")]
    public ActionResult<IReadOnlyList<BancoTarjeta>> Bancos() => Ok(_cafe.Bancos());

    /// <summary>El nombre en la DGII de un RNC o cedula; 204 si no esta.</summary>
    [HttpGet("rnc/{rnc}")]
    public ActionResult<string> Rnc(string rnc) => _cafe.NombreDgii(rnc) is { } n ? Ok(new { nombre = n }) : NoContent();

    [HttpGet("clientes/{numero:long}")]
    public ActionResult<ClienteCobro> Cliente(long numero)
    {
        try { return _cafe.ClienteDeCobro(numero) is { } c ? Ok(c) : NotFound($"No existe el cliente {numero}."); }
        catch (ErrorCatalogo e) { return BadRequest(e.Message); }
    }

    /// <summary>Cobra la mesa (e-NCF, pagos, CxC, cierre) y despues envia el e-CF. Si el envio falla, ya esta cobrada: se reenvia.</summary>
    [HttpPost("mesas/{mesa:int}/cobrar")]
    public async Task<ActionResult<RespuestaCobro>> Cobrar(int mesa, SolicitudCobro s)
    {
        ResultadoCobro r;
        try
        {
            r = _cafe.Cobrar(mesa, new DatosCobro(s.Tipo, s.Cliente, s.Rnc, s.Nombre,
                (s.Pagos ?? []).Select(p => new PagoCafeteria(p.FormaPago, p.Monto, p.TipoTarjeta)).ToList()), Login);
        }
        catch (Exception e) when (e is ErrorCatalogo or ErrorEcf) { return BadRequest(e.Message); }
        _log.LogWarning("{Usuario} cobro la mesa {Mesa}: factura {Factura} ({Encf}) por {Total}{Credito}", Login, mesa, r.Factura, r.Encf, r.Total,
            r.Credito ? " (con credito)" : "");
        EstadoEcf estado;
        try { estado = await _ecf.Enviar(r.Factura, OrigenFactura.Cafeteria); }
        catch (Exception e) when (e is ErrorCatalogo or ErrorEcf) { estado = new EstadoEcf(r.Encf, r.Factura, "Sin enviar", e.Message, false); }
        _log.LogInformation("{Encf} (cafeteria {Factura}): {Estado} {Mensaje}", r.Encf, r.Factura, estado.Estado, estado.Mensaje);
        return Ok(new RespuestaCobro(r, estado));
    }

    [HttpGet("facturas/{factura:long}/ticket")]
    public async Task<ActionResult<TicketCafeteria>> Ticket(long factura)
    {
        try { return Ok(await _cafe.Ticket(factura, _ecf)); }
        catch (ErrorCatalogo e) { return BadRequest(e.Message); }
    }

    [HttpGet("facturas/{factura:long}/dgii")]
    public async Task<ActionResult<EstadoEcf>> Estado(long factura)
    {
        try { return Ok(await _ecf.Consultar(factura, OrigenFactura.Cafeteria)); }
        catch (Exception e) when (e is ErrorCatalogo or ErrorEcf) { return BadRequest(e.Message); }
    }

    [HttpPost("facturas/{factura:long}/enviar")]
    public async Task<ActionResult<EstadoEcf>> Reenviar(long factura)
    {
        try
        {
            var actual = await _ecf.Consultar(factura, OrigenFactura.Cafeteria);
            if (actual.Aceptado) return BadRequest($"El {actual.Encf} ya fue aceptado por la DGII: no se vuelve a enviar.");
            var e = await _ecf.Enviar(factura, OrigenFactura.Cafeteria);
            _log.LogWarning("{Usuario} reenvio el {Encf} (cafeteria {Factura}): {Estado}", Login, e.Encf, factura, e.Estado);
            return Ok(e);
        }
        catch (Exception e) when (e is ErrorCatalogo or ErrorEcf) { return BadRequest(e.Message); }
    }

    // ---- etapa 8c: cargo a la habitacion ----

    [HttpGet("habitaciones")]
    public ActionResult<IReadOnlyList<HabitacionCafeteria>> Habitaciones() => Ok(_cafe.Habitaciones());

    /// <summary>Carga la cuenta de la mesa a la habitacion (sin e-CF: va en la factura del hotel al check-out).</summary>
    [HttpPost("mesas/{mesa:int}/habitacion")]
    public ActionResult<ResultadoCargo> CargarAHabitacion(int mesa, SolicitudCargoHabitacion s)
    {
        try
        {
            var r = _cafe.CargarAHabitacion(mesa, s.Habitacion, Login);
            _log.LogWarning("{Usuario} cargo la mesa {Mesa} (factura {Factura}, subtotal {SubTotal}) a la habitacion {Habitacion}",
                Login, mesa, r.Factura, r.SubTotal, r.Habitacion);
            return Ok(r);
        }
        catch (ErrorCatalogo e) { return BadRequest(e.Message); }
    }

    private ActionResult<CuentaMesa> Hacer(Func<CuentaMesa> accion)
    {
        try { return Ok(accion()); }
        catch (ErrorCatalogo e) { return BadRequest(e.Message); }
    }
}
