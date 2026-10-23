using System.Data;
using System.Globalization;
using System.Xml.Linq;
using Dapper;
using HotelWeb.Api.Catalogos;
using HotelWeb.Api.Datos;
using HotelWeb.Core.Ecf;

namespace HotelWeb.Api.Ecf;

public sealed record DatosCompra(DateTime Fecha, DateTime FechaPago, string Cedula, string Proveedor, string? Direccion, bool Verificado, string TipoIsr,
    string FormaPago, string TipoGasto, string? Observacion, IReadOnlyList<LineaCompra> Lineas);

public sealed record CompraLista(long Numero, DateTime Fecha, string Encf, string Cedula, string Proveedor, decimal Total, decimal Retenido,
    decimal NetoPagar, string FormaPago, string? Usuario, EstadoEcf? Dgii);

public sealed record TicketCompra(CompraInformal Compra, EmisorEcf Emisor, DateTime? Vencimiento, string FormaPago, string TipoGasto, string RetencionIsr,
    TimbreEcf Timbre);

/// <summary>
/// Compras a proveedores informales (E41, etapa 9d) sobre lo de Delphi (MIG_16): Stp_CrearCompraInformal revisa, calcula las
/// retenciones, toma el siguiente E41 dentro de la secuencia autorizada y graba; despues se arma el XML y se envia. Una
/// compra aceptada no se anula ni se cambia.
/// </summary>
public sealed class RepositorioCompras
{
    private readonly Conexiones _conexiones;

    public RepositorioCompras(Conexiones conexiones) => _conexiones = conexiones;

    private static DbString A(string? v, int largo) => new() { Value = v, IsAnsi = true, Length = largo };

    /// <summary>Las lineas como las lee el SP: &lt;L d="..." t="1|2" c="1.00" p="100.00" g="0|1"/&gt;.</summary>
    public static string LineasXml(IEnumerable<LineaCompra> lineas) =>
        string.Concat(lineas.Select(l => new XElement("L",
            new XAttribute("d", l.Descripcion.Trim()),
            new XAttribute("t", l.BienServicio.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("c", l.Cantidad.ToString("0.00", CultureInfo.InvariantCulture)),
            new XAttribute("p", l.Precio.ToString("0.00", CultureInfo.InvariantCulture)),
            new XAttribute("g", l.Gravado ? "1" : "0")).ToString(SaveOptions.DisableFormatting)));

    public (long Numero, string Encf) Crear(DatosCompra d, string usuario, string? equipo)
    {
        if (CompraInformalEcf.Validar(d.Fecha, d.FechaPago, d.Cedula, d.Proveedor, d.Direccion, d.Verificado, d.TipoIsr, d.FormaPago, d.TipoGasto,
                d.Observacion, d.Lineas, DateTime.Today) is { } malo) throw new ErrorCatalogo(malo);
        using var cn = _conexiones.Hotel();
        var r = cn.QueryFirst<(int Resultado, string? NCF, string? Mensaje)>("dbo.Stp_CrearCompraInformal", new
        {
            Fecha = d.Fecha.Date,
            FechaPago = d.FechaPago.Date,
            Cedula = A(d.Cedula.Replace("-", "").Replace(" ", "").Trim(), 20),
            Proveedor = A(d.Proveedor.Trim().ToUpperInvariant(), 200),
            Direccion = A(d.Direccion?.Trim(), 200),
            Verificado = d.Verificado,
            TipoRetencionIsr = new DbString { Value = d.TipoIsr, IsAnsi = true, IsFixedLength = true, Length = 1 },
            FormaPago = new DbString { Value = d.FormaPago, IsAnsi = true, IsFixedLength = true, Length = 2 },
            TipoGasto606 = new DbString { Value = d.TipoGasto, IsAnsi = true, IsFixedLength = true, Length = 2 },
            Observacion = A(d.Observacion?.Trim(), 600),
            Lineas = new DbString { Value = LineasXml(d.Lineas), IsAnsi = false, Length = -1 },
            Usuario = A(usuario.Length > 30 ? usuario[..30] : usuario, 30),
            Equipo = A(equipo is { Length: > 60 } e ? e[..60] : equipo ?? "HotelWeb", 60),
        }, commandType: CommandType.StoredProcedure);
        if (r.Resultado <= 0) throw new ErrorCatalogo(r.Mensaje ?? "No se pudo crear la compra.");
        return (r.Resultado, r.NCF!.Trim());
    }

    public async Task<IReadOnlyList<CompraLista>> Lista(DateTime desde, DateTime hasta, ServicioEcf ecf)
    {
        if (hasta.Date < desde.Date) throw new ErrorCatalogo("La fecha final es antes de la inicial.");
        if ((hasta.Date - desde.Date).TotalDays > 93) throw new ErrorCatalogo("Escoja a lo sumo tres meses.");
        List<CompraInformal> compras;
        using (var cn = _conexiones.Hotel())
            compras = cn.Query<int>("SELECT Compra_id FROM HotelCompraInformal WITH (NOLOCK) WHERE Fecha >= @d AND Fecha < @h ORDER BY Compra_id DESC",
                    new { d = desde.Date, h = hasta.Date.AddDays(1) })
                .Select(id => ServicioEcf.LeerCompra(cn, null, id)!).ToList();
        var estados = await ecf.Estados(compras.Select(CompraInformalEcf.ComoFactura).ToList());
        return compras.Select(c => new CompraLista(c.Numero, c.Fecha, c.Encf, c.Cedula, c.Proveedor, c.Total, c.ItbisRetenido + c.IsrRetenido, c.NetoPagar,
            GastoMenorEcf.FormasPago.GetValueOrDefault(c.FormaPago, c.FormaPago), c.Usuario, estados.GetValueOrDefault(c.Encf))).ToList();
    }

    public async Task<TicketCompra> Ticket(long numero, ServicioEcf ecf)
    {
        CompraInformal c;
        EmisorEcf emisor;
        DateTime venc;
        using (var cn = _conexiones.Hotel())
        {
            c = ServicioEcf.LeerCompra(cn, null, numero) ?? throw new ErrorCatalogo($"No existe la compra {numero}.");
            emisor = ServicioEcf.Emisor(cn);
            venc = ServicioEcf.FechaVencimiento(cn, c.Encf);
        }
        var timbre = await ecf.Timbre(CompraInformalEcf.ComoFactura(c), emisor.Rnc);
        return new TicketCompra(c, emisor, venc, GastoMenorEcf.FormasPago.GetValueOrDefault(c.FormaPago, c.FormaPago),
            $"{c.TipoGasto} {GastoMenorEcf.TiposGasto.GetValueOrDefault(c.TipoGasto, "")}".Trim(),
            CompraInformalEcf.RetencionesIsr.TryGetValue(c.TipoIsr, out var r) ? r.Nombre : c.TipoIsr, timbre);
    }
}
