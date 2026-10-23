using System.Data;
using Dapper;
using HotelWeb.Api.Catalogos;
using HotelWeb.Api.Datos;
using HotelWeb.Api.Ecf;
using HotelWeb.Core.Caja;
using HotelWeb.Core.Ecf;

namespace HotelWeb.Api.Caja;

/// <summary>Lo pendiente de un cajero: lo que entraria si cierra ahora.</summary>
public sealed record CuadreCaja(AmbitoCaja Ambito, string Usuario, DateTime? Desde, IReadOnlyList<MovimientoCaja> Movimientos, ResumenCaja Resumen);

public sealed record CierreResumen(int Cierre, AmbitoCaja Ambito, string Usuario, DateTime Hasta, decimal Total, decimal EsperadoEfectivo,
    decimal ContadoEfectivo, decimal EsperadoDolares, decimal ContadoDolares, string? Comentario);

public sealed record CierreImpreso(EmisorEcf Emisor, CierreResumen Cierre, DateTime? Desde, IReadOnlyList<MovimientoCaja> Movimientos, ResumenCaja Resumen);

/// <summary>
/// Cuadre y cierre de caja por cajero (etapa 9a, MIG_25). Hotel: los recibos (HotelReciboIngreso, incluye los walk-in) y
/// los adelantos de reserva sin postear del usuario; al cerrar se marcan Posteado = 1, como el cuadre de Delphi.
/// Cafeteria: lo que el usuario cobro en las mesas (faFacturaPago) que todavia no entro en un cierre suyo.
/// </summary>
public sealed class RepositorioCaja
{
    private const string Hotel = "HOTEL";
    private const string Cafe = "CAFETERIA";
    private readonly Conexiones _conexiones;

    public RepositorioCaja(Conexiones conexiones) => _conexiones = conexiones;

    private static DbString A(string? v, int largo) => new() { Value = v, IsAnsi = true, Length = largo };
    private static string U(string login) => (login.Length > 10 ? login[..10] : login).Trim();
    private static string Nombre(AmbitoCaja a) => a == AmbitoCaja.Hotel ? Hotel : Cafe;

    public CuadreCaja Pendiente(AmbitoCaja ambito, string usuario)
    {
        using var cn = _conexiones.Hotel();
        cn.Open();
        return Pendiente(cn, null, ambito, U(usuario));
    }

    private static CuadreCaja Pendiente(IDbConnection cn, IDbTransaction? tx, AmbitoCaja ambito, string u)
    {
        DateTime? desde = ambito == AmbitoCaja.Hotel ? null : DesdeCafeteria(cn, tx, u);
        var m = ambito == AmbitoCaja.Hotel ? MovimientosHotel(cn, tx, u, null) : MovimientosCafeteria(cn, tx, u, desde, null);
        return new CuadreCaja(ambito, u, desde, m, ReglasCaja.Resumen(ambito, m));
    }

    /// <summary>Cafeteria: desde el primer cierre del usuario (lo cerrado se descarta por el detalle); si nunca cerro, desde hoy.</summary>
    private static DateTime DesdeCafeteria(IDbConnection cn, IDbTransaction? tx, string u) =>
        cn.ExecuteScalar<DateTime?>("SELECT MIN(Desde) FROM HotelCierreCaja WHERE Ambito = 'CAFETERIA' AND RTRIM(Usuario) = @u",
            new { u = A(u, 10) }, tx) ?? DateTime.Today;

