using System.Data;
using System.Globalization;
using System.Text.Json;
using Dapper;
using HotelWeb.Api.Catalogos;
using HotelWeb.Api.Datos;
using HotelWeb.Core.Ecf;

namespace HotelWeb.Api.Ecf;

/// <summary>Como quedo un e-CF en la DGII.</summary>
/// <param name="Estado">Aceptado, Aceptado Condicional, Rechazado, En proceso, Sin enviar...</param>
public sealed record EstadoEcf(string Encf, long Factura, string Estado, string? Mensaje, bool Aceptado);

/// <summary>Los datos del QR y de la representacion impresa (DocumentosXml, el registro del e-CF completo).</summary>
public sealed record TimbreEcf(string? CodigoSeguridad, string? FechaHoraFirma, string? Url);

/// <summary>
/// De donde sale el documento: el check-out (HotelTmpFactura), el walk-in (HotelFacturaWalking) y sus notas de credito
/// (HotelFacturaNC, HotelFacturaWalkingNC), y la cafeteria (FaTmpFactura). Cada uno numera aparte.
/// </summary>
public enum OrigenFactura { Hospedaje, WalkIn, NotaHospedaje, NotaWalkIn, Cafeteria, NotaCafeteria, GastoMenor, CompraInformal }

/// <summary>
/// Arma el e-CF de una factura cerrada (HotelTmpFactura), lo guarda en FacturaXml (sp_GuardarFacturaXml) y lo envia por
/// la API WebEncf (Parametro.UrlApiEcf; la misma que usa Delphi con EcfPorHttp): ECF con EnviarDesdeHotel; la E32 de
/// menos de 250 mil se firma completa (FirmarDesdeHotel, para el codigo de seguridad) y se envia el resumen (RFCE).
/// </summary>
public sealed class ServicioEcf
{
    private readonly Conexiones _conexiones;
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;
    private readonly ILogger<ServicioEcf> _log;

    public ServicioEcf(Conexiones conexiones, IHttpClientFactory http, IConfiguration config, ILogger<ServicioEcf> log)
    {
        _conexiones = conexiones;
        _http = http;
        _config = config;
        _log = log;
    }

    private sealed class FilaFactura
    {
        public decimal Factura_id { get; set; }
        public string? NCF { get; set; }
        public DateTime Fecha { get; set; }
        public string? RNC { get; set; }
        public string? NombreCliente { get; set; }
        public decimal? SubTotal { get; set; }
        public decimal? Itbis { get; set; }
        public decimal? Propina { get; set; }
        public bool Credito { get; set; }
        public bool? Transferida { get; set; }
    }

    private sealed class FilaParametro
    {
        public string? RncEmpresa { get; set; }
        public string? NombreEmpresa { get; set; }
        public string? DireccionEmpresa { get; set; }
        public string? TelefonoEmpresa { get; set; }
        public string? UrlApiEcf { get; set; }
        public string? ClaveApiEcf { get; set; }
    }

    /// <summary>
    /// La factura cerrada con lo que lleva el e-CF. Credito (TipoPago 2) si algun pago de la estadia fue a credito
    /// (Monedas_id 6), con los recibos de la reserva + habitacion + clave, como CrearFacturaXml.
    /// </summary>
    internal static FacturaEcf? Leer(IDbConnection cn, IDbTransaction? tx, OrigenFactura origen, long numero, out bool transferida)
    {
        if (origen == OrigenFactura.Hospedaje) return Leer(cn, tx, numero, out transferida);
        transferida = false;
        if (origen == OrigenFactura.WalkIn) return LeerWalkIn(cn, tx, numero);
        if (origen == OrigenFactura.Cafeteria) return LeerCafeteria(cn, tx, numero);
        if (origen == OrigenFactura.GastoMenor) return LeerGasto(cn, tx, numero) is { } g ? GastoMenorEcf.ComoFactura(g) : null;
        if (origen == OrigenFactura.CompraInformal) return LeerCompra(cn, tx, numero) is { } co ? CompraInformalEcf.ComoFactura(co) : null;
        return LeerNota(cn, tx, origen, numero) is { } n ? ComoFactura(n.Nota, n.Original) : null;
    }

