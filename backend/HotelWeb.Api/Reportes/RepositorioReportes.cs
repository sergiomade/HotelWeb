using System.Data;
using Dapper;
using HotelWeb.Api.Catalogos;
using HotelWeb.Api.Datos;
using HotelWeb.Api.Ecf;
using HotelWeb.Core.Ecf;

namespace HotelWeb.Api.Reportes;

/// <param name="Origen">Check-out o Walk-in.</param>
public sealed record VentaHotel(string Origen, long Numero, DateTime Fecha, string? Habitacion, string? Cliente, string? Encf, decimal SubTotal,
    decimal Itbis, decimal Propina, decimal Total, bool Transferida);

/// <param name="Exceso">Lo pagado de mas (Delphi dejaba poner en la tarjeta mas que el total: 1,489 de 1,508 desde 2025-06).</param>
public sealed record VentaCafeteria(long Numero, DateTime Fecha, int Mesa, string? Cliente, string? Encf, decimal Alimentos, decimal Bebidas,
    decimal Otros, decimal SubTotal, decimal Itbis, decimal Propina, decimal Total, decimal Efectivo, decimal Tarjeta, decimal Credito,
    decimal Habitacion, decimal Exceso, int? HabitacionId);

/// <param name="Origen">Check-out, Walk-in, Cafeteria o la nota de credito de cada uno.</param>
public sealed record Comprobante(string Origen, long Numero, DateTime Fecha, string Tipo, string Ncf, string? Rnc, string? Cliente, decimal Base,
    decimal Itbis, decimal Propina, decimal Total, string? Modifica, string? Estado);

public sealed record TipoResumen(string Tipo, int Cantidad, decimal Base, decimal Itbis, decimal Propina, decimal Total);

public sealed record ReporteComprobantes(IReadOnlyList<Comprobante> Lista, IReadOnlyList<TipoResumen> PorTipo);

public sealed record FacturaCancelada(int Cancelacion, DateTime FechaCancelacion, long Factura, int? Habitacion, string? Cliente, decimal Cargos,
    string? Autoriza, string? Usuario, string? Motivo);

/// <summary>
/// Reportes de ventas y comprobantes (etapa 9b), como Stp_ListadodeFacturas (ventas del periodo), Stp_ListadoVentasDiarias
/// (cafeteria: alimentos, bebidas y formas de pago), Stp_ListaFacturasxComprobante (NCF de las facturas, con las notas
/// E34) y Stp_FacturasCanceladasHotel. Solo lectura; el estado de la DGII sale de EcfEstadoDgii (sin llamar a la API).
/// </summary>
public sealed class RepositorioReportes
{
    public const int DiasMaximo = 93;
    private readonly Conexiones _conexiones;

    public RepositorioReportes(Conexiones conexiones) => _conexiones = conexiones;

    private static (DateTime Desde, DateTime Hasta) Rango(DateTime desde, DateTime hasta)
    {
        if (hasta.Date < desde.Date) throw new ErrorCatalogo("La fecha final es antes de la inicial.");
        if ((hasta.Date - desde.Date).TotalDays > DiasMaximo) throw new ErrorCatalogo("Escoja a lo sumo tres meses.");
        return (desde.Date, hasta.Date.AddDays(1));
    }

