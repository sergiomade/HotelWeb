using HotelWeb.Api.Catalogos;
using HotelWeb.Api.Hospedaje;
using HotelWeb.Api.Rack;
using HotelWeb.Api.Seguridad;
using HotelWeb.Core.Hospedaje;
using Microsoft.AspNetCore.Mvc;

namespace HotelWeb.Api.Controllers;

public sealed record SolicitudCargo(int Servicio, string Descripcion, int Cantidad, decimal Precio);

public sealed record SolicitudPago(int FormaPago, string Descripcion, decimal Monto, decimal? MontoUs, string? NoCheque,
    DateTime? FechaCheque, string? Banco);

public sealed record SolicitudRecibo(string? Concepto, IReadOnlyList<SolicitudPago> Pagos);

/// <param name="Todas">Tambien las demas habitaciones abiertas de la misma reserva.</param>
public sealed record SolicitudSalida(DateTime Salida, bool Todas, string? Comentario);

/// <param name="Tarifa">Sin impuestos; null = la de ahora (mismo tipo y categoria) o la de la tabla de precios.</param>
public sealed record SolicitudCambio(int Hasta, string? Categoria, decimal? Tarifa);

public sealed record SolicitudTransferencia(int Hasta);

/// <summary>
/// Cargos a la cuenta de una habitacion (etapa 4). Cualquier usuario carga, como en Delphi; anular un cargo lo hacen
/// los de Permisos:AnulanCargos.
/// </summary>
[ApiController]
[Route("api/cuentas/{habitacion:int}")]
public sealed class CuentasController : ControllerBase
{
    private readonly RepositorioCuentas _cuentas;
    private readonly RepositorioRecibos _recibos;
    private readonly RepositorioSalidas _salidas;
    private readonly RepositorioCambioHabitacion _cambio;
    private readonly RepositorioTransferencia _transferencia;
    private readonly PermisosWeb _permisos;
    private readonly ILogger<CuentasController> _log;

    public CuentasController(RepositorioCuentas cuentas, RepositorioRecibos recibos, RepositorioSalidas salidas,
        RepositorioCambioHabitacion cambio, RepositorioTransferencia transferencia, PermisosWeb permisos, ILogger<CuentasController> log)
    {
        _cuentas = cuentas;
        _recibos = recibos;
        _salidas = salidas;
        _cambio = cambio;
        _transferencia = transferencia;
        _permisos = permisos;
        _log = log;
    }

    /// <summary>Para transferir a una habitacion ocupada: lo que pasa y las ocupadas.</summary>
    [HttpGet("transferencia")]
    public ActionResult<PreparacionTransferencia> PrepararTransferencia(int habitacion) => Hacer(() => _transferencia.Preparar(habitacion));

    /// <summary>Transferir la cuenta a otra habitacion ocupada y cerrar la de origen (cualquier usuario, como en Delphi).</summary>
    [HttpPost("transferencia")]
    public ActionResult<ResultadoTransferencia> Transferir(int habitacion, SolicitudTransferencia s) => Hacer(() =>
    {
        var r = _transferencia.Transferir(habitacion, s.Hasta, Login);
        _log.LogWarning("{Usuario} transfirio la habitacion {Desde} a la {Hasta}: cargos {Cargos}, pagos {Pagos}; origen cerrado en la {Cerrada}",
            Login, habitacion, s.Hasta, r.Cargos, r.Pagos, r.Cerrada);
        return r;
    });

    /// <summary>Para cambiar a otra habitacion libre: lo que se cobra hoy y las libres.</summary>
    [HttpGet("cambio")]
    public ActionResult<PreparacionCambio> PrepararCambio(int habitacion) => Hacer(() => _cambio.Preparar(habitacion));

    /// <summary>Mover la cuenta a una habitacion libre (cualquier usuario de recepcion, como en Delphi).</summary>
    [HttpPost("cambio")]
    public ActionResult<ResultadoCambio> Cambiar(int habitacion, SolicitudCambio s) => Hacer(() =>
    {
        var r = _cambio.Cambiar(habitacion, s.Hasta, s.Categoria, s.Tarifa, Login);
        _log.LogInformation("{Usuario} cambio la cuenta {Factura} de la habitacion {Desde} a la {Hasta} ({Accion}, tarifa {Tarifa})",
            Login, r.Factura, habitacion, s.Hasta, r.Accion, s.Tarifa);
        return r;
    });