    /// <summary>
    /// La factura cobrada de la cafeteria (FaTmpFactura), como CrearFacturaXml de Comunes\uCreateXml.pas: items de
    /// cantidad x precio, credito (TipoPago 2) si algun pago fue a credito (forma 3) y el TOTAL grabado (no la suma).
    /// </summary>
    /// <summary>El gasto menor (HotelGastoMenor, MIG_14).</summary>
    internal static GastoMenor? LeerGasto(IDbConnection cn, IDbTransaction? tx, long numero) =>
        cn.Query<(int Id, string Ncf, DateTime Fecha, string Descripcion, byte Bs, decimal Cantidad, decimal Precio, decimal Total, string FormaPago,
                string TipoGasto, string Beneficiario, string? Cedula, string Detalle, string? Usuario)>("""
            SELECT Gasto_id, RTRIM(NCF), Fecha, RTRIM(Descripcion), TipoBienServicio, Cantidad, Precio, Total, FormaPago, TipoGasto606,
                   RTRIM(Beneficiario), RTRIM(Cedula), RTRIM(Detalle), RTRIM(Usuario)
            FROM HotelGastoMenor WHERE Gasto_id = @numero
            """, new { numero = (int)numero }, tx)
            .Select(x => new GastoMenor(x.Ncf, x.Id, x.Fecha.Date, x.Descripcion, x.Bs, x.Cantidad, x.Precio, x.Total, x.FormaPago, x.TipoGasto,
                x.Beneficiario, x.Cedula, x.Detalle, x.Usuario)).FirstOrDefault();

    /// <summary>La compra a un proveedor informal con sus lineas (HotelCompraInformal, MIG_16).</summary>
    internal static CompraInformal? LeerCompra(IDbConnection cn, IDbTransaction? tx, long numero)
    {
        var c = cn.QueryFirstOrDefault<FilaCompra>("SELECT * FROM HotelCompraInformal WHERE Compra_id = @numero", new { numero = (int)numero }, tx);
        if (c is null) return null;
        var lineas = cn.Query<(string Descripcion, byte Bs, decimal Cantidad, decimal Precio, bool Gravado, decimal Monto, decimal Itbis, decimal ItbisRet,
                decimal IsrRet)>("""
            SELECT RTRIM(Descripcion), TipoBienServicio, Cantidad, Precio, Gravado, Monto, Itbis, ItbisRetenido, IsrRetenido
            FROM HotelCompraInformalLinea WHERE Compra_id = @numero ORDER BY NumeroLinea
            """, new { numero = (int)numero }, tx)
            .Select(l => new LineaCompra(l.Descripcion, l.Bs, l.Cantidad, l.Precio, l.Gravado, l.Monto, l.Itbis, l.ItbisRet, l.IsrRet)).ToList();
        return new CompraInformal(c.NCF.Trim(), c.Compra_id, c.Fecha.Date, c.FechaPago.Date, c.Cedula.Trim(), c.Proveedor.Trim(), c.Direccion?.Trim(),
            c.TipoRetencionIsr.Trim(), c.FormaPago.Trim(), c.TipoGasto606.Trim(), c.Observacion?.Trim(), lineas, c.MontoGravado, c.MontoExento, c.Itbis,
            c.Total, c.ItbisRetenido, c.IsrRetenido, c.NetoPagar, c.Usuario?.Trim());
    }

    private sealed class FilaCompra
    {
        public int Compra_id { get; set; }
        public string NCF { get; set; } = "";
        public DateTime Fecha { get; set; }
        public DateTime FechaPago { get; set; }
        public string Cedula { get; set; } = "";
        public string Proveedor { get; set; } = "";
        public string? Direccion { get; set; }
        public string TipoRetencionIsr { get; set; } = "";
        public string FormaPago { get; set; } = "";
        public string TipoGasto606 { get; set; } = "";
        public string? Observacion { get; set; }
        public decimal MontoGravado { get; set; }
        public decimal MontoExento { get; set; }
        public decimal Itbis { get; set; }
        public decimal Total { get; set; }
        public decimal ItbisRetenido { get; set; }
        public decimal IsrRetenido { get; set; }
        public decimal NetoPagar { get; set; }
        public string? Usuario { get; set; }
    }

    internal static FacturaEcf? LeerCafeteria(IDbConnection cn, IDbTransaction? tx, long numero)
    {
        var f = cn.QueryFirstOrDefault<(int Factura, string? Ncf, DateTime Fecha, string? Rnc, string? Nombre, decimal SubTotal, decimal Itbis,
                decimal Propina, decimal Total, bool Credito)>("""
            SELECT f.InvFactura, RTRIM(f.NCF), f.Fecha, RTRIM(f.RncCliente), RTRIM(f.NombreCliente), f.Subtotal, f.Itbis, f.Propina, f.Total,
                   CAST(CASE WHEN EXISTS (SELECT 1 FROM faFacturaPago p WHERE p.InvFactura = f.InvFactura AND p.FormaPago_id = 3) THEN 1 ELSE 0 END AS bit)
            FROM FaTmpFactura f WHERE f.InvFactura = @numero
            """, new { numero = (int)numero }, tx);
        if (f.Factura == 0) return null;
        var lineas = cn.Query<(string Descripcion, decimal Cantidad, decimal Precio)>(
            "SELECT Descripcion, Cantidad, Precio FROM FaTmpFacturaDetalle WHERE InvFactura = @numero ORDER BY Secuencia",
            new { numero = (int)numero }, tx);
        return new FacturaEcf(f.Ncf ?? "", f.Factura, f.Fecha.Date, f.Rnc, f.Nombre, f.SubTotal, f.Itbis, f.Propina, f.Credito,
            EcfHotel.Items(lineas.Select(l => (l.Descripcion ?? "", l.Cantidad, l.Precio,
                Math.Round(l.Cantidad * l.Precio, 2, MidpointRounding.AwayFromZero), true))), f.Total);
    }

