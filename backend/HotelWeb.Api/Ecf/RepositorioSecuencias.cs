using System.Data;
using Dapper;
using HotelWeb.Api.Catalogos;
using HotelWeb.Api.Datos;

namespace HotelWeb.Api.Ecf;

/// <param name="Estado">OK, POR AGOTARSE, POR VENCER, AGOTADA, VENCIDA, SIN AUTORIZACION o NO SE USA (Stp_EstadoSecuenciasEcf).</param>
public sealed record SecuenciaEcf(string Codigo, string Descripcion, string? Autorizacion, DateTime? FechaAutorizacion, long? Desde, long? Hasta,
    DateTime? Vence, long Ultima, long Siguiente, long Restantes, int? Dias, string Estado);

public sealed record PantallaSecuencias(IReadOnlyList<SecuenciaEcf> Secuencias, bool PuedeEditar, int AvisoRestantes, int AvisoDias);

/// <summary>
/// Las secuencias e-NCF autorizadas por la DGII (etapa 9d, MIG_19 de Delphi): el rango (desde - hasta) y el vencimiento de
/// cada tipo, cuantos quedan y su estado. Lo calcula Stp_EstadoSecuenciasEcf y lo guarda Stp_GuardarAutorizacionEcf
/// (los mismos de la pantalla de Delphi). Un e-NCF fuera del rango o vencido no se emite (Stp_EcfSecuenciaAutorizada).
/// </summary>
public sealed class RepositorioSecuencias
{
    /// <summary>Los tipos que el hotel emite: los avisos solo importan para estos.</summary>
    public static readonly IReadOnlySet<string> QueSeUsan = new HashSet<string> { "E31", "E32", "E34", "E41", "E43", "E44", "E45" };

    private readonly Conexiones _conexiones;

    public RepositorioSecuencias(Conexiones conexiones) => _conexiones = conexiones;

    private sealed class Fila
    {
        public string? NCodigo { get; set; }
        public string? Descripcion { get; set; }
        public string? NumeroAutorizacionEncf { get; set; }
        public DateTime? FechaAutorizacionEncf { get; set; }
        public decimal? SecuenciaDesdeEncf { get; set; }
        public decimal? SecuenciaHastaEncf { get; set; }
        public DateTime? FechaVencimientoEncf { get; set; }
        public decimal? Ultima { get; set; }
        public decimal? Siguiente { get; set; }
        public decimal? Restantes { get; set; }
        public int? Dias { get; set; }
        public string? Estado { get; set; }
    }

    public IReadOnlyList<SecuenciaEcf> Estado()
    {
        using var cn = _conexiones.Hotel();
        return cn.Query<Fila>("dbo.Stp_EstadoSecuenciasEcf", commandType: CommandType.StoredProcedure)
            .Select(f => new SecuenciaEcf((f.NCodigo ?? "").Trim(), f.Descripcion ?? "", f.NumeroAutorizacionEncf?.Trim(), f.FechaAutorizacionEncf,
                (long?)f.SecuenciaDesdeEncf, (long?)f.SecuenciaHastaEncf, f.FechaVencimientoEncf, (long)(f.Ultima ?? 0), (long)(f.Siguiente ?? 0),
                (long)(f.Restantes ?? 0), f.Dias, (f.Estado ?? "").Trim())).ToList();
    }

    public (int Restantes, int Dias) Umbrales()
    {
        using var cn = _conexiones.Hotel();
        var u = cn.QueryFirstOrDefault<(int?, int?)>("SELECT TOP 1 AlertaEcfRestantes, AlertaEcfDias FROM Parametro");
        return (u.Item1 ?? 100, u.Item2 ?? 30);
    }

    /// <summary>Lo que hay que avisar al entrar (como MostrarAvisosSecuenciasEcf): los tipos que se usan y no estan bien.</summary>
    public IReadOnlyList<SecuenciaEcf> Avisos() =>
        Estado().Where(s => QueSeUsan.Contains(s.Codigo) && s.Estado is not ("OK" or "NO SE USA")).ToList();

    public string Guardar(string codigo, string? autorizacion, DateTime? fechaAutorizacion, long desde, long hasta, DateTime? vence)
    {
        using var cn = _conexiones.Hotel();
        var r = cn.QueryFirst<(int Resultado, string? Mensaje)>("dbo.Stp_GuardarAutorizacionEcf", new
        {
            NCodigo = new DbString { Value = codigo.Trim().ToUpperInvariant(), IsAnsi = true, IsFixedLength = true, Length = 3 },
            NumeroAutorizacion = new DbString { Value = (autorizacion ?? "").Trim(), IsAnsi = true, Length = 40 },
            FechaAutorizacion = fechaAutorizacion?.Date,
            SecuenciaDesde = (decimal)desde,
            SecuenciaHasta = (decimal)hasta,
            FechaVencimiento = vence?.Date,
        }, commandType: CommandType.StoredProcedure);
        return r.Resultado == 1 ? r.Mensaje ?? "Guardado." : throw new ErrorCatalogo(r.Mensaje ?? "No se pudo guardar.");
    }
}