    /// <summary>Los recibos y adelantos del usuario sin postear (cierre null), o los de un cierre.</summary>
    private static List<MovimientoCaja> MovimientosHotel(IDbConnection cn, IDbTransaction? tx, string? u, int? cierre) =>
        cn.Query<(string Tipo, decimal Numero, DateTime Fecha, string? Referencia, string? Nombre, int Forma, string? FormaNombre, decimal? Monto, decimal? Us)>("""
            SELECT 'R', r.NumeroRecibo, r.Fecha,
                   CASE WHEN ISNULL(r.FacturaWalking_id, 0) > 0 THEN 'Walk-in ' + LTRIM(STR(r.FacturaWalking_id)) ELSE 'Hab. ' + LTRIM(STR(r.Habitacion)) END,
                   RTRIM(r.Nombre), d.Monedas_id, RTRIM(m.NombreMoneda), d.Monto, d.MontoUs
            FROM HotelReciboIngreso r JOIN HotelReciboDetalle d ON d.NumeroRecibo = r.NumeroRecibo LEFT JOIN ConMonedas m ON m.Monedas_id = d.Monedas_id
            WHERE (@cierre IS NULL AND r.Posteado = 0 AND RTRIM(r.Usuario) = @u AND RTRIM(r.Estatus) <> 'Anulado')
               OR (@cierre IS NOT NULL AND EXISTS (SELECT 1 FROM HotelCierreCajaDetalle x WHERE x.Cierre_id = @cierre AND x.Tipo = 'R' AND x.Numero = r.NumeroRecibo))
            UNION ALL
            SELECT 'A', a.ReservaAdelando_id, a.Fecha, 'Reserva ' + LTRIM(STR(a.Reserva_id)), RTRIM(c.Nombre), a.Monedas_id, RTRIM(m.NombreMoneda),
                   a.Monto, a.Montous
            FROM HotelReservaAdelanto a LEFT JOIN CxcCliente c ON c.NumeroCliente = a.Cliente_id LEFT JOIN ConMonedas m ON m.Monedas_id = a.Monedas_id
            WHERE (@cierre IS NULL AND a.Posteado = 0 AND RTRIM(a.Usuario) = @u AND RTRIM(a.Estatus) = 'Activa')
               OR (@cierre IS NOT NULL AND EXISTS (SELECT 1 FROM HotelCierreCajaDetalle x WHERE x.Cierre_id = @cierre AND x.Tipo = 'A' AND x.Numero = a.ReservaAdelando_id))
            -- gastos menores (E43) y compras a informales (E41) pagados en efectivo: salen de la gaveta (MIG_26). Los que
            -- no estan en un cierre, desde el dia del primer cierre del hotel del usuario (o desde hoy)
            UNION ALL
            SELECT 'G', g.Gasto_id, g.Fecha, 'Gasto menor ' + RTRIM(g.NCF), RTRIM(g.Beneficiario), -43, 'GASTOS MENORES (E43)', g.Total, 0
            FROM HotelGastoMenor g
            WHERE (@cierre IS NULL AND g.FormaPago = '01' AND LEFT(RTRIM(g.Usuario), 10) = @u AND g.FechaAdicion >= @desde
                   AND NOT EXISTS (SELECT 1 FROM HotelCierreCajaDetalle x WHERE x.Tipo = 'G' AND x.Numero = g.Gasto_id))
               OR (@cierre IS NOT NULL AND EXISTS (SELECT 1 FROM HotelCierreCajaDetalle x WHERE x.Cierre_id = @cierre AND x.Tipo = 'G' AND x.Numero = g.Gasto_id))
            UNION ALL
            SELECT 'C', c.Compra_id, c.Fecha, 'Compra ' + RTRIM(c.NCF), RTRIM(c.Proveedor), -41, 'COMPRAS A INFORMALES (E41)', c.NetoPagar, 0
            FROM HotelCompraInformal c
            WHERE (@cierre IS NULL AND c.FormaPago = '01' AND LEFT(RTRIM(c.Usuario), 10) = @u AND c.FechaAdicion >= @desde
                   AND NOT EXISTS (SELECT 1 FROM HotelCierreCajaDetalle x WHERE x.Tipo = 'C' AND x.Numero = c.Compra_id))
               OR (@cierre IS NOT NULL AND EXISTS (SELECT 1 FROM HotelCierreCajaDetalle x WHERE x.Cierre_id = @cierre AND x.Tipo = 'C' AND x.Numero = c.Compra_id))
            ORDER BY 1 DESC, 2
            """, new
            {
                u = A(u, 10), cierre,
                desde = u is null ? DateTime.Today : cn.ExecuteScalar<DateTime?>(
                    "SELECT CAST(MIN(FechaAdicion) AS date) FROM HotelCierreCaja WHERE Ambito = 'HOTEL' AND RTRIM(Usuario) = @u",
                    new { u = A(u, 10) }, tx) ?? DateTime.Today,
            }, tx)
            .Select(x => new MovimientoCaja(x.Tipo[0], (long)x.Numero, x.Fecha, x.Referencia, x.Nombre, x.Forma, x.FormaNombre ?? $"Forma {x.Forma}",
                x.Monto ?? 0, x.Us ?? 0)).ToList();

