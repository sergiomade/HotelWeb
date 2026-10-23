using System.Data;
using Dapper;
using HotelWeb.Api.Catalogos;
using HotelWeb.Api.Datos;
using HotelWeb.Api.Hospedaje;
using HotelWeb.Core.Ecf;
using HotelWeb.Core.Hospedaje;
using HotelWeb.Core.Identificacion;

namespace HotelWeb.Api.Ecf;

public sealed record PreparacionCheckout(int Habitacion, long Factura, string? Cliente, string? Huesped, string? Rnc, decimal SubTotal,
    decimal Itbis, decimal Propina, decimal Total, decimal Saldo, bool Transferida, string? NcfActual,
    IReadOnlyList<TipoComprobante> Tipos, int Sugerido, string? Impide);

public sealed record ResultadoCheckout(long Numero, string? Encf, decimal Total, bool Transferida);

/// <summary>Una factura cerrada en la lista, con su estado en la DGII (null si no lleva e-NCF).</summary>
/// <param name="WalkIn">Del walk-in (HotelFacturaWalking, numera aparte; Habitacion 0), o nota de un walk-in.</param>
/// <param name="Origen">Hospedaje, WalkIn, NotaHospedaje o NotaWalkIn.</param>
/// <param name="Modifica">En una nota de credito: el e-NCF de la factura que modifica.</param>
public sealed record FacturaCerrada(long Numero, DateTime Fecha, int Habitacion, string? Cliente, string? Huesped, string? Encf,
    decimal Total, bool Transferida, EstadoEcf? Dgii, bool WalkIn = false, string Origen = "Hospedaje", string? Modifica = null);

public sealed record LineaImpresa(DateTime? Fecha, string Descripcion, decimal Cantidad, decimal Precio, decimal Importe);

public sealed record FacturaImpresa(long Numero, string? Encf, string TipoNombre, DateTime Fecha, DateTime? Vencimiento,
    EmisorEcf Emisor, string? Cliente, string? Rnc, string? Huesped, int Habitacion, DateTime? Entrada, DateTime? Salida,
    IReadOnlyList<LineaImpresa> Items, IReadOnlyList<LineaImpresa> Otros, decimal SubTotal, decimal Itbis, decimal Propina,
    decimal Total, bool Transferida, TimbreEcf Timbre);

/// <summary>
/// Check-out con e-CF (etapa 5), como UfrmfacturaModificada.RzRapidFireButton3Click: balance 0, comprobante con las
/// validaciones de UFrmMntComprobanteFiscal ANTES de tomar el numero (y ademas el e-CF se revisa entero antes, para no
/// gastar un e-NCF en uno que la DGII rechazaria), e-NCF con STp_NumeroComprobanteFiscal_E (candado MIG_18), y
/// Stp_CHECKOUT, todo en una transaccion. El envio va despues (ServicioEcf), con la factura ya cerrada.
/// </summary>
public sealed class RepositorioCheckout
{
    private readonly Conexiones _conexiones;

    public RepositorioCheckout(Conexiones conexiones) => _conexiones = conexiones;

    private static DbString A(string? v, int largo) => new() { Value = v, IsAnsi = true, Length = largo };

    private sealed class FilaCuenta
    {
        public decimal Factura_id { get; set; }
        public string? Cliente { get; set; }
        public int? CodigoCompFiscal { get; set; }
        public string? Huesped { get; set; }
        public string? RncFactura { get; set; }
        public string? RncCliente { get; set; }
        public decimal? SubTotal { get; set; }
        public decimal? Itbis { get; set; }
        public decimal? Propina { get; set; }
        public bool? Transferida { get; set; }
        public string? NCF { get; set; }
    }

    private static FilaCuenta Cuenta(IDbConnection cn, IDbTransaction? tx, int habitacion) =>
        cn.QueryFirstOrDefault<FilaCuenta>($"""
            SELECT TOP 1 f.Factura_id, RTRIM(c.Nombre) AS Cliente, c.CodigoCompFiscal, RTRIM(f.Huesped) AS Huesped,
                   RTRIM(f.RNC) AS RncFactura, RTRIM(c.RNC) AS RncCliente, f.SubTotal, f.Itbis, f.Propina, f.Transferida, RTRIM(f.NCF) AS NCF
            FROM HotelFactura f {(tx is null ? "WITH (NOLOCK)" : "WITH (UPDLOCK)")}
            LEFT JOIN CxcCliente c WITH (NOLOCK) ON c.NumeroCliente = f.Cliente_id
            WHERE f.Habitacion_id = @habitacion ORDER BY f.Factura_id DESC
            """, new { habitacion }, tx) ?? throw new ErrorCatalogo($"La habitacion {habitacion} no tiene cuenta abierta.");

