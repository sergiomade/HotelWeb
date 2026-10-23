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
    public async Task<EstadoEcf> Enviar(long numero)
    {
        FacturaEcf f;
        string xml;
        FilaParametro p;
        using (var cn = _conexiones.Hotel())
        {
            f = Leer(cn, null, numero, out var transferida) ?? throw new ErrorCatalogo($"No existe la factura {numero}.");
            if (transferida) throw new ErrorCatalogo("La factura es de una cuenta transferida: no va a la DGII.");
            p = Parametro(cn);
            xml = EcfHotel.Ecf(f, Emisor(cn), FechaVencimiento(cn, f.Encf), DateTime.Now);
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
    public async Task<EstadoEcf> Consultar(long numero)
    {
        FacturaEcf f;
        FilaParametro p;
        using (var cn = _conexiones.Hotel())
        {
            f = Leer(cn, null, numero, out _) ?? throw new ErrorCatalogo($"No existe la factura {numero}.");
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

    private async Task<EstadoEcf> Resultado(HttpClient api, FacturaEcf f, long numero, string tipo, string? mensajeEnvio)
    {
        var doc = await Documento(api, f, tipo);
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
        return new EstadoEcf(f.Encf, numero, estado, string.IsNullOrWhiteSpace(mensajes) ? null : mensajes,
            estado.StartsWith("Aceptado", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>El registro mas nuevo de DocumentosXml de ese e-NCF y tipo (la API filtra por fecha de creacion).</summary>
    private async Task<JsonElement?> Documento(HttpClient api, FacturaEcf f, string tipo)
    {
        var desde = f.Fecha.AddDays(-1).ToString("yyyy-MM-dd");
        var hasta = DateTime.Today.AddDays(1).ToString("yyyy-MM-dd");
        var lista = await Llamar(api, $"/DgiiECfs/DocumentosXml?tipoDocumento={tipo}&desde={desde}&hasta={hasta}", 30, get: true);
        if (lista.ValueKind != JsonValueKind.Array) return null;
        return lista.EnumerateArray().Where(x => Texto(x, "encf") == f.Encf)
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