    /// <summary>Lo cobrado por el usuario en la cafeteria que no esta en ningun cierre (cierre null), o lo de un cierre.</summary>
    private static List<MovimientoCaja> MovimientosCafeteria(IDbConnection cn, IDbTransaction? tx, string? u, DateTime? desde, int? cierre) =>
        cn.Query<(int Numero, DateTime Fecha, int Mesa, string? Nombre, int Forma, string? FormaNombre, decimal Monto)>("""
            SELECT f.InvFactura, p.FechaAdicion, f.Mesa_id, RTRIM(f.NombreCliente), p.FormaPago_id, RTRIM(fp.Formadepago), p.MontoPago
            FROM faFacturaPago p JOIN FaTmpFactura f ON f.InvFactura = p.InvFactura LEFT JOIN faformadepago fp ON fp.Formapago_id = p.FormaPago_id
            WHERE (@cierre IS NULL AND RTRIM(p.Usuario) = @u AND p.FechaAdicion >= @desde
                   AND NOT EXISTS (SELECT 1 FROM HotelCierreCajaDetalle x WHERE x.Tipo = 'F' AND x.Numero = p.InvFactura))
               OR (@cierre IS NOT NULL AND EXISTS (SELECT 1 FROM HotelCierreCajaDetalle x WHERE x.Cierre_id = @cierre AND x.Tipo = 'F' AND x.Numero = p.InvFactura))
            ORDER BY f.InvFactura, p.Secuencia
            """, new { u = A(u, 10), desde, cierre }, tx)
            .Select(x => new MovimientoCaja('F', x.Numero, x.Fecha, $"Mesa {x.Mesa}", x.Nombre, x.Forma, x.FormaNombre ?? $"Forma {x.Forma}", x.Monto, 0))
            .ToList();

    /// <summary>
    /// Cierra lo pendiente del usuario. <paramref name="visto"/> es cuantos movimientos y cuanto vio el cajero al contar:
    /// si entro algo mientras tanto, no cierra (que vuelva a mirar).
    /// </summary>
    public CierreResumen Cerrar(AmbitoCaja ambito, string usuario, decimal contadoEfectivo, decimal contadoDolares, string? comentario,
        (int Movimientos, decimal Ingresos) visto, string? equipo)
    {
        var u = U(usuario);
        using var cn = _conexiones.Hotel();
        cn.Open();
        using var tx = cn.BeginTransaction();
        cn.Execute("EXEC sp_getapplock @Resource = @r, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 20000",
            new { r = $"HotelWeb_Caja_{Nombre(ambito)}_{u}" }, tx);
        var p = Pendiente(cn, tx, ambito, u);
        if (p.Movimientos.Count != visto.Movimientos || p.Resumen.Ingresos != visto.Ingresos)
            throw new ErrorCatalogo("Entró un cobro nuevo mientras cuadraba: revise el cuadre y cierre otra vez.");
        var c = string.IsNullOrWhiteSpace(comentario) ? null : comentario.Trim();
        if (ReglasCaja.ValidarCierre(p.Resumen, p.Movimientos.Count, contadoEfectivo, contadoDolares, c) is { } malo) throw new ErrorCatalogo(malo);

        var id = cn.ExecuteScalar<int>("""
            INSERT INTO HotelCierreCaja (Ambito, Usuario, Desde, Hasta, Total, EsperadoEfectivo, ContadoEfectivo, EsperadoDolares, ContadoDolares,
                   Comentario, Equipo)
            OUTPUT INSERTED.Cierre_id
            VALUES (@ambito, @u, @desde, GETDATE(), @total, @espEf, @conEf, @espUs, @conUs, @c, @equipo)
            """, new
        {
            ambito = A(Nombre(ambito), 10), u = A(u, 10), desde = p.Desde, total = p.Resumen.Ingresos, espEf = p.Resumen.Efectivo, conEf = contadoEfectivo,
            espUs = p.Resumen.Dolares, conUs = contadoDolares, c = A(string.IsNullOrEmpty(c) ? null : c, 200),
            equipo = A(equipo is { Length: > 60 } e ? e[..60] : equipo, 60),
        }, tx);
        foreach (var d in p.Movimientos.Select(m => (m.Tipo, m.Numero)).Distinct())
            cn.Execute("INSERT INTO HotelCierreCajaDetalle (Cierre_id, Tipo, Numero) VALUES (@id, @tipo, @numero)",
                new { id, tipo = A(d.Tipo.ToString(), 1), numero = (decimal)d.Numero }, tx);
        if (ambito == AmbitoCaja.Hotel)
        {
            // como el "cerrar el turno" de Delphi, pero solo lo del usuario (y sus anulados, que no cuentan)
            cn.Execute("UPDATE HotelReciboIngreso SET Posteado = 1 WHERE Posteado = 0 AND RTRIM(Usuario) = @u", new { u = A(u, 10) }, tx);
            cn.Execute("""
                UPDATE a SET Posteado = 1 FROM HotelReservaAdelanto a
                WHERE a.Posteado = 0 AND EXISTS (SELECT 1 FROM HotelCierreCajaDetalle x WHERE x.Cierre_id = @id AND x.Tipo = 'A' AND x.Numero = a.ReservaAdelando_id)
                """, new { id }, tx);
        }
        tx.Commit();
        return new CierreResumen(id, ambito, u, DateTime.Now, p.Resumen.Ingresos, p.Resumen.Efectivo, contadoEfectivo, p.Resumen.Dolares,
            contadoDolares, c);
    }