    private sealed class FilaNota
    {
        public decimal Devolucion_id { get; set; }
        public decimal Original_id { get; set; }
        public DateTime Fecha { get; set; }
        public string? NCF { get; set; }
        public decimal? Total { get; set; }
        public int? CodigoModificacion { get; set; }
        public string? RazonModificacion { get; set; }
    }

    /// <summary>La nota de credito y su factura original, como CrearNotaCreditoXml / CrearNotaCreditoXmlWalking.</summary>
    internal static (NotaCredito Nota, OriginalNota Original, long OriginalId)? LeerNota(IDbConnection cn, IDbTransaction? tx, OrigenFactura origen, long devolucion)
    {
        if (origen == OrigenFactura.NotaCafeteria)
        {
            // como CrearNotaCreditoXml de Comunes\uCreateXml.pas: faFacturaNC sobre FaTmpFactura
            var c = cn.QueryFirstOrDefault<FilaNota>("""
                SELECT Devolucion_id, InvFactura AS Original_id, Fecha, RTRIM(NCF) AS NCF, Total, CAST(CodigoModificacion AS int) AS CodigoModificacion,
                       RTRIM(RazonModificacion) AS RazonModificacion
                FROM faFacturaNC WHERE Devolucion_id = @devolucion
                """, new { devolucion = (decimal)devolucion }, tx);
            if (c is null) return null;
            var oc = LeerCafeteria(cn, tx, (long)c.Original_id)
                     ?? throw new ErrorCatalogo($"No se encontro la factura original de la nota {devolucion}.");
            return (new NotaCredito(c.NCF ?? "", (long)c.Devolucion_id, c.Fecha.Date, c.Total ?? 0, c.CodigoModificacion ?? 0, c.RazonModificacion),
                new OriginalNota(oc.Encf, oc.Fecha, oc.Rnc, oc.NombreCliente, oc.SubTotal, oc.Itbis, oc.Propina, oc.TotalCobrado), (long)c.Original_id);
        }
        var walk = origen == OrigenFactura.NotaWalkIn;
        var n = cn.QueryFirstOrDefault<FilaNota>(walk
            ? """
              SELECT Devolucion_id, FacturaWalking_id AS Original_id, Fecha, RTRIM(NCF) AS NCF, Total, CodigoModificacion, RTRIM(RazonModificacion) AS RazonModificacion
              FROM HotelFacturaWalkingNC WHERE Devolucion_id = @devolucion
              """
            : """
              SELECT Devolucion_id, Factura_id AS Original_id, Fecha, RTRIM(NCF) AS NCF, Total, CodigoModificacion, RTRIM(RazonModificacion) AS RazonModificacion
              FROM HotelFacturaNC WHERE Devolucion_id = @devolucion
              """, new { devolucion }, tx);
        if (n is null) return null;
        var o = (walk ? LeerWalkIn(cn, tx, (long)n.Original_id) : Leer(cn, tx, (long)n.Original_id, out _))
                ?? throw new ErrorCatalogo($"No se encontro la factura original de la nota {devolucion}.");
        return (new NotaCredito(n.NCF ?? "", (long)n.Devolucion_id, n.Fecha.Date, n.Total ?? 0, n.CodigoModificacion ?? 0, n.RazonModificacion),
            new OriginalNota(o.Encf, o.Fecha, o.Rnc, o.NombreCliente, o.SubTotal, o.Itbis, o.Propina), (long)n.Original_id);
    }

    /// <summary>La nota vista como un e-CF (para el estado y el QR): el comprador del original y el MontoTotal de la E34.</summary>
    internal static FacturaEcf ComoFactura(NotaCredito n, OriginalNota o)
    {
        var d = NotaCreditoEcf.Desglose(n.Monto, n.Codigo, o);
        return new FacturaEcf(n.Encf, n.Numero, n.Fecha, o.Rnc, o.Nombre, d.Gravado + d.Exento, d.Itbis, d.Propina, false, Array.Empty<ItemEcf>(),
            d.Monto);
    }

