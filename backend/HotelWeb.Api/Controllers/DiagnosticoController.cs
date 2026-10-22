using Dapper;
using HotelWeb.Api.Datos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelWeb.Api.Controllers;

public sealed record EstadoBase(string Servidor, string Base, bool Conecta, string? Error);
public sealed record Diagnostico(string Version, string Empresa, string Rnc, int Habitaciones, int Ocupadas,
    EstadoBase Hotel, EstadoBase Seguridad);

/// <summary>Para saber, al instalar, que la API llega a las dos bases (no muestra claves).</summary>
[ApiController]
[Route("api/diagnostico")]
public sealed class DiagnosticoController : ControllerBase
{
    private readonly Conexiones _conexiones;

    public DiagnosticoController(Conexiones conexiones) => _conexiones = conexiones;

    /// <summary>Sin sesion: solo dice que la API esta viva y su version.</summary>
    [AllowAnonymous]
    [HttpGet("ping")]
    public IActionResult Ping() => Ok(new { ok = true, version = Version() });

    [HttpGet]
    public ActionResult<Diagnostico> Obtener()
    {
        string empresa = "", rnc = "";
        int habitaciones = 0, ocupadas = 0;
        EstadoBase hotel, seguridad;

        using (var cn = _conexiones.Hotel())
        {
            var (srv, db) = Conexiones.Describir(cn);
            try
            {
                var p = cn.QueryFirstOrDefault<(string? NombreEmpresa, string? RncEmpresa)>(
                    "SELECT TOP 1 NombreEmpresa, RncEmpresa FROM Parametro");
                empresa = p.NombreEmpresa?.Trim() ?? "";
                rnc = p.RncEmpresa?.Trim() ?? "";
                habitaciones = cn.ExecuteScalar<int>("SELECT COUNT(*) FROM hotelHabitacion");
                ocupadas = cn.ExecuteScalar<int>("SELECT COUNT(DISTINCT Habitacion_id) FROM HotelFactura");
                hotel = new EstadoBase(srv, db, true, null);
            }
            catch (Exception e)
            {
                hotel = new EstadoBase(srv, db, false, e.Message);
            }
        }

        using (var cn = _conexiones.Seguridad())
        {
            var (srv, db) = Conexiones.Describir(cn);
            try
            {
                cn.ExecuteScalar<int>("SELECT COUNT(*) FROM Seg_Usuario");
                seguridad = new EstadoBase(srv, db, true, null);
            }
            catch (Exception e)
            {
                seguridad = new EstadoBase(srv, db, false, e.Message);
            }
        }

        return Ok(new Diagnostico(Version(), empresa, rnc, habitaciones, ocupadas, hotel, seguridad));
    }

    private static string Version() =>
        typeof(DiagnosticoController).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