    /// <summary>El RNC para el comprobante: el de la cuenta si tiene, si no el del cliente (GetRncdelCliente de Delphi).</summary>
    private static string Rnc(FilaCuenta c) =>
        !IdentificacionRD.SinIdentificacion(c.RncFactura) ? c.RncFactura!.Trim() : (c.RncCliente ?? "").Trim();

    private static (decimal Saldo, bool HayCargos) Saldo(IDbConnection cn, IDbTransaction? tx, decimal factura) =>
        cn.QueryFirst<(decimal, bool)>("""
            SELECT dbo.BalanceActualHabitacion(@f),
                   CAST(CASE WHEN EXISTS (SELECT 1 FROM HotelFacturaDetalle WHERE Factura_id = @f AND Activo = 1 AND Importe > 0) THEN 1 ELSE 0 END AS bit)
            """, new { f = factura }, tx);

    public PreparacionCheckout Preparar(int habitacion)
    {
        using var cn = _conexiones.Hotel();
        var c = Cuenta(cn, null, habitacion);
        var (saldo, hay) = Saldo(cn, null, c.Factura_id);
        var rnc = Rnc(c);
        var total = (c.SubTotal ?? 0) + (c.Itbis ?? 0) + (c.Propina ?? 0);
        return new PreparacionCheckout(habitacion, (long)c.Factura_id, c.Cliente, c.Huesped, rnc, c.SubTotal ?? 0, c.Itbis ?? 0,
            c.Propina ?? 0, total, saldo, c.Transferida == true, EsEcf(c.NCF) ? c.NCF : null, ReglasCheckout.Permitidos(rnc),
            ReglasCheckout.Sugerido(c.CodigoCompFiscal, rnc), ReglasCheckout.PuedeCerrar(saldo, total, hay, c.Transferida == true));
    }

    private static bool EsEcf(string? ncf) => ncf is { Length: 13 } n && n[0] == 'E';

    public ResultadoCheckout Hacer(int habitacion, int tipo, string? rnc, string usuario)
    {
        using var cn = _conexiones.Hotel();
        cn.Open();
        using var tx = cn.BeginTransaction(IsolationLevel.Serializable);
        var c = Cuenta(cn, tx, habitacion);
        // como Salvar(Self) de Delphi: el encabezado con los totales de las lineas antes de cerrar
        RepositorioCuentas.Recalcular(cn, tx, habitacion);
        c = Cuenta(cn, tx, habitacion);
        var transferida = c.Transferida == true;
        var total = (c.SubTotal ?? 0) + (c.Itbis ?? 0) + (c.Propina ?? 0);
        var (saldo, hay) = Saldo(cn, tx, c.Factura_id);
        if (ReglasCheckout.PuedeCerrar(saldo, total, hay, transferida) is { } impide) throw new ErrorCatalogo(impide);

        string? encf = null;
        if (!transferida)
        {
            encf = EsEcf(c.NCF) ? c.NCF : null;   // un e-NCF viejo (de antes) se usa
            var codigo = encf is null ? ReglasCheckout.Tipos.FirstOrDefault(t => t.Id == tipo)?.Codigo : encf[..3];
            if (encf is null && ReglasCheckout.Validar(tipo, rnc, c.Cliente, total, c.Itbis ?? 0) is { } motivo) throw new ErrorCatalogo(motivo);
            var rncFinal = IdentificacionRD.SinIdentificacion(rnc) ? "000000000" : rnc!.Trim();
            if (rncFinal.Length > 11) throw new ErrorCatalogo("La identificacion tiene mas de 11 caracteres (HotelFactura.RNC).");

            // el e-CF entero se revisa ANTES de gastar el numero (con un numero de muestra del tipo)
            var lineas = cn.Query<(string Descripcion, int? Cantidad, decimal? Precio, decimal? Importe, bool? Activo)>("""
                SELECT RTRIM(Descripcion), Cantidad, Precio, Importe, Activo FROM HotelFacturaDetalle WHERE Factura_id = @f ORDER BY Secuencia
                """, new { f = c.Factura_id }, tx);
            var muestra = new FacturaEcf(encf ?? $"{codigo}0000000001", (long)c.Factura_id, DateTime.Today, rncFinal, c.Cliente,
                c.SubTotal ?? 0, c.Itbis ?? 0, c.Propina ?? 0, false,
                EcfHotel.Items(lineas.Select(l => (l.Descripcion ?? "", (decimal)(l.Cantidad ?? 0), l.Precio ?? 0, l.Importe ?? 0, l.Activo == true))));
            if (EcfHotel.Validar(muestra) is { } malo) throw new ErrorCatalogo(malo);

            if (encf is null)
            {
                encf = cn.QueryFirstOrDefault<string?>("STp_NumeroComprobanteFiscal_E", new { NumeroCliente = 1m, NotasCRDB = tipo }, tx,
                    commandType: CommandType.StoredProcedure)?.Trim();
                if (!EsEcf(encf)) throw new ErrorCatalogo("No se pudo obtener el e-NCF del tipo de comprobante elegido.");
            }
            cn.Execute("UPDATE HotelFactura SET NCF = @encf, RNC = @rnc WHERE Factura_id = @f",
                new { encf = A(encf, 19), rnc = A(rncFinal, 11), f = c.Factura_id }, tx);
        }

        var p = new DynamicParameters();
        p.Add("@Factura_id", c.Factura_id);
        p.Add("@Transferida", dbType: DbType.Decimal, direction: ParameterDirection.Output, precision: 18, scale: 0);
        cn.Execute("Stp_CHECKOUT", p, tx, commandType: CommandType.StoredProcedure);
        var numero = p.Get<decimal?>("@Transferida");
        if (numero is null || numero == c.Factura_id
            || cn.ExecuteScalar<int>("SELECT COUNT(*) FROM HotelFactura WHERE Factura_id = @f", new { f = c.Factura_id }, tx) > 0)
            throw new ErrorCatalogo("No se pudo realizar el check-out: verifique el balance de la factura. No se hizo nada.");
        tx.Commit();
        return new ResultadoCheckout((long)numero.Value, encf, total, transferida);
    }

