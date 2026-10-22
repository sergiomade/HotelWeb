using HotelWeb.Api.Catalogos;
using HotelWeb.Api.Seguridad;
using HotelWeb.Core.Catalogos;
using Microsoft.AspNetCore.Mvc;

namespace HotelWeb.Api.Controllers;

/// <summary>
/// Lo que se manda al guardar. Se puede dar el precio final (lo que paga el huesped, con ITBIS y propina) o el precio
/// sin impuestos; si vienen los dos, manda el final.
/// </summary>
public sealed record SolicitudPrecio(decimal? ConImpuestos, decimal? SinImpuestos, decimal PersonaAdicional);

public sealed record PantallaPrecios(TablaPrecios Tabla, bool PuedeEditar);

/// <summary>Precios de las habitaciones por tipo y categoria (etapa 2). Todos consultan; editan los de Permisos.</summary>
[ApiController]
[Route("api/catalogos/precios")]
public sealed class PreciosController : ControllerBase
{
    private readonly RepositorioPrecios _precios;
    private readonly PermisosWeb _permisos;
    private readonly ILogger<PreciosController> _log;

    public PreciosController(RepositorioPrecios precios, PermisosWeb permisos, ILogger<PreciosController> log)
    {
        _precios = precios;
        _permisos = permisos;
        _log = log;
    }

    /// <summary>La tabla completa y si el usuario de la sesion puede editarla.</summary>
    [HttpGet]
    public ActionResult<PantallaPrecios> Obtener() =>
        Ok(new PantallaPrecios(_precios.Obtener(), _permisos.EditaCatalogos(Tokens.IdDe(User))));

    [HttpPut("{tipo:int}/{categoria}")]
    public ActionResult<Precio> Guardar(int tipo, string categoria, SolicitudPrecio s)
    {
        var id = Tokens.IdDe(User);
        if (!_permisos.EditaCatalogos(id)) return StatusCode(403, "Su usuario no puede modificar los precios.");

        decimal sinImpuestos;
        if (s.ConImpuestos is { } final) sinImpuestos = PrecioHabitacion.DesdeFinal(final);
        else if (s.SinImpuestos is { } neto) sinImpuestos = neto;
        else return BadRequest("Falta el precio.");

        var login = User.FindFirst("login")?.Value ?? id.ToString();
        try
        {
            var p = _precios.Guardar(tipo, categoria, sinImpuestos, s.PersonaAdicional, login);
            _log.LogInformation("{Usuario} cambio el precio del tipo {Tipo} categoria {Categoria}: {Precio} ({Final} final)",
                login, tipo, p.Categoria, p.SinImpuestos, p.ConImpuestos);
            return Ok(p);
        }
        catch (ErrorCatalogo e)
        {
            return BadRequest(e.Message);
        }
    }
}