    public IReadOnlyList<VentaHotel> VentasHotel(DateTime desde, DateTime hasta)
    {
        var (d, h) = Rango(desde, hasta);
        using var cn = _conexiones.Hotel();
        return cn.Query<VentaHotel>("""
            SELECT 'Check-out' AS Origen, CAST(f.Factura_id AS bigint) AS Numero, f.Fecha, LTRIM(STR(f.Habitacion_id)) AS Habitacion,
                   ISNULL(RTRIM(c.Nombre), RTRIM(f.Huesped)) AS Cliente, RTRIM(f.NCF) AS Encf, ISNULL(f.SubTotal, 0) AS SubTotal,
                   ISNULL(f.Itbis, 0) AS Itbis, ISNULL(f.Propina, 0) AS Propina,
                   ISNULL(f.SubTotal, 0) + ISNULL(f.Itbis, 0) + ISNULL(f.Propina, 0) AS Total, CAST(ISNULL(f.Transferida, 0) AS bit) AS Transferida
            FROM HotelTmpFactura f WITH (NOLOCK) LEFT JOIN CxcCliente c WITH (NOLOCK) ON c.NumeroCliente = f.Cliente_id
            WHERE f.Fecha >= @d AND f.Fecha < @h
            UNION ALL
            SELECT 'Walk-in', CAST(w.FacturaWalking_id AS bigint), w.Fecha, NULL, RTRIM(w.Nombre), RTRIM(w.NCF), ISNULL(w.SubTotal, 0),
                   ISNULL(w.Itbis, 0), ISNULL(w.Propina, 0), ISNULL(w.SubTotal, 0) + ISNULL(w.Itbis, 0) + ISNULL(w.Propina, 0), CAST(0 AS bit)
            FROM HotelFacturaWalking w WITH (NOLOCK)
            WHERE w.Fecha >= @d AND w.Fecha < @h
            ORDER BY Fecha, Origen, Numero
            """, new { d, h }).ToList();
    }

    public IReadOnlyList<VentaCafeteria> VentasCafeteria(DateTime desde, DateTime hasta)
    {
        var (d, h) = Rango(desde, hasta);
        using var cn = _conexiones.Hotel();
        // alimentos y bebidas por el tipo del articulo (InvArticulo.TipoArticulo_id 1 y 2), como Stp_ListadoVentasDiarias
        return cn.Query<VentaCafeteria>("""
            SELECT CAST(f.InvFactura AS bigint) AS Numero, f.Fecha, f.Mesa_id AS Mesa, RTRIM(f.NombreCliente) AS Cliente, RTRIM(f.NCF) AS Encf,
                   ISNULL(x.Alimentos, 0) AS Alimentos, ISNULL(x.Bebidas, 0) AS Bebidas, ISNULL(x.Otros, 0) AS Otros,
                   f.Subtotal AS SubTotal, f.Itbis, f.Propina, f.Total,
                   ISNULL(p.Efectivo, 0) AS Efectivo, ISNULL(p.Tarjeta, 0) AS Tarjeta, ISNULL(p.Credito, 0) AS Credito, ISNULL(p.Habitacion, 0) AS Habitacion,
                   CASE WHEN ISNULL(p.Dinero, 0) > f.Total + 0.009 THEN ISNULL(p.Dinero, 0) - f.Total ELSE 0 END AS Exceso,
                   NULLIF(f.Habitacion_id, 0) AS HabitacionId
            FROM FaTmpFactura f WITH (NOLOCK)
            OUTER APPLY (SELECT SUM(CASE WHEN ISNULL(a.TipoArticulo_id, 1) = 1 THEN dd.Importe ELSE 0 END) Alimentos,
                                SUM(CASE WHEN a.TipoArticulo_id = 2 THEN dd.Importe ELSE 0 END) Bebidas,
                                SUM(CASE WHEN a.TipoArticulo_id > 2 THEN dd.Importe ELSE 0 END) Otros
                         FROM FaTmpFacturaDetalle dd WITH (NOLOCK) LEFT JOIN InvArticulo a WITH (NOLOCK) ON a.ArticuloRecord_Id = dd.ArticuloRecord_id
                         WHERE dd.InvFactura = f.InvFactura) x
            OUTER APPLY (SELECT SUM(CASE WHEN pp.FormaPago_id = 1 THEN pp.MontoPago ELSE 0 END) Efectivo,
                                SUM(CASE WHEN pp.FormaPago_id = 2 THEN pp.MontoPago ELSE 0 END) Tarjeta,
                                SUM(CASE WHEN pp.FormaPago_id = 3 THEN pp.MontoPago ELSE 0 END) Credito,
                                SUM(CASE WHEN pp.FormaPago_id = 8 THEN pp.MontoPago ELSE 0 END) Habitacion,
                                SUM(CASE WHEN pp.FormaPago_id <> 8 THEN pp.MontoPago ELSE 0 END) Dinero
                         FROM faFacturaPago pp WITH (NOLOCK) WHERE pp.InvFactura = f.InvFactura) p
            WHERE f.Fecha >= @d AND f.Fecha < @h AND NOT (f.TipoVenta_id = 3 AND f.Total = 0)
            ORDER BY f.Fecha, f.InvFactura
            """, new { d, h }).ToList();
    }