    /// <summary>
    /// Las facturas cerradas (HotelTmpFactura) de un rango de fechas, las mas nuevas primero, con su estado en la DGII.
    /// Texto: numero de habitacion, de factura, e-NCF o parte del nombre.
    /// </summary>
    public async Task<IReadOnlyList<FacturaCerrada>> Facturas(DateTime desde, DateTime hasta, string? texto, ServicioEcf ecf, bool cafeteria = false)
    {
        if (hasta.Date < desde.Date) throw new ErrorCatalogo("La fecha final es antes de la inicial.");
        if ((hasta.Date - desde.Date).TotalDays > 62) throw new ErrorCatalogo("Escoja a lo sumo dos meses.");
        var t = string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
        List<(FacturaCerrada F, FacturaEcf E)> filas;
        using (var cn = _conexiones.Hotel())
        {
            if (cafeteria)
            {
                // la cafeteria va en su propia lista: sus facturas (FaTmpFactura) y sus notas (faFacturaNC)
                filas = Cafeteria.RepositorioCafeteria.Lista(cn, desde, hasta, t);
                filas.AddRange(RepositorioNotas.ListaCafeteria(cn, desde, hasta, t));
            }
            else
            {
                filas = cn.Query<(decimal Numero, DateTime Fecha, decimal Hab, string? Cliente, string? Huesped, string? Ncf, string? Rnc,
                        decimal? SubTotal, decimal? Itbis, decimal? Propina, bool? Transferida)>("""
                    SELECT TOP 500 f.Factura_id, f.Fecha, f.Habitacion_id, RTRIM(c.Nombre), RTRIM(f.Huesped), RTRIM(f.NCF), RTRIM(f.RNC),
                           f.SubTotal, f.Itbis, f.Propina, f.Transferida
                    FROM HotelTmpFactura f WITH (NOLOCK) LEFT JOIN CxcCliente c WITH (NOLOCK) ON c.NumeroCliente = f.Cliente_id
                    WHERE f.Fecha >= @desde AND f.Fecha < @hasta
                      AND (@t IS NULL OR RTRIM(f.NCF) = @t OR CAST(f.Habitacion_id AS varchar(20)) = @t OR CAST(f.Factura_id AS varchar(20)) = @t
                           OR c.Nombre LIKE '%' + @t + '%' OR f.Huesped LIKE '%' + @t + '%')
                    ORDER BY f.Factura_id DESC
                    """, new { desde = desde.Date, hasta = hasta.Date.AddDays(1), t = A(t, 60) })
                    .Select(x =>
                    {
                        var subtotal = x.SubTotal ?? 0;
                        var e = new FacturaEcf(x.Ncf ?? "", (long)x.Numero, x.Fecha.Date, x.Rnc, x.Cliente, subtotal, x.Itbis ?? 0, x.Propina ?? 0,
                            false, Array.Empty<ItemEcf>());
                        return (new FacturaCerrada((long)x.Numero, x.Fecha, (int)x.Hab, x.Cliente, x.Huesped, x.Ncf, e.Total,
                            x.Transferida == true, null), e);
                    }).ToList();
                // los walk-in del mismo rango (numeran aparte)
                filas.AddRange(RepositorioWalkIn.Lista(cn, desde, hasta, t));
                filas.AddRange(RepositorioNotas.Lista(cn, desde, hasta, t));
            }
        }
        var estados = await ecf.Estados(filas.Where(x => !x.F.Transferida).Select(x => x.E).ToList());
        return filas.Select(x => x.F with { Dgii = x.F.Encf is { } e && estados.TryGetValue(e, out var s) ? s : null })
            .OrderByDescending(x => x.Fecha.Date).ThenByDescending(x => x.WalkIn ? 0 : 1).ThenByDescending(x => x.Numero).ToList();
    }

