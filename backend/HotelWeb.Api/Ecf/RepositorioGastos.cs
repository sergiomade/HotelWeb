using System.Data;
using Dapper;
using HotelWeb.Api.Catalogos;
using HotelWeb.Api.Datos;
using HotelWeb.Core.Ecf;

namespace HotelWeb.Api.Ecf;

public sealed record DatosGasto(DateTime Fecha, string Descripcion, int BienServicio, decimal Cantidad, decimal Precio, string FormaPago,
    string TipoGasto, string Beneficiario, string? Cedula, string Detalle);

public sealed record GastoLista(long Numero, DateTime Fecha, string Encf, string Descripcion, string Beneficiario, decimal Total, string FormaPago,
    string? Usuario, EstadoEcf? Dgii);

public sealed record TicketGasto(GastoMenor Gasto, EmisorEcf Emisor, DateTime? Vencimiento, string FormaPago, string TipoGasto, TimbreEcf Timbre);

/// <summary>
/// Gastos menores (E43, etapa 9d) sobre lo de Delphi (MIG_14): Stp_CrearGastoMenor revisa, toma el siguiente E43 dentro de la
/// secuencia autorizada y graba; despues se arma el XML y se envia. Un gasto aceptado no se anula ni se cambia.
/// </summary>
public sealed class RepositorioGastos
{
    private readonly Conexiones _conexiones;

    public RepositorioGastos(Conexiones conexiones) => _conexiones = conexiones;

    private static DbString A(string? v, int largo) => new() { Value = v, IsAnsi = true, Length = largo };

    public (long Numero, string Encf) Crear(DatosGasto d, string usuario, string? equipo)
    {
        var total = GastoMenorEcf.Total(d.Cantidad, d.Precio);
        if (GastoMenorEcf.Validar(d.Descripcion, d.BienServicio, d.Cantidad, d.Precio, d.FormaPago, d.TipoGasto, d.Beneficiario, d.Detalle,
                d.Fecha, DateTime.Today) is { } malo) throw new ErrorCatalogo(malo);
        if (total <= 0) throw new ErrorCatalogo("El gasto no tiene monto.");
        using var cn = _conexiones.Hotel();
        var r = cn.QueryFirst<(int Resultado, string? NCF, string? Mensaje)>("dbo.Stp_CrearGastoMenor", new
        {
            Fecha = d.Fecha.Date,
            Descripcion = A(d.Descripcion.Trim(), 80),
            TipoBienServicio = (byte)d.BienServicio,
            Cantidad = d.Cantidad,
            Precio = d.Precio,
            FormaPago = new DbString { Value = d.FormaPago, IsAnsi = true, IsFixedLength = true, Length = 2 },
            TipoGasto606 = new DbString { Value = d.TipoGasto, IsAnsi = true, IsFixedLength = true, Length = 2 },
            Beneficiario = A(d.Beneficiario.Trim().ToUpperInvariant(), 80),
            Cedula = A(string.IsNullOrWhiteSpace(d.Cedula) ? null : d.Cedula.Trim(), 20),
            Detalle = A(d.Detalle.Trim(), 500),
            Usuario = A(usuario.Length > 30 ? usuario[..30] : usuario, 30),
            Equipo = A(equipo is { Length: > 60 } e ? e[..60] : equipo ?? "HotelWeb", 60),
        }, commandType: CommandType.StoredProcedure);
        if (r.Resultado <= 0) throw new ErrorCatalogo(r.Mensaje ?? "No se pudo crear el gasto.");
        return (r.Resultado, r.NCF!.Trim());
    }

    public async Task<IReadOnlyList<GastoLista>> Lista(DateTime desde, DateTime hasta, ServicioEcf ecf)
    {
        if (hasta.Date < desde.Date) throw new ErrorCatalogo("La fecha final es antes de la inicial.");
        if ((hasta.Date - desde.Date).TotalDays > 93) throw new ErrorCatalogo("Escoja a lo sumo tres meses.");
        List<GastoMenor> gastos;
        using (var cn = _conexiones.Hotel())
            gastos = cn.Query<int>("SELECT Gasto_id FROM HotelGastoMenor WITH (NOLOCK) WHERE Fecha >= @d AND Fecha < @h ORDER BY Gasto_id DESC",
                    new { d = desde.Date, h = hasta.Date.AddDays(1) })
                .Select(id => ServicioEcf.LeerGasto(cn, null, id)!).ToList();
        var estados = await ecf.Estados(gastos.Select(GastoMenorEcf.ComoFactura).ToList());
        return gastos.Select(g => new GastoLista(g.Numero, g.Fecha, g.Encf, g.Descripcion, g.Beneficiario, g.Total,
            GastoMenorEcf.FormasPago.GetValueOrDefault(g.FormaPago, g.FormaPago), g.Usuario, estados.GetValueOrDefault(g.Encf))).ToList();
    }

    public async Task<TicketGasto> Ticket(long numero, ServicioEcf ecf)
    {
        GastoMenor g;
        EmisorEcf emisor;
        DateTime venc;
        using (var cn = _conexiones.Hotel())
        {
            g = ServicioEcf.LeerGasto(cn, null, numero) ?? throw new ErrorCatalogo($"No existe el gasto menor {numero}.");
            emisor = ServicioEcf.Emisor(cn);
            venc = ServicioEcf.FechaVencimiento(cn, g.Encf);
        }
        var timbre = await ecf.Timbre(GastoMenorEcf.ComoFactura(g), emisor.Rnc);
        return new TicketGasto(g, emisor, venc, GastoMenorEcf.FormasPago.GetValueOrDefault(g.FormaPago, g.FormaPago),
            $"{g.TipoGasto} {GastoMenorEcf.TiposGasto.GetValueOrDefault(g.TipoGasto, "")}".Trim(), timbre);
    }
}