    /// <summary>
    /// El walk-in como CrearFacturaXmlWalking: si el RNC esta vacio o en ceros, la cedula; credito si algun pago de su
    /// recibo fue a credito (Monedas_id 6).
    /// </summary>
    internal static FacturaEcf? LeerWalkIn(IDbConnection cn, IDbTransaction? tx, long numero)
    {
        var f = cn.QueryFirstOrDefault<FilaFactura>("""
            SELECT w.FacturaWalking_id AS Factura_id, RTRIM(w.NCF) AS NCF, w.Fecha,
                   CASE WHEN REPLACE(LTRIM(RTRIM(ISNULL(w.RNC, ''))), '0', '') = '' THEN RTRIM(w.Cedula) ELSE RTRIM(w.RNC) END AS RNC,
                   RTRIM(w.Nombre) AS NombreCliente, w.SubTotal, w.Itbis, w.Propina, CAST(0 AS bit) AS Transferida,
                   CAST(CASE WHEN EXISTS (SELECT 1 FROM HotelReciboIngreso r JOIN HotelReciboDetalle d ON d.NumeroRecibo = r.NumeroRecibo
                                          WHERE r.FacturaWalking_id = w.FacturaWalking_id AND d.Monedas_id = 6) THEN 1 ELSE 0 END AS bit) AS Credito
            FROM HotelFacturaWalking w WHERE w.FacturaWalking_id = @numero
            """, new { numero }, tx);
        if (f is null) return null;
        var lineas = cn.Query<(string Descripcion, int? Cantidad, decimal? Precio, decimal? Importe, bool? Activo)>("""
            SELECT RTRIM(Descripcion), Cantidad, Precio, Importe, Activo FROM HotelFacturaWalkingDetalle
            WHERE FacturaWalking_id = @numero ORDER BY Secuencia
            """, new { numero }, tx);
        return new FacturaEcf(f.NCF ?? "", (long)f.Factura_id, f.Fecha.Date, f.RNC, f.NombreCliente, f.SubTotal ?? 0, f.Itbis ?? 0,
            f.Propina ?? 0, f.Credito,
            EcfHotel.Items(lineas.Select(l => (l.Descripcion ?? "", (decimal)(l.Cantidad ?? 0), l.Precio ?? 0, l.Importe ?? 0, l.Activo == true))));
    }

    internal static FacturaEcf? Leer(IDbConnection cn, IDbTransaction? tx, long numero, out bool transferida)
    {
        var f = cn.QueryFirstOrDefault<FilaFactura>("""
            SELECT f.Factura_id, RTRIM(f.NCF) AS NCF, f.Fecha, RTRIM(f.RNC) AS RNC, RTRIM(c.Nombre) AS NombreCliente,
                   f.SubTotal, f.Itbis, f.Propina, f.Transferida,
                   CAST(CASE WHEN EXISTS (SELECT 1 FROM HotelReciboIngreso r JOIN HotelReciboDetalle d ON d.NumeroRecibo = r.NumeroRecibo
                                          WHERE r.Reserva_id = f.HabitacionReserva_id AND r.Habitacion = f.Habitacion_id
                                            AND (ISNULL(f.ClaveUnica, 0) = 0 OR r.Factura_id = f.ClaveUnica) AND d.Monedas_id = 6)
                             THEN 1 ELSE 0 END AS bit) AS Credito
            FROM HotelTmpFactura f LEFT JOIN CxcCliente c ON c.NumeroCliente = f.Cliente_id
            WHERE f.Factura_id = @numero
            """, new { numero }, tx);
        transferida = f?.Transferida == true;
        if (f is null) return null;
        var lineas = cn.Query<(string Descripcion, int? Cantidad, decimal? Precio, decimal? Importe, bool? Activo)>("""
            SELECT RTRIM(Descripcion), Cantidad, Precio, Importe, Activo FROM HotelTmpFacturaDetalle
            WHERE Factura_id = @numero ORDER BY Secuencia
            """, new { numero }, tx);
        return new FacturaEcf(f.NCF ?? "", (long)f.Factura_id, f.Fecha.Date, f.RNC, f.NombreCliente, f.SubTotal ?? 0, f.Itbis ?? 0,
            f.Propina ?? 0, f.Credito,
            EcfHotel.Items(lineas.Select(l => (l.Descripcion ?? "", (decimal)(l.Cantidad ?? 0), l.Precio ?? 0, l.Importe ?? 0, l.Activo == true))));
    }

    private static FilaParametro Parametro(IDbConnection cn, IDbTransaction? tx = null) =>
        cn.QueryFirst<FilaParametro>("""
            SELECT TOP 1 RTRIM(RncEmpresa) AS RncEmpresa, RTRIM(NombreEmpresa) AS NombreEmpresa, RTRIM(DireccionEmpresa) AS DireccionEmpresa,
                   RTRIM(TelefonoEmpresa) AS TelefonoEmpresa, RTRIM(UrlApiEcf) AS UrlApiEcf, RTRIM(ClaveApiEcf) AS ClaveApiEcf
            FROM Parametro
            """, transaction: tx);

