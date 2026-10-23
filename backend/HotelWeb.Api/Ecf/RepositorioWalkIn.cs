using System.Data;
using Dapper;
using HotelWeb.Api.Catalogos;
using HotelWeb.Api.Datos;
using HotelWeb.Core.Ecf;
using HotelWeb.Core.Hospedaje;

namespace HotelWeb.Api.Ecf;

public sealed record ResultadoWalkIn(long Numero, string Encf, decimal SubTotal, decimal Itbis, decimal Propina, decimal Total,
    decimal Recibido, decimal Devuelta, long Recibo);

public sealed record PagoImpreso(string FormaPago, string Descripcion, decimal Monto);

public sealed record TicketWalkIn(long Numero, string? Encf, string TipoNombre, DateTime Fecha, DateTime? Vencimiento, EmisorEcf Emisor,
    string? Nombre, string? Identificacion, IReadOnlyList<LineaImpresa> Items, decimal SubTotal, decimal Itbis, decimal Propina,
    decimal Total, IReadOnlyList<PagoImpreso> Pagos, string? Concepto, string? Usuario, TimbreEcf Timbre);

/// <summary>
/// Walk-in (etapa 6), como UfrmWalking.Salvar de Delphi, en una transaccion: numero (mayor + 1), e-NCF
/// (STp_NumeroComprobanteFiscal_E), HotelFacturaWalking + detalle y el recibo (HotelReciboIngreso con FacturaWalking_id,
/// Habitacion 1, Cliente 1, Posteado 0 para el cuadre). Se graba lo aplicado: la devuelta queda en el concepto del recibo.
/// </summary>
public sealed class RepositorioWalkIn
{
    private readonly Conexiones _conexiones;

    public RepositorioWalkIn(Conexiones conexiones) => _conexiones = conexiones;

    private static DbString A(string? v, int largo) => new() { Value = v, IsAnsi = true, Length = largo };
    private static DbString U(string u) => A(u.Length > 10 ? u[..10] : u, 10);

