using System.Data;
using Dapper;
using HotelWeb.Api.Catalogos;
using HotelWeb.Api.Datos;
using HotelWeb.Core.Hospedaje;
using HotelWeb.Core.Rack;

namespace HotelWeb.Api.Hospedaje;

public sealed record OcupadaVista(int Habitacion, long Factura, string? Huesped, string? Cliente, string Impuestos);

public sealed record PreparacionTransferencia(int Habitacion, long Factura, string? Huesped, string? Cliente, string Impuestos,
    decimal Cargos, decimal PayOut, decimal Pagos, decimal Saldo, IReadOnlyList<OcupadaVista> Ocupadas);

public sealed record ResultadoTransferencia(int Desde, int Hasta, long FacturaDestino, long? Cerrada, decimal Cargos, decimal Pagos,
    IReadOnlyList<string> Avisos);

/// <summary>
/// Transferencia de la cuenta a otra habitacion OCUPADA (Delphi: UFrmMntTransferirHuesped, "ocupada", con
/// Stp_TransferiraOcupada). La web no usa ese procedimiento (mezcla cargos y pagos, y no deja en 0 a un cliente sin
/// ITBIS): reglas en ReglasTransferencia. Despues hace el check-out del origen (Stp_CHECKOUT, que con MIG_21 la deja en
/// 0; sin e-NCF ni envio, como Delphi con las transferidas), todo en una transaccion.
/// </summary>
public sealed class RepositorioTransferencia
{
    private readonly Conexiones _conexiones;

    public RepositorioTransferencia(Conexiones conexiones) => _conexiones = conexiones;

    private static DbString A(string? v, int largo) => new() { Value = v, IsAnsi = true, Length = largo };

    private sealed class FilaCuenta
    {
        public decimal Factura_id { get; set; }
        public decimal Habitacion_id { get; set; }
        public string? Huesped { get; set; }
        public string? Cliente { get; set; }
        public bool? Transferida { get; set; }
        public string? NCF { get; set; }
        public bool LlevaItbis { get; set; }
        public bool LlevaPropina { get; set; }
        public bool RegimenEspecial { get; set; }
    }

    private sealed class FilaLinea
    {
        public int Secuencia { get; set; }
        public string? Servicio_id { get; set; }
        public string? Descripcion { get; set; }
        public int? Cantidad { get; set; }
        public decimal? Precio { get; set; }
        public decimal? Importe { get; set; }
        public bool? Activo { get; set; }
    }

    private const string SelCuenta = """
        SELECT f.Factura_id, f.Habitacion_id, COALESCE(NULLIF(RTRIM(f.Huesped), ''), RTRIM(c.Nombre)) AS Huesped, RTRIM(c.Nombre) AS Cliente,
               f.Transferida, RTRIM(f.NCF) AS NCF, CAST(ISNULL(c.Itbis, 0) AS bit) AS LlevaItbis, CAST(ISNULL(c.Propina, 0) AS bit) AS LlevaPropina,
               CAST(CASE WHEN c.CodigoCompFiscal = 13 THEN 1 ELSE 0 END AS bit) AS RegimenEspecial
        FROM HotelFactura f {0}
        LEFT JOIN CxcCliente c WITH (NOLOCK) ON c.NumeroCliente = f.Cliente_id
        """;

    private static FilaCuenta? Cuenta(IDbConnection cn, IDbTransaction? tx, int habitacion) =>
        cn.QueryFirstOrDefault<FilaCuenta>(string.Format(SelCuenta, tx is null ? "WITH (NOLOCK)" : "WITH (UPDLOCK)")
            + " WHERE f.Habitacion_id = @habitacion ORDER BY f.Factura_id DESC", new { habitacion }, tx);

    private static List<FilaLinea> Lineas(IDbConnection cn, IDbTransaction? tx, decimal factura) =>
        cn.Query<FilaLinea>("""
            SELECT Secuencia, RTRIM(Servicio_id) AS Servicio_id, RTRIM(Descripcion) AS Descripcion, Cantidad, Precio, Importe, Activo
            FROM HotelFacturaDetalle WHERE Factura_id = @factura ORDER BY Secuencia
            """, new { factura }, tx).ToList();

    private static List<(LineaCuenta, string)> Para(List<FilaLinea> l) => l.Select(x =>
        (new LineaCuenta(x.Secuencia, x.Descripcion ?? "", x.Cantidad ?? 1, x.Precio ?? 0, x.Importe ?? 0, x.Activo == true, null, null),
            x.Servicio_id ?? "")).ToList();

    private static string Impuestos(FilaCuenta c) =>
        string.Join(" y ", new[] { c.LlevaItbis && !c.RegimenEspecial ? "ITBIS" : null, c.LlevaPropina ? "propina" : null }
            .Where(x => x is not null)) is { Length: > 0 } s ? s : "sin ITBIS ni propina";

    public PreparacionTransferencia Preparar(int habitacion)
    {
        using var cn = _conexiones.Hotel();
        var c = Cuenta(cn, null, habitacion) ?? throw new ErrorCatalogo($"La habitacion {habitacion} no tiene cuenta abierta.");
        if (c.Transferida == true) throw new ErrorCatalogo("Esa cuenta ya fue transferida.");
        var plan = ReglasTransferencia.Planear(Para(Lineas(cn, null, c.Factura_id)), habitacion, (long)c.Factura_id,
            c.LlevaItbis, c.LlevaPropina, c.RegimenEspecial);
        var saldo = cn.ExecuteScalar<decimal>("SELECT dbo.BalanceActualHabitacion(@f)", new { f = c.Factura_id });
        var ocupadas = cn.Query<FilaCuenta>(string.Format(SelCuenta, "WITH (NOLOCK)")
                + " WHERE f.Habitacion_id <> @habitacion AND ISNULL(f.Transferida, 0) = 0 ORDER BY f.Habitacion_id", new { habitacion })
            .GroupBy(x => x.Habitacion_id).Select(g => g.OrderByDescending(x => x.Factura_id).First())
            .Select(x => new OcupadaVista((int)x.Habitacion_id, (long)x.Factura_id, x.Huesped, x.Cliente, Impuestos(x))).ToList();
        return new PreparacionTransferencia(habitacion, (long)c.Factura_id, c.Huesped, c.Cliente, Impuestos(c), plan.Cargos,
            plan.PayOut, plan.Pagos, saldo, ocupadas);
    }