    /// <summary>La factura cerrada para imprimir (carta, como Delphi) con el QR.</summary>
    public async Task<FacturaImpresa> Imprimible(long numero, ServicioEcf ecf)
    {
        FacturaEcf f;
        EmisorEcf emisor;
        DateTime venc;
        bool transferida;
        (decimal Hab, string? Huesped, DateTime? Entrada, DateTime? Salida) d;
        List<LineaImpresa> otros;
        using (var cn = _conexiones.Hotel())
        {
            f = ServicioEcf.Leer(cn, null, numero, out transferida) ?? throw new ErrorCatalogo($"No existe la factura {numero}.");
            emisor = ServicioEcf.Emisor(cn);
            venc = f.Encf.StartsWith('E') ? ServicioEcf.FechaVencimiento(cn, f.Encf) : default;
            d = cn.QueryFirst<(decimal, string?, DateTime?, DateTime?)>("""
                SELECT f.Habitacion_id, COALESCE(NULLIF(RTRIM(f.Huesped), ''), RTRIM(r.Huesped)), r.FechaEntrada, r.FechaSalida
                FROM HotelTmpFactura f LEFT JOIN HotelHabitacionReserva r ON r.HabitacionReserva_id = f.HabitacionReserva_id
                WHERE f.Factura_id = @numero
                """, new { numero });
            // lo que no va en el e-CF pero se cobro o se pago: PAY-OUT, pagos y adelantos
            otros = cn.Query<(DateTime? Fecha, string Descripcion, int? Cantidad, decimal? Precio, decimal? Importe)>("""
                SELECT fechaadicion, RTRIM(Descripcion), Cantidad, Precio, Importe FROM HotelTmpFacturaDetalle
                WHERE Factura_id = @numero AND Activo = 1 AND (Importe < 0 OR LTRIM(Descripcion) LIKE 'PAY-OUT%') ORDER BY Secuencia
                """, new { numero }).Select(x => new LineaImpresa(x.Fecha, x.Descripcion ?? "", x.Cantidad ?? 1, x.Precio ?? 0, x.Importe ?? 0)).ToList();
        }
        var tipo = ReglasCheckout.Tipos.FirstOrDefault(t => f.Encf.StartsWith(t.Codigo));
        var timbre = f.Encf.StartsWith('E') && !transferida ? await ecf.Timbre(f, emisor.Rnc) : new TimbreEcf(null, null, null);
        return new FacturaImpresa(numero, f.Encf == "" ? null : f.Encf,
            // el vencimiento de la secuencia se imprime en todos los tipos (pedido del usuario); en el XML de la E32 no va
            tipo is null ? "Factura" : $"Factura de {tipo.Nombre.ToLowerInvariant()} electronica", f.Fecha, venc == default ? null : venc,
            emisor, f.NombreCliente, IdentificacionRD.SinIdentificacion(f.Rnc) ? null : f.Rnc, d.Huesped, (int)d.Hab, d.Entrada, d.Salida,
            f.Items.Select(i => new LineaImpresa(null, i.Descripcion, i.Cantidad, i.Precio, i.Importe)).ToList(), otros,
            f.SubTotal, f.Itbis, f.Propina, f.Total, transferida, timbre);
    }
}
