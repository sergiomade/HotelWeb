using Dapper;
using HotelWeb.Api.Datos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelWeb.Api.Controllers;

public sealed record Empresa(string Nombre, string? Rnc);

/// <summary>
/// El nombre del hotel para la barra, la pantalla de ingreso y el titulo de la pagina: Parametro.NombreEmpresa, la misma
/// configuracion de Delphi. Sin sesion (la pantalla de ingreso lo muestra); no es un dato reservado.
/// </summary>
[ApiController]
[Route("api/empresa")]
public sealed class EmpresaController : ControllerBase
{
    private readonly Conexiones _conexiones;

    public EmpresaController(Conexiones conexiones) => _conexiones = conexiones;

    [AllowAnonymous]
    [HttpGet]
    public ActionResult<Empresa> Obtener()
    {
        try
        {
            using var cn = _conexiones.Hotel();
            var p = cn.QueryFirstOrDefault<(string? Nombre, string? Rnc)>("SELECT TOP 1 NombreEmpresa, RncEmpresa FROM Parametro");
            return Ok(new Empresa(string.IsNullOrWhiteSpace(p.Nombre) ? "Hotel" : p.Nombre.Trim(), p.Rnc?.Trim()));
        }
        catch
        {
            // sin base no se cae la pantalla de ingreso: dice "Hotel"
            return Ok(new Empresa("Hotel", null));
        }
    }
}
