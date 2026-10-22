using HotelWeb.Api.Catalogos;
using HotelWeb.Api.Reservas;
using HotelWeb.Api.Seguridad;
using HotelWeb.Core.Reservas;
using Microsoft.AspNetCore.Mvc;

namespace HotelWeb.Api.Controllers;

public sealed record SolicitudLinea(int Tipo, int Cantidad, int Adultos, int Ninos, int Bebes, bool Credito, string Categoria,
    decimal Tarifa);

public sealed record SolicitudReserva(string NombreCliente, long? ClienteId, string? Contacto, string? Telefono, string? Huesped,
    string? Cedula, string? Pasaporte, string? Rnc, DateTime Entrada, DateTime Salida, string? Comentario, int FormaPago,
    string? Categoria, IReadOnlyList<SolicitudLinea> Lineas)
{
    public DatosReserva Datos() => new(NombreCliente, ClienteId, Contacto, Telefono, Huesped, Cedula, Pasaporte, Rnc,
        Entrada, Salida, Comentario, FormaPago, Categoria,
        (Lineas ?? Array.Empty<SolicitudLinea>()).Select(l => new LineaReserva(l.Tipo, l.Cantidad, l.Adultos, l.Ninos, l.Bebes,
            l.Credito, l.Categoria, l.Tarifa)).ToList());
}

public sealed record SolicitudAdelanto(decimal Monto, int FormaPago, decimal? MontoUs);

public sealed record OpcionesReserva(IReadOnlyList<Opcion> FormasPago, IReadOnlyList<Opcion> FormasPagoAdelanto, bool AnulaAdelantos);

/// <summary>Reservaciones y sus adelantos (etapa 3). Cualquier usuario reserva y registra adelantos, como en Delphi.</summary>
[ApiController]
[Route("api/reservas")]
public sealed class ReservasController : ControllerBase
{
    private readonly RepositorioReservas _reservas;
    private readonly PermisosWeb _permisos;
    private readonly ILogger<ReservasController> _log;

    public ReservasController(RepositorioReservas reservas, PermisosWeb permisos, ILogger<ReservasController> log)
    {
        _reservas = reservas;
        _permisos = permisos;
        _log = log;
    }

    private string Login => User.FindFirst("login")?.Value ?? Tokens.IdDe(User).ToString();

    /// <summary>Por fecha de entrada (o de salida, como la proyeccion de Delphi), texto y estado.</summary>
    [HttpGet]
    public ActionResult<IReadOnlyList<ReservaResumen>> Buscar([FromQuery] DateTime? desde, [FromQuery] DateTime? hasta,
        [FromQuery] bool porSalida, [FromQuery] string? buscar, [FromQuery] int? estado)
    {
        var d = desde ?? DateTime.Today;
        var h = hasta ?? d.AddDays(30);
        if (h < d) return BadRequest("La fecha final es antes de la inicial.");
        if ((h - d).TotalDays > 400) return BadRequest("Elija un rango de hasta 400 dias.");
        return Ok(_reservas.Buscar(d, h, porSalida, buscar, estado));
    }

    [HttpGet("opciones")]
    public ActionResult<OpcionesReserva> Opciones() =>
        Ok(new OpcionesReserva(_reservas.FormasPagoReserva(), _reservas.FormasPagoAdelanto(), _permisos.AnulaAdelantos(Tokens.IdDe(User))));

    [HttpGet("disponibilidad")]
    public ActionResult<IReadOnlyList<Disponibilidad>> Disponibilidad([FromQuery] DateTime entrada, [FromQuery] DateTime salida,
        [FromQuery] long? excluir)
    {
        if (salida.Date <= entrada.Date) return BadRequest("La salida tiene que ser despues de la entrada.");
        return Ok(_reservas.Disponibilidad(entrada, salida, excluir));
    }

    [HttpGet("{id:long}")]
    public ActionResult<Reserva> Obtener(long id) =>
        _reservas.Obtener(id) is { } r ? Ok(r) : NotFound($"No existe la reserva {id}.");

    [HttpPost]
    public ActionResult<ReservaGuardada> Crear(SolicitudReserva s) => Hacer(() =>
    {
        var r = _reservas.Crear(s.Datos(), Login, DateTime.Now);
        _log.LogInformation("{Usuario} creo la reserva {Id} de {Cliente}", Login, r.Reserva.Id, r.Reserva.Cliente);
        return r;
    });

    [HttpPut("{id:long}")]
    public ActionResult<ReservaGuardada> Modificar(long id, SolicitudReserva s) => Hacer(() =>
    {
        var r = _reservas.Modificar(id, s.Datos(), Login, DateTime.Now);
        _log.LogInformation("{Usuario} modifico la reserva {Id}", Login, id);
        return r;
    });

    [HttpPost("{id:long}/cancelar")]
    public ActionResult<Reserva> Cancelar(long id, [FromQuery] bool aunConAdelantos) => Hacer(() =>
    {
        var r = _reservas.Cancelar(id, aunConAdelantos);
        _log.LogInformation("{Usuario} cancelo la reserva {Id}{Nota}", Login, id, aunConAdelantos ? " (con adelantos activos)" : "");
        return r;
    });

    [HttpPost("{id:long}/adelantos")]
    public ActionResult<Reserva> RegistrarAdelanto(long id, SolicitudAdelanto s) => Hacer(() =>
    {
        var r = _reservas.RegistrarAdelanto(id, s.Monto, s.FormaPago, s.MontoUs, Login);
        _log.LogInformation("{Usuario} registro un adelanto de {Monto} (forma {Forma}) en la reserva {Id}", Login, s.Monto, s.FormaPago, id);
        return r;
    });

    [HttpPost("{id:long}/adelantos/{adelanto:long}/anular")]
    public ActionResult<Reserva> AnularAdelanto(long id, long adelanto)
    {
        if (!_permisos.AnulaAdelantos(Tokens.IdDe(User))) return StatusCode(403, "Su usuario no puede anular adelantos.");
        return Hacer(() =>
        {
            var r = _reservas.AnularAdelanto(id, adelanto);
            _log.LogWarning("{Usuario} anulo el adelanto {Adelanto} de la reserva {Id}", Login, adelanto, id);
            return r;
        });
    }

    private ActionResult<T> Hacer<T>(Func<T> accion)
    {
        try
        {
            return Ok(accion());
        }
        catch (ErrorConfirmar e)
        {
            return Conflict(e.Message);
        }
        catch (ErrorCatalogo e)
        {
            return BadRequest(e.Message);
        }
    }
}