    internal static EmisorEcf Emisor(IDbConnection cn, IDbTransaction? tx = null)
    {
        var p = Parametro(cn, tx);
        return new EmisorEcf(p.RncEmpresa ?? "", p.NombreEmpresa ?? "", p.DireccionEmpresa ?? "", p.TelefonoEmpresa);
    }

    /// <summary>
    /// El vencimiento de la secuencia del TIPO del e-NCF (ComprobanteFiscal.FechaVencimientoEncf de ese NCodigo).
    /// Delphi (GetFechaVencimiento) tomaba siempre la del E31; cada tipo puede tener su propia fecha autorizada.
    /// </summary>
    internal static DateTime FechaVencimiento(IDbConnection cn, string encf, IDbTransaction? tx = null)
    {
        var codigo = encf.Length >= 3 ? encf[..3] : encf;
        return cn.ExecuteScalar<DateTime?>("SELECT FechaVencimientoEncf FROM ComprobanteFiscal WHERE RTRIM(NCodigo) = @codigo",
                   new { codigo = new DbString { Value = codigo, IsAnsi = true, Length = 3 } }, tx)
               ?? throw new ErrorCatalogo($"Falta la fecha de vencimiento de la secuencia {codigo} (ComprobanteFiscal.FechaVencimientoEncf).");
    }

    /// <summary>Arma, guarda y envia el e-CF de la factura cerrada. No lanza por la DGII: devuelve el estado.</summary>
    public async Task<EstadoEcf> Enviar(long numero, OrigenFactura origen = OrigenFactura.Hospedaje)
    {
        FacturaEcf f;
        string xml;
        FilaParametro p;
        using (var cn = _conexiones.Hotel())
        {
            if (origen is OrigenFactura.NotaHospedaje or OrigenFactura.NotaWalkIn or OrigenFactura.NotaCafeteria)
            {
                // la nota de credito (E34): siempre el e-CF completo (no hay resumen)
                var n = LeerNota(cn, null, origen, numero) ?? throw new ErrorCatalogo($"No existe la nota de credito {numero}.");
                f = ComoFactura(n.Nota, n.Original);
                xml = NotaCreditoEcf.Xml(n.Nota, n.Original, Emisor(cn), DateTime.Now);
            }
            else if (origen == OrigenFactura.CompraInformal)
            {
                // la compra a informal (E41): siempre el e-CF completo; el comprador es el proveedor
                var co = LeerCompra(cn, null, numero) ?? throw new ErrorCatalogo($"No existe la compra {numero}.");
                f = CompraInformalEcf.ComoFactura(co);
                xml = CompraInformalEcf.Xml(co, Emisor(cn), FechaVencimiento(cn, co.Encf), DateTime.Now);
            }
            else if (origen == OrigenFactura.GastoMenor)
            {
                // el gasto menor (E43): siempre el e-CF completo, sin comprador
                var g = LeerGasto(cn, null, numero) ?? throw new ErrorCatalogo($"No existe el gasto menor {numero}.");
                f = GastoMenorEcf.ComoFactura(g);
                xml = GastoMenorEcf.Xml(g, Emisor(cn), FechaVencimiento(cn, g.Encf), DateTime.Now);
            }
            else
            {
                f = Leer(cn, null, origen, numero, out var transferida) ?? throw new ErrorCatalogo($"No existe la factura {numero}.");
                if (transferida) throw new ErrorCatalogo("La factura es de una cuenta transferida: no va a la DGII.");
                xml = EcfHotel.Ecf(f, Emisor(cn), FechaVencimiento(cn, f.Encf), DateTime.Now);
            }
            p = Parametro(cn);
            Guardar(cn, f.Encf, numero, "ECF", xml);
        }
        if (string.IsNullOrWhiteSpace(p.UrlApiEcf)) return new EstadoEcf(f.Encf, numero, "Sin enviar", "Falta Parametro.UrlApiEcf.", false);

        var api = Cliente(p);
        try
        {
            if (EcfHotel.EsResumen(f))
            {
                // la E32 de consumo: se firma completa para el codigo de seguridad y se envia el resumen
                var firma = await Llamar(api, $"/DgiiECfs/FirmarDesdeHotel?encf={f.Encf}", 60);
                var codigo = Texto(firma, "codigoSeguridad");
                if (string.IsNullOrWhiteSpace(codigo))
                    return new EstadoEcf(f.Encf, numero, "Sin enviar", "No se pudo firmar: " + (Texto(firma, "error") ?? Texto(firma, "mensaje") ?? "sin respuesta"), false);
                using (var cn = _conexiones.Hotel())
                    Guardar(cn, f.Encf, numero, "RFCE", EcfHotel.Rfce(f, Emisor(cn), codigo));
                var envio = await Llamar(api, $"/DgiiECfs/EnviarDesdeHotel?encf={f.Encf}&factura={numero}&tipoDocumento=RFCE", 120);
                return await Resultado(api, f, numero, "RFCE", Texto(envio, "mensaje"));
            }
            var r = await Llamar(api, $"/DgiiECfs/EnviarDesdeHotel?encf={f.Encf}&factura={numero}&tipoDocumento=ECF", 120);
            return await Resultado(api, f, numero, "ECF", Texto(r, "mensaje"));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            _log.LogWarning(e, "No se pudo enviar el {Encf} (factura {Factura})", f.Encf, numero);
            return new EstadoEcf(f.Encf, numero, "Sin enviar", $"No hubo conexion con la API ({p.UrlApiEcf}): {e.Message}", false);
        }
    }