    private sealed class FilaComprobante
    {
        public string Origen { get; set; } = "";
        public long Numero { get; set; }
        public DateTime Fecha { get; set; }
        public string Ncf { get; set; } = "";
        public string? Rnc { get; set; }
        public string? Cliente { get; set; }
        public decimal Base { get; set; }
        public decimal Itbis { get; set; }
        public decimal Propina { get; set; }
        public decimal Total { get; set; }
        public string? Modifica { get; set; }
        public string? Estado { get; set; }
    }

    /// <summary>Todos los comprobantes del periodo (facturas y notas de credito) con su estado en la DGII y el resumen por tipo.</summary>
    public ReporteComprobantes Comprobantes(DateTime desde, DateTime hasta)
    {
        var (d, h) = Rango(desde, hasta);
        using var cn = _conexiones.Hotel();
        cn.Open();
        var filas = cn.Query<FilaComprobante>("""
            SELECT 'Check-out' AS Origen, CAST(f.Factura_id AS bigint) AS Numero, f.Fecha, RTRIM(f.NCF) AS Ncf, RTRIM(f.RNC) AS Rnc,
                   ISNULL(RTRIM(c.Nombre), RTRIM(f.Huesped)) AS Cliente, ISNULL(f.SubTotal, 0) AS Base, ISNULL(f.Itbis, 0) AS Itbis,
                   ISNULL(f.Propina, 0) AS Propina, ISNULL(f.SubTotal, 0) + ISNULL(f.Itbis, 0) + ISNULL(f.Propina, 0) AS Total, NULL AS Modifica
            FROM HotelTmpFactura f WITH (NOLOCK) LEFT JOIN CxcCliente c WITH (NOLOCK) ON c.NumeroCliente = f.Cliente_id
            WHERE f.Fecha >= @d AND f.Fecha < @h AND ISNULL(RTRIM(f.NCF), '') <> '' AND ISNULL(f.Transferida, 0) = 0
            UNION ALL
            SELECT 'Walk-in', CAST(w.FacturaWalking_id AS bigint), w.Fecha, RTRIM(w.NCF),
                   CASE WHEN REPLACE(LTRIM(RTRIM(ISNULL(w.RNC, ''))), '0', '') = '' THEN RTRIM(w.Cedula) ELSE RTRIM(w.RNC) END,
                   RTRIM(w.Nombre), ISNULL(w.SubTotal, 0), ISNULL(w.Itbis, 0), ISNULL(w.Propina, 0),
                   ISNULL(w.SubTotal, 0) + ISNULL(w.Itbis, 0) + ISNULL(w.Propina, 0), NULL
            FROM HotelFacturaWalking w WITH (NOLOCK)
            WHERE w.Fecha >= @d AND w.Fecha < @h AND ISNULL(RTRIM(w.NCF), '') <> ''
            UNION ALL
            SELECT 'Cafeteria', CAST(f.InvFactura AS bigint), f.Fecha, RTRIM(f.NCF), RTRIM(f.RncCliente), RTRIM(f.NombreCliente), f.Subtotal, f.Itbis,
                   f.Propina, f.Total, NULL
            FROM FaTmpFactura f WITH (NOLOCK)
            WHERE f.Fecha >= @d AND f.Fecha < @h AND ISNULL(RTRIM(f.NCF), '') <> ''
            """, new { d, h }).ToList();

        // las notas de credito E34: el desglose de la E34 que fue a la DGII (las tablas guardan el monto con ITBIS 0)
        foreach (var (origen, tabla) in new[] { (OrigenFactura.NotaHospedaje, "HotelFacturaNC"), (OrigenFactura.NotaWalkIn, "HotelFacturaWalkingNC"),
                     (OrigenFactura.NotaCafeteria, "faFacturaNC") })
        {
            var numeros = cn.Query<decimal>($"SELECT Devolucion_id FROM {tabla} WITH (NOLOCK) WHERE Fecha >= @d AND Fecha < @h AND NCF LIKE 'E34%'",
                new { d, h }).ToList();
            foreach (var n in numeros)
            {
                if (ServicioEcf.LeerNota(cn, null, origen, (long)n) is not { } nota) continue;
                var des = NotaCreditoEcf.Desglose(nota.Nota.Monto, nota.Nota.Codigo, nota.Original);
                filas.Add(new FilaComprobante
                {
                    Origen = origen switch { OrigenFactura.NotaWalkIn => "N/C walk-in", OrigenFactura.NotaCafeteria => "N/C cafeteria", _ => "N/C check-out" },
                    Numero = (long)n, Fecha = nota.Nota.Fecha, Ncf = nota.Nota.Encf, Rnc = nota.Original.Rnc, Cliente = nota.Original.Nombre,
                    Base = des.Gravado + des.Exento, Itbis = des.Itbis, Propina = des.Propina, Total = des.Monto, Modifica = nota.Original.Ncf,
                });
            }
        }

        // EcfEstadoDgii tiene una fila por e-NCF y es chica: se lee entera (sin STRING_SPLIT, que esta base puede no tener)
        var estados = cn.Query<(string Encf, string? Estado)>("SELECT RTRIM(eNCF), RTRIM(Estado) FROM EcfEstadoDgii WITH (NOLOCK)")
            .GroupBy(x => x.Encf).ToDictionary(g => g.Key, g => g.First().Estado);
        var lista = filas.Select(f => new Comprobante(f.Origen, f.Numero, f.Fecha, Tipo(f.Ncf), f.Ncf,
                IdentificacionVisible(f.Rnc), f.Cliente, f.Base, f.Itbis, f.Propina, f.Total, f.Modifica,
                f.Ncf.StartsWith('E') ? estados.GetValueOrDefault(f.Ncf) ?? "Sin consultar" : null))
            .OrderBy(c => c.Tipo).ThenBy(c => c.Ncf).ToList();
        var resumen = lista.GroupBy(c => c.Tipo).OrderBy(g => g.Key)
            .Select(g => new TipoResumen(g.Key, g.Count(), g.Sum(c => c.Base), g.Sum(c => c.Itbis), g.Sum(c => c.Propina), g.Sum(c => c.Total))).ToList();
        return new ReporteComprobantes(lista, resumen);
    }