    public ResultadoTransferencia Transferir(int desde, int hasta, string usuario)
    {
        using var cn = _conexiones.Hotel();
        cn.Open();
        using var tx = cn.BeginTransaction(IsolationLevel.Serializable);
        var o = Cuenta(cn, tx, desde) ?? throw new ErrorCatalogo($"La habitacion {desde} no tiene cuenta abierta.");
        var d = Cuenta(cn, tx, hasta) ?? throw new ErrorCatalogo($"La habitacion {hasta} no esta ocupada: use Cambiar habitacion.");
        var lineas = Lineas(cn, tx, o.Factura_id);
        var plan = ReglasTransferencia.Planear(Para(lineas), desde, (long)o.Factura_id, o.LlevaItbis, o.LlevaPropina, o.RegimenEspecial);
        if (ReglasTransferencia.Validar(desde, hasta, o.Transferida == true, d.Transferida == true, plan.Destino.Count > 0) is { } motivo)
            throw new ErrorCatalogo(motivo);

        var u = A(usuario.Length > 10 ? usuario[..10] : usuario, 10);
        // destino: los cargos (servicio 9), el PAY-OUT y los pagos, como lineas nuevas (Referencia e Impreso como Delphi)
        foreach (var l in plan.Destino)
            Insertar(cn, tx, d.Factura_id, l.Servicio, l.Descripcion, l.Precio, l.Importe, u);

        // origen: sus pagos se van (inactivos) y la linea de cierre lo deja en 0; encabezado en 0 y Transferida, como Delphi
        if (plan.PagosOrigen.Count > 0)
            cn.Execute("UPDATE HotelFacturaDetalle SET Activo = 0 WHERE Factura_id = @f AND Secuencia IN @s",
                new { f = o.Factura_id, s = plan.PagosOrigen }, tx);
        if (plan.Cierre != 0)
            Insertar(cn, tx, o.Factura_id, ReglasTransferencia.ServicioTransferencia, $"Transf. Hab.#{hasta} Fact.#{(long)d.Factura_id}",
                plan.Cierre, plan.Cierre, u);
        cn.Execute("""
            UPDATE HotelFactura SET Usuario = @u, Estatus = 1, Total = 0, SubTotal = 0, Itbis = 0, Descuento = 0, Propina = 0, Transferida = 1
            WHERE Factura_id = @f
            """, new { f = o.Factura_id, u }, tx);
        RepositorioCuentas.Recalcular(cn, tx, hasta);

        var saldo = cn.ExecuteScalar<decimal>("SELECT dbo.BalanceActualHabitacion(@f)", new { f = o.Factura_id }, tx);
        if (saldo != 0) throw new ErrorCatalogo($"El origen quedaria con saldo {saldo:N2}: no se hizo nada (avisar).");

        // check-out del origen con el procedimiento de Delphi (exige saldo 0; devuelve el numero de la factura cerrada)
        var p = new DynamicParameters();
        p.Add("@Factura_id", o.Factura_id);
        p.Add("@Transferida", dbType: DbType.Decimal, direction: ParameterDirection.Output, precision: 18, scale: 0);
        cn.Execute("Stp_CHECKOUT", p, tx, commandType: CommandType.StoredProcedure);
        var cerrada = p.Get<decimal?>("@Transferida");
        if (cn.ExecuteScalar<int>("SELECT COUNT(*) FROM HotelFactura WHERE Factura_id = @f", new { f = o.Factura_id }, tx) > 0)
            throw new ErrorCatalogo("No se pudo hacer el check-out del origen: no se hizo nada (avisar).");

        var avisos = new List<string>();
        if (Impuestos(o) != Impuestos(d))
            avisos.Add($"Los cargos pasaron sin impuestos: en la {hasta} se cobran con {Impuestos(d)} (su cliente); en la {desde} eran con {Impuestos(o)}.");
        if (!string.IsNullOrWhiteSpace(o.NCF))
            avisos.Add($"La cuenta de la {desde} tenia el e-NCF {o.NCF}, que queda sin usar: hay que anularlo ante la DGII.");
        tx.Commit();
        return new ResultadoTransferencia(desde, hasta, (long)d.Factura_id, (long?)cerrada, plan.Cargos, plan.Pagos, avisos);
    }

    private static void Insertar(IDbConnection cn, IDbTransaction tx, decimal factura, string servicio, string descripcion,
        decimal precio, decimal importe, DbString usuario) =>
        cn.Execute("""
            INSERT INTO HotelFacturaDetalle (Factura_id, Secuencia, Servicio_id, Descripcion, Cantidad, Precio, Importe, Activo, Usuario,
                                             fechaadicion, referencia, Impreso)
            SELECT @factura, ISNULL(MAX(Secuencia), 0) + 1, @servicio, @descripcion, 1, @precio, @importe, 1, @usuario, GETDATE(), '1', 1
            FROM HotelFacturaDetalle WHERE Factura_id = @factura
            """, new
        {
            factura, servicio = A(servicio, 4), descripcion = A(descripcion.Length > 50 ? descripcion[..50] : descripcion, 50),
            precio, importe, usuario,
        }, tx);
}