    /// <summary>El estado en la DGII de un e-CF ya enviado (sin volver a enviarlo).</summary>
    public async Task<EstadoEcf> Consultar(long numero, OrigenFactura origen = OrigenFactura.Hospedaje)
    {
        FacturaEcf f;
        FilaParametro p;
        using (var cn = _conexiones.Hotel())
        {
            f = Leer(cn, null, origen, numero, out _) ?? throw new ErrorCatalogo($"No existe la factura {numero}.");
            p = Parametro(cn);
        }
        if (string.IsNullOrWhiteSpace(p.UrlApiEcf) || !f.Encf.StartsWith('E')) return new EstadoEcf(f.Encf, numero, "Sin enviar", null, false);
        try
        {
            return await Resultado(Cliente(p), f, numero, EcfHotel.EsResumen(f) ? "RFCE" : "ECF", null);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return new EstadoEcf(f.Encf, numero, "Desconocido", $"No hubo conexion con la API: {e.Message}", false);
        }
    }

    /// <summary>El QR (consultatimbre / consultatimbrefc, como uQrDgii.UrlConsultaTimbre) con el registro del e-CF completo.</summary>
    public async Task<TimbreEcf> Timbre(FacturaEcf f, string rncEmisor)
    {
        string? url;
        using (var cn = _conexiones.Hotel()) url = Parametro(cn).UrlApiEcf;
        if (string.IsNullOrWhiteSpace(url)) return new TimbreEcf(null, null, null);
        try
        {
            var doc = await Documento(Cliente(new FilaParametro { UrlApiEcf = url }), f, "ECF");
            if (doc is null) return new TimbreEcf(null, null, null);
            var codigo = Texto(doc.Value, "codigoSeguridad");
            var firma = Texto(doc.Value, "fechaHoraFirma");
            return new TimbreEcf(codigo, firma, codigo is null ? null : UrlTimbre(f, rncEmisor, codigo, firma ?? "", Ambiente));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return new TimbreEcf(null, null, null);
        }
    }

    /// <summary>1 = testecf, 2 = certecf, otro = ecf (produccion). La API no lo dice: va en la configuracion (Ecf:Ambiente).</summary>
    public int Ambiente => _config.GetValue("Ecf:Ambiente", 3);

    public static string UrlTimbre(FacturaEcf f, string rncEmisor, string codigo, string fechaFirma, int ambiente)
    {
        var ruta = ambiente switch { 1 => "testecf", 2 => "certecf", _ => "ecf" };
        string U(string s) => Uri.EscapeDataString(s.Trim());
        var monto = f.Total.ToString("0.00", CultureInfo.InvariantCulture);
        if (EcfHotel.EsResumen(f))
            return $"https://fc.dgii.gov.do/{ruta}/consultatimbrefc?rncemisor={U(rncEmisor)}&encf={U(f.Encf)}&montototal={monto}&codigoseguridad={U(codigo)}";
        var rnc = (f.Rnc ?? "").Trim();
        var comprador = rnc == "" || rnc.All(c => c == '0') ? "" : $"&rnccomprador={U(rnc)}";
        return $"https://ecf.dgii.gov.do/{ruta}/consultatimbre?rncemisor={U(rncEmisor)}{comprador}&encf={U(f.Encf)}" +
               $"&fechaemision={f.Fecha:dd-MM-yyyy}&montototal={monto}&fechafirma={U(fechaFirma)}&codigoseguridad={U(codigo)}";
    }