    public ResultadoWalkIn Crear(DatosWalkIn datos, string usuario)
    {
        var d = ReglasWalkIn.Normalizar(datos);
        if (ReglasWalkIn.Validar(d) is { } motivo) throw new ErrorCatalogo(motivo);
        var t = ReglasWalkIn.Totales(d.Lineas, d.Tipo, d.ConPropina);
        var pagos = ReglasWalkIn.Pagos(d.Pagos, t.Total) ?? throw new ErrorCatalogo("El pago es menor que el total del walk-in.");

        using var cn = _conexiones.Hotel();
        cn.Open();
        using var tx = cn.BeginTransaction(IsolationLevel.Serializable);
        foreach (var s in d.Lineas.Select(l => l.Servicio).Distinct())
            if (cn.ExecuteScalar<int>("SELECT COUNT(*) FROM HotelServicioAdicional WHERE LTRIM(RTRIM(Servicio_id)) = @s",
                    new { s = s.ToString() }, tx) == 0)
                throw new ErrorCatalogo($"No existe el servicio {s}.");
        foreach (var f in pagos.Aplicados.Select(p => p.FormaPago).Distinct())
            if (cn.ExecuteScalar<int>("SELECT COUNT(*) FROM ConMonedas WHERE Monedas_id = @f", new { f }, tx) == 0)
                throw new ErrorCatalogo($"No existe la forma de pago {f}.");

        // el e-CF entero se revisa ANTES de gastar el numero (con un numero de muestra del tipo)
        var codigo = ReglasCheckout.Tipos.First(x => x.Id == d.Tipo).Codigo;
        if (EcfHotel.Validar(ReglasWalkIn.Ecf($"{codigo}0000000001", 1, DateTime.Today, d, t, false)) is { } malo) throw new ErrorCatalogo(malo);

        var numero = cn.ExecuteScalar<long>(
            "SELECT ISNULL(MAX(FacturaWalking_id), 0) + 1 FROM HotelFacturaWalking WITH (UPDLOCK, HOLDLOCK)", transaction: tx);
        var encf = cn.QueryFirstOrDefault<string?>("STp_NumeroComprobanteFiscal_E", new { NumeroCliente = 1m, NotasCRDB = d.Tipo }, tx,
            commandType: CommandType.StoredProcedure)?.Trim();
        if (encf is not { Length: 13 } || encf[0] != 'E') throw new ErrorCatalogo("No se pudo obtener el e-NCF del tipo de comprobante elegido.");
        var u = U(usuario);

        // como QWalkingNewRecord + Salvar: fecha de hoy, RNC y cedula en ceros si no hay
        cn.Execute("""
            INSERT INTO HotelFacturaWalking (FacturaWalking_id, Fecha, NCF, Nombre, Direccion, Telefono, Cedula, SubTotal, Itbis, Descuento,
                   Propina, Total, RNC, ConPropina, usuario, fechaadicion)
            VALUES (@numero, CAST(GETDATE() AS date), @encf, @nombre, @direccion, @telefono, @cedula, @sub, @itbis, 0, @propina, @total,
                    @rnc, @conPropina, @u, GETDATE())
            """, new
        {
            numero, encf = A(encf, 19), nombre = A(d.Nombre, 50), direccion = A(d.Direccion, 50), telefono = A(d.Telefono, 13),
            cedula = A(d.Cedula ?? "00000000000", 13), sub = t.SubTotal, itbis = t.Itbis, propina = t.Propina, total = t.Total,
            rnc = A(d.Rnc ?? "000000000", 11), conPropina = d.ConPropina, u,
        }, tx);
        var sec = 0;
        foreach (var l in d.Lineas)
            cn.Execute("""
                INSERT INTO HotelFacturaWalkingDetalle (FacturaWalking_id, Secuencia, Servicio_id, Referencia, Descripcion, Cantidad, Precio,
                       Importe, Activo, Usuario, FechaAdicion)
                VALUES (@numero, @sec, @servicio, @referencia, @descripcion, @cantidad, @precio, @importe, 1, @u, GETDATE())
                """, new
            {
                numero, sec = ++sec, servicio = l.Servicio, referencia = A(l.Servicio.ToString(), 10), descripcion = A(l.Descripcion, 50),
                cantidad = l.Cantidad, precio = l.Precio, importe = l.Importe, u,
            }, tx);

        // el recibo, como ReciboNewRecord de Delphi (Habitacion 1, Cliente 1, Posteado 0: lo toma el cuadre de caja)
        var recibo = cn.ExecuteScalar<long>(
            "SELECT ISNULL(MAX(NumeroRecibo), 0) + 1 FROM HotelReciboIngreso WITH (UPDLOCK, HOLDLOCK)", transaction: tx);
        var concepto = "Pago Factura Walking " + (pagos.Devuelta > 0 ? $"(recibido {pagos.Recibido:N2}, devuelta {pagos.Devuelta:N2})" : "");
        cn.Execute("""
            INSERT INTO HotelReciboIngreso (NumeroRecibo, Fecha, Habitacion, Habitacion_id, Cliente_id, EntradaRec_id, Factura_id,
                   FacturaWalking_id, Reserva_id, Nombre, Concepto, Monto, Estatus, Usuario, FechaAdicion, Posteado)
            VALUES (@recibo, CAST(GETDATE() AS date), 1, 0, 1, 0, 0, @numero, NULL, @nombre, @concepto, @total, 'Activo', @u, GETDATE(), 0)
            """, new { recibo, numero, nombre = A(d.Nombre, 60), concepto = A(concepto.Trim(), -1), total = t.Total, u }, tx);
        foreach (var p in pagos.Aplicados)
            cn.Execute("""
                INSERT INTO HotelReciboDetalle (NumeroRecibo, Monedas_id, Descripcion, Monto, MontoUs, NoCheque, Fechacheque, BancoTc)
                VALUES (@recibo, @forma, @descripcion, @monto, @us, @cheque, @fechaCheque, @banco)
                """, new
            {
                recibo, forma = p.FormaPago, descripcion = A(p.Descripcion, 40), monto = p.Monto,
                us = p.FormaPago == ReglasRecibo.Dolares ? p.MontoUs : null, cheque = A(p.NoCheque, 15), fechaCheque = p.FechaCheque,
                banco = A(p.Banco, 50),
            }, tx);
        tx.Commit();
        return new ResultadoWalkIn(numero, encf, t.SubTotal, t.Itbis, t.Propina, t.Total, pagos.Recibido, pagos.Devuelta, recibo);
    }