    /// <summary>E31, E32, E34... o, para los NCF viejos, B01, B02... (los de 19 con ceros delante, sin ellos).</summary>
    public static string Tipo(string ncf)
    {
        var n = ncf.Trim();
        if (n.Length == 19 && n.StartsWith("00000000")) n = n[8..];
        return n.Length >= 3 ? n[..3] : n;
    }

    private static string? IdentificacionVisible(string? rnc)
    {
        var r = (rnc ?? "").Trim();
        return r == "" || r.All(c => c == '0') ? null : r;
    }

    private sealed class FilaCancelada
    {
        public int Cancelacion_id { get; set; }
        public DateTime FechaCancelacion { get; set; }
        public decimal Factura_id { get; set; }
        public decimal? Habitacion_id { get; set; }
        public string? Cliente { get; set; }
        public decimal? MontoCargos { get; set; }
        public string? UsuarioAutoriza { get; set; }
        public string? UsuarioSesion { get; set; }
        public string? Motivo { get; set; }
    }

    public IReadOnlyList<FacturaCancelada> Canceladas(DateTime desde, DateTime hasta)
    {
        Rango(desde, hasta);
        using var cn = _conexiones.Hotel();
        return cn.Query<FilaCancelada>("dbo.Stp_FacturasCanceladasHotel", new { Desde = desde.Date, Hasta = hasta.Date },
                commandType: CommandType.StoredProcedure)
            .Select(x => new FacturaCancelada(x.Cancelacion_id, x.FechaCancelacion, (long)x.Factura_id, x.Habitacion_id is null ? null : (int)x.Habitacion_id,
                x.Cliente?.Trim(), x.MontoCargos ?? 0, x.UsuarioAutoriza?.Trim(), x.UsuarioSesion?.Trim(), x.Motivo?.Trim())).ToList();
    }
}