    /// <summary>
    /// El estado en la DGII de varias facturas cerradas (la lista de facturas): DocumentosXml se pide UNA vez por tipo
    /// para todo el rango, y los ECF con trackId se consultan de a 4. Las que no tienen e-NCF no van en el resultado.
    /// </summary>
    public async Task<Dictionary<string, EstadoEcf>> Estados(IReadOnlyList<FacturaEcf> facturas)
    {
        var r = new Dictionary<string, EstadoEcf>();
        var conEcf = facturas.Where(f => f.Encf.Length == 13 && f.Encf[0] == 'E').ToList();
        if (conEcf.Count == 0) return r;
        string? url;
        string? clave;
        string rnc;
        using (var cn = _conexiones.Hotel())
        {
            var p = Parametro(cn);
            url = p.UrlApiEcf;
            clave = p.ClaveApiEcf;
            rnc = p.RncEmpresa ?? "";
        }
        if (string.IsNullOrWhiteSpace(url))
        {
            foreach (var f in conEcf) r[f.Encf] = new EstadoEcf(f.Encf, f.Factura, "Sin enviar", "Falta Parametro.UrlApiEcf.", false);
            return r;
        }
        var api = Cliente(new FilaParametro { UrlApiEcf = url, ClaveApiEcf = clave });
        var desde = conEcf.Min(f => f.Fecha).AddDays(-1);
        try
        {
            var docs = new Dictionary<string, List<JsonElement>>();
            foreach (var tipo in new[] { "ECF", "RFCE" })
            {
                var lista = await Llamar(api, $"/DgiiECfs/DocumentosXml?tipoDocumento={tipo}&desde={desde:yyyy-MM-dd}&hasta={DateTime.Today.AddDays(1):yyyy-MM-dd}", 30, get: true);
                docs[tipo] = lista.ValueKind == JsonValueKind.Array ? lista.EnumerateArray().ToList() : new List<JsonElement>();
            }
            using var turnos = new SemaphoreSlim(4);
            var tareas = conEcf.Select(async f =>
            {
                await turnos.WaitAsync();
                try
                {
                    var doc = docs[EcfHotel.EsResumen(f) ? "RFCE" : "ECF"].Where(x => Texto(x, "encf") == f.Encf && DelEmisor(x, rnc))
                        .OrderByDescending(x => x.TryGetProperty("id", out var id) ? id.GetInt64() : 0).Cast<JsonElement?>().FirstOrDefault();
                    return await DeDocumento(api, f, f.Factura, doc, null);
                }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
                {
                    return new EstadoEcf(f.Encf, f.Factura, "Desconocido", e.Message, false);
                }
                finally { turnos.Release(); }
            });
            foreach (var e in await Task.WhenAll(tareas)) r[e.Encf] = e;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            foreach (var f in conEcf) r[f.Encf] = new EstadoEcf(f.Encf, f.Factura, "Desconocido", $"No hubo conexion con la API: {e.Message}", false);
        }
        return r;
    }

    private async Task<EstadoEcf> Resultado(HttpClient api, FacturaEcf f, long numero, string tipo, string? mensajeEnvio) =>
        await DeDocumento(api, f, numero, await Documento(api, f, tipo), mensajeEnvio);