    /// <summary>El ticket del walk-in (POS 80 mm, como ppRepPOs de Delphi) con el QR.</summary>
    public async Task<TicketWalkIn> Ticket(long numero, ServicioEcf ecf)
    {
        FacturaEcf f;
        EmisorEcf emisor;
        DateTime? venc;
        List<PagoImpreso> pagos;
        (string? Concepto, string? Usuario) extra;
        using (var cn = _conexiones.Hotel())
        {
            f = ServicioEcf.LeerWalkIn(cn, null, numero) ?? throw new ErrorCatalogo($"No existe el walk-in {numero}.");
            emisor = ServicioEcf.Emisor(cn);
            venc = f.Encf.StartsWith('E') ? ServicioEcf.FechaVencimiento(cn, f.Encf) : null;
            pagos = cn.Query<(string? Forma, string? Descripcion, decimal? Monto)>("""
                SELECT RTRIM(m.NombreMoneda), RTRIM(d.Descripcion), d.Monto
                FROM HotelReciboIngreso r JOIN HotelReciboDetalle d ON d.NumeroRecibo = r.NumeroRecibo
                LEFT JOIN ConMonedas m ON m.Monedas_id = d.Monedas_id
                WHERE r.FacturaWalking_id = @numero AND RTRIM(r.Estatus) = 'Activo' ORDER BY d.NumeroRecibo
                """, new { numero }).Select(x => new PagoImpreso(x.Forma ?? "", x.Descripcion ?? "", x.Monto ?? 0)).ToList();
            extra = cn.QueryFirstOrDefault<(string?, string?)>("""
                SELECT TOP 1 CAST(r.Concepto AS varchar(300)), RTRIM(w.usuario) FROM HotelFacturaWalking w
                LEFT JOIN HotelReciboIngreso r ON r.FacturaWalking_id = w.FacturaWalking_id WHERE w.FacturaWalking_id = @numero
                """, new { numero });
        }
        var tipo = ReglasCheckout.Tipos.FirstOrDefault(t => f.Encf.StartsWith(t.Codigo));
        var timbre = f.Encf.StartsWith('E') ? await ecf.Timbre(f, emisor.Rnc) : new TimbreEcf(null, null, null);
        return new TicketWalkIn(numero, f.Encf == "" ? null : f.Encf, tipo is null ? "Factura" : $"Factura de {tipo.Nombre.ToLowerInvariant()} electronica",
            f.Fecha, venc, emisor, f.NombreCliente, string.IsNullOrWhiteSpace(f.Rnc) || f.Rnc.All(c => c == '0') ? null : f.Rnc,
            f.Items.Select(i => new LineaImpresa(null, i.Descripcion, i.Cantidad, i.Precio, i.Importe)).ToList(),
            f.SubTotal, f.Itbis, f.Propina, f.Total, pagos, extra.Concepto?.Trim(), extra.Usuario, timbre);
    }

    /// <summary>Los walk-in de un rango de fechas para la lista de facturas.</summary>
    internal static List<(FacturaCerrada F, FacturaEcf E)> Lista(IDbConnection cn, DateTime desde, DateTime hasta, string? t) =>
        cn.Query<(decimal Numero, DateTime Fecha, string? Nombre, string? Ncf, string? Rnc, decimal? SubTotal, decimal? Itbis, decimal? Propina)>("""
            SELECT TOP 500 w.FacturaWalking_id, w.Fecha, RTRIM(w.Nombre), RTRIM(w.NCF),
                   CASE WHEN REPLACE(LTRIM(RTRIM(ISNULL(w.RNC, ''))), '0', '') = '' THEN RTRIM(w.Cedula) ELSE RTRIM(w.RNC) END,
                   w.SubTotal, w.Itbis, w.Propina
            FROM HotelFacturaWalking w WITH (NOLOCK)
            WHERE w.Fecha >= @desde AND w.Fecha < @hasta
              AND (@t IS NULL OR RTRIM(w.NCF) = @t OR CAST(w.FacturaWalking_id AS varchar(20)) = @t OR w.Nombre LIKE '%' + @t + '%')
            ORDER BY w.FacturaWalking_id DESC
            """, new { desde = desde.Date, hasta = hasta.Date.AddDays(1), t = A(t, 60) })
            .Select(x =>
            {
                var e = new FacturaEcf(x.Ncf ?? "", (long)x.Numero, x.Fecha.Date, x.Rnc, x.Nombre, x.SubTotal ?? 0, x.Itbis ?? 0, x.Propina ?? 0,
                    false, Array.Empty<ItemEcf>());
                return (new FacturaCerrada((long)x.Numero, x.Fecha, 0, x.Nombre, null, x.Ncf, e.Total, false, null, true, "WalkIn"), e);
            }).ToList();
}
