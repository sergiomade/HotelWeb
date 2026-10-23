using HotelWeb.Api.Cafeteria;
using HotelWeb.Api.Catalogos;
using HotelWeb.Api.Seguridad;
using Microsoft.AspNetCore.Mvc;

namespace HotelWeb.Api.Controllers;

/// <param name="PorFraccion">true = por trago (Precio2); false = por unidad; null = como entra el articulo.</param>
public sealed record SolicitudArticulo(string Referencia, decimal Cantidad, bool? PorFraccion);

public sealed record SolicitudEspecial(bool Bebida, string Descripcion, decimal Precio);

public sealed record SolicitudCambioLinea(decimal Cantidad, bool? PorFraccion);

public sealed record SolicitudMover(int Destino);

/// <summary>Cafeteria (etapa 8a): mesas, cuenta abierta de cada una, pre-cuenta y mover de mesa. Cualquier usuario, como Delphi.</summary>
[ApiController]
[Route("api/cafeteria")]
public sealed class CafeteriaController : ControllerBase
{
    private readonly RepositorioCafeteria _cafe;
    private readonly ILogger<CafeteriaController> _log;

    public CafeteriaController(RepositorioCafeteria cafe, ILogger<CafeteriaController> log)
    {
        _cafe = cafe;
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

    private ActionResult<CuentaMesa> Hacer(Func<CuentaMesa> accion)
    {
        try { return Ok(accion()); }
        catch (ErrorCatalogo e) { return BadRequest(e.Message); }
    }
}