    private async Task<EstadoEcf> DeDocumento(HttpClient api, FacturaEcf f, long numero, JsonElement? doc, string? mensajeEnvio)
    {
        if (doc is null) return new EstadoEcf(f.Encf, numero, "Sin enviar", mensajeEnvio ?? "No llego a la DGII.", false);
        var d = doc.Value;
        if (Texto(d, "estado") != "Enviado")
            return new EstadoEcf(f.Encf, numero, Texto(d, "estado") ?? "Sin enviar", mensajeEnvio ?? Texto(d, "respuestaDgii"), false);
        // el ECF trae trackId: la DGII responde aparte; el RFCE trae la respuesta en respuestaDgii
        JsonElement? resp = null;
        if (Texto(d, "trackId") is { Length: > 0 } track) resp = await Llamar(api, $"/DgiiECfs/ConsultarResultado/{track}", 30, get: true);
        else if (Texto(d, "respuestaDgii") is { Length: > 0 } json) resp = JsonDocument.Parse(json).RootElement.Clone();
        var estado = resp is { } r ? Texto(r, "estado") ?? "En proceso" : "En proceso";
        string? mensajes = null;
        if (resp is { } rr && rr.TryGetProperty("mensajes", out var m) && m.ValueKind == JsonValueKind.Array)
            mensajes = string.Join("; ", m.EnumerateArray().Select(x => Texto(x, "valor")).Where(x => !string.IsNullOrWhiteSpace(x)));
        if (resp is { } dgii)
            GuardarEstado(f.Encf, numero, Texto(d, "tipoDocumento") ?? "ECF", estado,
                int.TryParse(Texto(dgii, "codigo"), out var c) ? c : null, mensajes, Texto(d, "trackId"));
        return new EstadoEcf(f.Encf, numero, estado, string.IsNullOrWhiteSpace(mensajes) ? null : mensajes,
            estado.StartsWith("Aceptado", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// La respuesta de la DGII en EcfEstadoDgii (sp_GuardarEstadoEcf), como el monitor de Delphi (UFrmEstadoEcf): la
    /// leen el monitor, los listados de pendientes y Stp_AnularFacturaHotelNC (exige la factura aceptada, codigo 1 o 4).
    /// Si falla no importa: se vuelve a guardar en la proxima consulta.
    /// </summary>
    private void GuardarEstado(string encf, long numero, string tipo, string estado, int? codigo, string? mensaje, string? trackId)
    {
        try
        {
            using var cn = _conexiones.Hotel();
            cn.Execute("dbo.sp_GuardarEstadoEcf", new
            {
                eNCF = new DbString { Value = encf, IsAnsi = true, Length = 19 },
                InvFactura = (int)numero,
                TipoEnvio = new DbString { Value = tipo, IsAnsi = true, Length = 10 },
                Estado = new DbString { Value = estado.Length > 40 ? estado[..40] : estado, IsAnsi = true, Length = 40 },
                Codigo = codigo,
                Mensaje = new DbString { Value = mensaje is { Length: > 2000 } x ? x[..2000] : mensaje ?? "", IsAnsi = false, Length = 2000 },
                TrackId = new DbString { Value = trackId ?? "", IsAnsi = true, Length = 60 },
            }, commandType: CommandType.StoredProcedure);
        }
        catch (Exception e) when (e is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
        {
            _log.LogWarning(e, "No se pudo guardar el estado de {Encf} en EcfEstadoDgii", encf);
        }
    }

    /// <summary>
    /// DocumentosXml lo pueden compartir varias empresas y los e-NCF se repiten entre ellas (Delphi filtra por RncEmisor, ver
    /// FiltroRncEmisor): solo cuentan los del RNC del hotel. Si la API no trae el RNC, se acepta.
    /// </summary>
    private static bool DelEmisor(JsonElement x, string rnc) =>
        Texto(x, "rncEmisor") is not { Length: > 0 } r || r.Trim() == rnc.Trim();

    /// <summary>El registro mas nuevo de DocumentosXml de ese e-NCF y tipo (la API filtra por fecha de creacion).</summary>
    private async Task<JsonElement?> Documento(HttpClient api, FacturaEcf f, string tipo)
    {
        var desde = f.Fecha.AddDays(-1).ToString("yyyy-MM-dd");
        var hasta = DateTime.Today.AddDays(1).ToString("yyyy-MM-dd");
        var lista = await Llamar(api, $"/DgiiECfs/DocumentosXml?tipoDocumento={tipo}&desde={desde}&hasta={hasta}", 30, get: true);
        if (lista.ValueKind != JsonValueKind.Array) return null;
        string rnc;
        using (var cn = _conexiones.Hotel()) rnc = Parametro(cn).RncEmpresa ?? "";
        return lista.EnumerateArray().Where(x => Texto(x, "encf") == f.Encf && DelEmisor(x, rnc))
            .OrderByDescending(x => x.TryGetProperty("id", out var id) ? id.GetInt64() : 0).Cast<JsonElement?>().FirstOrDefault();
    }

    private HttpClient Cliente(FilaParametro p)
    {
        var c = _http.CreateClient("ecf");
        c.BaseAddress = new Uri(p.UrlApiEcf!.TrimEnd('/') + "/");
        if (!string.IsNullOrWhiteSpace(p.ClaveApiEcf)) c.DefaultRequestHeaders.Add("X-Api-Key", p.ClaveApiEcf);
        return c;
    }

    private static async Task<JsonElement> Llamar(HttpClient api, string ruta, int segundos, bool get = false)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(segundos));
        using var r = get ? await api.GetAsync(ruta.TrimStart('/'), cts.Token)
                          : await api.PostAsync(ruta.TrimStart('/'), new StringContent(""), cts.Token);
        var texto = await r.Content.ReadAsStringAsync(cts.Token);
        if (string.IsNullOrWhiteSpace(texto)) return default;
        try { return JsonDocument.Parse(texto).RootElement.Clone(); }
        catch (JsonException) { return JsonDocument.Parse(JsonSerializer.Serialize(new { mensaje = $"HTTP {(int)r.StatusCode}: {texto[..Math.Min(200, texto.Length)]}" })).RootElement.Clone(); }
    }

    private static string? Texto(JsonElement e, string clave) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(clave, out var v)
            ? v.ValueKind switch { JsonValueKind.String => v.GetString(), JsonValueKind.Null or JsonValueKind.Undefined => null, _ => v.ToString() }
            : null;

    private static void Guardar(IDbConnection cn, string encf, long factura, string tipo, string xml) =>
        cn.Execute("dbo.sp_GuardarFacturaXml", new
        {
            Encf = new DbString { Value = encf, IsAnsi = true, Length = 19 },
            Factura = (int)factura,
            TipoDocumento = new DbString { Value = tipo, IsAnsi = true, Length = 10 },
            Xml = new DbString { Value = xml, IsAnsi = false, Length = -1 },
        }, commandType: CommandType.StoredProcedure);
}