    public CierreImpreso Imprimible(int cierre)
    {
        using var cn = _conexiones.Hotel();
        cn.Open();
        var c = Leer(cn, cierre) ?? throw new ErrorCatalogo($"No existe el cierre {cierre}.");
        var desde = cn.ExecuteScalar<DateTime?>("SELECT Desde FROM HotelCierreCaja WHERE Cierre_id = @cierre", new { cierre });
        var m = c.Ambito == AmbitoCaja.Hotel ? MovimientosHotel(cn, null, null, cierre) : MovimientosCafeteria(cn, null, null, null, cierre);
        return new CierreImpreso(ServicioEcf.Emisor(cn), c, desde, m, ReglasCaja.Resumen(c.Ambito, m));
    }

    /// <summary>Los cierres del usuario (los mas nuevos primero).</summary>
    public IReadOnlyList<CierreResumen> Cierres(AmbitoCaja ambito, string usuario)
    {
        using var cn = _conexiones.Hotel();
        return cn.Query<FilaCierre>("""
            SELECT TOP 30 Cierre_id, RTRIM(Ambito) AS Ambito, RTRIM(Usuario) AS Usuario, Hasta, Total, EsperadoEfectivo, ContadoEfectivo,
                   EsperadoDolares, ContadoDolares, RTRIM(Comentario) AS Comentario
            FROM HotelCierreCaja WHERE Ambito = @ambito AND RTRIM(Usuario) = @u ORDER BY Cierre_id DESC
            """, new { ambito = A(Nombre(ambito), 10), u = A(U(usuario), 10) }).Select(Vista).ToList();
    }

    private sealed class FilaCierre
    {
        public int Cierre_id { get; set; }
        public string Ambito { get; set; } = "";
        public string Usuario { get; set; } = "";
        public DateTime Hasta { get; set; }
        public decimal Total { get; set; }
        public decimal EsperadoEfectivo { get; set; }
        public decimal ContadoEfectivo { get; set; }
        public decimal EsperadoDolares { get; set; }
        public decimal ContadoDolares { get; set; }
        public string? Comentario { get; set; }
    }

    private static CierreResumen Vista(FilaCierre f) => new(f.Cierre_id, f.Ambito == Hotel ? AmbitoCaja.Hotel : AmbitoCaja.Cafeteria, f.Usuario,
        f.Hasta, f.Total, f.EsperadoEfectivo, f.ContadoEfectivo, f.EsperadoDolares, f.ContadoDolares, f.Comentario);

    private static CierreResumen? Leer(IDbConnection cn, int cierre) =>
        cn.Query<FilaCierre>("""
            SELECT Cierre_id, RTRIM(Ambito) AS Ambito, RTRIM(Usuario) AS Usuario, Hasta, Total, EsperadoEfectivo, ContadoEfectivo,
                   EsperadoDolares, ContadoDolares, RTRIM(Comentario) AS Comentario
            FROM HotelCierreCaja WHERE Cierre_id = @cierre
            """, new { cierre }).Select(Vista).FirstOrDefault();
}