    /// <summary>La salida de la habitacion, las demas de su reserva y los cambios hechos.</summary>
    [HttpGet("salida")]
    public ActionResult<SalidaCuenta> Salida(int habitacion) => Hacer(() => _salidas.Ver(habitacion));

    /// <summary>Cambiar la salida (cualquier usuario de recepcion, como el check-in).</summary>
    [HttpPost("salida")]
    public ActionResult<ResultadoSalida> CambiarSalida(int habitacion, SolicitudSalida s) => Hacer(() =>
    {
        var r = _salidas.Cambiar(habitacion, s.Salida, s.Todas, s.Comentario, Login, DateTime.Today);
        _log.LogInformation("{Usuario} cambio la salida de la habitacion {Habitacion} al {Salida} (todas: {Todas})",
            Login, habitacion, s.Salida.ToString("yyyy-MM-dd"), s.Todas);
        return r;
    });

    /// <summary>Recibos de pago de la estadia actual de la habitacion.</summary>
    [HttpGet("recibos")]
    public ActionResult<IReadOnlyList<Recibo>> Recibos(int habitacion) => Ok(_recibos.DeLaCuenta(habitacion));

    [HttpPost("recibos")]
    public ActionResult<Recibo> NuevoRecibo(int habitacion, SolicitudRecibo s) => Hacer(() =>
    {
        var r = _recibos.Crear(habitacion, s.Concepto,
            (s.Pagos ?? Array.Empty<SolicitudPago>()).Select(p => new PagoRecibo(p.FormaPago, p.Descripcion, p.Monto, p.MontoUs,
                p.NoCheque, p.FechaCheque, p.Banco)).ToList(), Login);
        _log.LogInformation("{Usuario} hizo el recibo {Numero} por {Monto} a la habitacion {Habitacion}", Login, r.Numero, r.Monto, habitacion);
        return r;
    });

    [HttpGet("~/api/recibos/{numero:long}")]
    public ActionResult<ReciboImpreso> ReciboImpreso(long numero) =>
        _recibos.Impreso(numero) is { } r ? Ok(r) : NotFound($"No existe el recibo {numero}.");

    [HttpPost("~/api/recibos/{numero:long}/anular")]
    public ActionResult<Recibo> AnularRecibo(long numero)
    {
        if (!_permisos.AnulaRecibos(Tokens.IdDe(User))) return StatusCode(403, "Su usuario no puede anular recibos.");
        return Hacer(() =>
        {
            var r = _recibos.Anular(numero, Login);
            _log.LogWarning("{Usuario} anulo el recibo {Numero} de {Monto} (habitacion {Habitacion})", Login, numero, r.Monto, r.Habitacion);
            return r;
        });
    }

    private string Login => User.FindFirst("login")?.Value ?? Tokens.IdDe(User).ToString();

    /// <summary>Lo que el usuario de la sesion puede hacer en las cuentas.</summary>
    [HttpGet("~/api/cuentas/permisos")]
    public IActionResult Permisos() => Ok(new
    {
        anulaCargos = _permisos.AnulaCargos(Tokens.IdDe(User)),
        anulaRecibos = _permisos.AnulaRecibos(Tokens.IdDe(User)),
    });

    [HttpPost("cargos")]
    public ActionResult<CuentaHabitacion> Cargar(int habitacion, SolicitudCargo s) => Hacer(() =>
    {
        var c = _cuentas.Cargar(habitacion, new NuevoCargo(s.Servicio, s.Descripcion, s.Cantidad, s.Precio), Login);
        _log.LogInformation("{Usuario} cargo {Cantidad} x {Descripcion} ({Precio}) a la habitacion {Habitacion}",
            Login, s.Cantidad, s.Descripcion, s.Precio, habitacion);
        return c;
    });

    [HttpPost("cargos/{secuencia:int}/anular")]
    public ActionResult<CuentaHabitacion> Anular(int habitacion, int secuencia)
    {
        if (!_permisos.AnulaCargos(Tokens.IdDe(User))) return StatusCode(403, "Su usuario no puede anular cargos.");
        return Hacer(() =>
        {
            var c = _cuentas.Anular(habitacion, secuencia);
            _log.LogWarning("{Usuario} anulo la linea {Secuencia} de la cuenta de la habitacion {Habitacion}", Login, secuencia, habitacion);
            return c;
        });
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
