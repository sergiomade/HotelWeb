using HotelWeb.Api.Catalogos;
using HotelWeb.Api.Hospedaje;
using HotelWeb.Api.Seguridad;
using HotelWeb.Core.Hospedaje;
using Microsoft.AspNetCore.Mvc;

namespace HotelWeb.Api.Controllers;

public sealed record SolicitudCheckIn(long? ClienteId, IReadOnlyList<Asignacion> Habitaciones);

public sealed record ResultadoCarga(CargaNoche Noche, int Cargadas);

/// <summary>Check-in y carga del hospedaje de la noche (etapa 4). Cualquier usuario de recepcion, como en Delphi.</summary>
[ApiController]
[Route("api/hospedaje")]
public sealed class HospedajeController : ControllerBase
{
    private readonly RepositorioCheckIn _checkIn;
    private readonly RepositorioHospedaje _hospedaje;
    private readonly ILogger<HospedajeController> _log;

    public HospedajeController(RepositorioCheckIn checkIn, RepositorioHospedaje hospedaje, ILogger<HospedajeController> log)
    {
        _checkIn = checkIn;
        _hospedaje = hospedaje;
        _log = log;
    }

    private string Login => User.FindFirst("login")?.Value ?? Tokens.IdDe(User).ToString();

    /// <summary>Reservas activas que ya deberian entrar (de hoy y atrasadas).</summary>
    [HttpGet("llegadas")]
    public ActionResult<IReadOnlyList<Llegada>> Llegadas() => Ok(_checkIn.Llegadas(DateTime.Today));

    [HttpGet("checkin/{reserva:long}")]
    public ActionResult<PreparacionCheckIn> Preparar(long reserva) => Hacer(() => _checkIn.Preparar(reserva));

    [HttpPost("checkin/{reserva:long}")]
    public ActionResult<ResultadoCheckIn> CheckIn(long reserva, SolicitudCheckIn s) => Hacer(() =>
    {
        var r = _checkIn.Hacer(reserva, s.Habitaciones ?? Array.Empty<Asignacion>(), s.ClienteId, Login);
        _log.LogInformation("{Usuario} hizo el check-in de la reserva {Reserva}: habitaciones {Habitaciones}", Login, reserva,
            string.Join(", ", r.Cuentas.Select(c => c.Habitacion)));
        return r;
    });

    /// <summary>Como quedaria la carga del hospedaje de una noche (no carga nada).</summary>
    [HttpGet("noche/{noche}")]
    public ActionResult<CargaNoche> VerNoche(DateTime noche) => Ok(_hospedaje.Ver(noche));

    [HttpPost("noche/{noche}")]
    public ActionResult<ResultadoCarga> CargarNoche(DateTime noche, [FromQuery] bool sinVencidas)
    {
        if (noche.Date > DateTime.Today) return BadRequest("No se carga una noche que todavia no llega.");
        var (despues, cargadas) = _hospedaje.Cargar(noche, Login, sinVencidas);
        _log.LogInformation("{Usuario} cargo el hospedaje de la noche {Noche}: {Cargadas} cuentas", Login, noche.ToString("yyyy-MM-dd"), cargadas);
        return Ok(new ResultadoCarga(despues, cargadas));
    }

    private ActionResult<T> Hacer<T>(Func<T> accion)
    {
        try
        {
            return Ok(accion());
        }
        catch (ErrorCatalogo e)
        {
            return BadRequest(e.Message);
        }
    }
}
