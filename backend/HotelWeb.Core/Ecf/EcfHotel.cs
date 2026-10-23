using System.Globalization;
using System.Text;
using HotelWeb.Core.Identificacion;

namespace HotelWeb.Core.Ecf;

public sealed record EmisorEcf(string Rnc, string RazonSocial, string Direccion, string? Telefono);

/// <summary>Una linea del e-CF: las de la cuenta activas y positivas, sin los PAY-OUT.</summary>
public sealed record ItemEcf(string Descripcion, decimal Cantidad, decimal Precio, decimal Importe);

/// <summary>La factura cerrada (HotelTmpFactura) con lo que lleva el e-CF.</summary>
public sealed record FacturaEcf(string Encf, long Factura, DateTime Fecha, string? Rnc, string? NombreCliente, decimal SubTotal,
    decimal Itbis, decimal Propina, bool Credito, IReadOnlyList<ItemEcf> Items)
{
    public string Tipo => Encf.Length >= 3 ? Encf.Substring(1, 2) : "";
    public decimal Total => SubTotal + Itbis + Propina;
}

public sealed class ErrorEcf(string mensaje) : Exception(mensaje);

/// <summary>
/// El XML del e-CF del hotel, copia de ArmarYEnviarEcf y CrearRFCE (Comunes\UCreateXmlHotel.pas), que la DGII ya acepta:
/// mismo orden de nodos y formatos. Unica diferencia, decision del usuario (2026-10-23): los PAY-OUT no van como items
/// (Delphi los ponia en los items y no en los totales, y los items no cuadraban con el MontoGravado).
/// </summary>
public static class EcfHotel
{
    /// <summary>LimiteResumenConsumo de Delphi: una E32 de menos se envia como resumen (RFCE).</summary>
    public const decimal LimiteResumen = 250_000m;

    public static bool EsResumen(FacturaEcf f) => f.Tipo == "32" && f.Total < LimiteResumen;

    /// <summary>Los items del e-CF a partir de las lineas de la cuenta.</summary>
    public static IReadOnlyList<ItemEcf> Items(IEnumerable<(string Descripcion, decimal Cantidad, decimal Precio, decimal Importe, bool Activo)> lineas) =>
        lineas.Where(l => l.Activo && l.Importe > 0 && !l.Descripcion.TrimStart().StartsWith("PAY-OUT", StringComparison.OrdinalIgnoreCase))
            .Select(l => new ItemEcf(l.Descripcion.Trim(), l.Cantidad, l.Precio, l.Importe)).ToList();

    /// <summary>Lo que la DGII rechazaria (o Delphi no deja), antes de armar nada. Null si esta bien.</summary>
    public static string? Validar(FacturaEcf f)
    {
        if (f.Encf.Length != 13 || f.Encf[0] != 'E') return $"La factura {f.Factura} no tiene e-NCF.";
        if (f.Tipo == "44" && f.Itbis > 0) return "Factura E44 (regimen especial) no puede llevar ITBIS. Revise el cliente.";
        if (f.Tipo != "43")
        {
            var rnc = Rnc(f);
            var tipo = IdentificacionRD.Determinar(rnc, out _);
            if (f.Tipo == "31" && tipo is not (TipoIdentificacion.Rnc or TipoIdentificacion.Cedula))
                return $"Factura de credito fiscal (E31): el RNC o cedula del cliente no es valido: {rnc}";
            if (!(rnc == "" && EsResumen(f)) && tipo == TipoIdentificacion.Invalido)
                return rnc == "" ? "Falta el RNC, la cedula o el pasaporte del cliente." : $"Identificacion invalida: {rnc}";
        }
        foreach (var i in f.Items)
        {
            if (i.Precio < 1) return $"Precio unitario no puede ser menor de 1.00 ({i.Descripcion}).";
            if (i.Cantidad < 1) return $"Cantidad de items es invalida ({i.Descripcion}).";
        }
        if (f.Items.Count == 0) return "La factura no tiene cargos.";
        return null;
    }

    private static string Rnc(FacturaEcf f)
    {
        var r = (f.Rnc ?? "").Trim();
        return IdentificacionRD.SinIdentificacion(r) ? "" : r;
    }

    public static string Ecf(FacturaEcf f, EmisorEcf e, DateTime fechaVencimiento, DateTime fechaHoraFirma)
    {
        if (Validar(f) is { } motivo) throw new ErrorEcf(motivo);
        var x = new Xml();
        x.Abrir("ECF");
        x.Abrir("Encabezado");
        x.Nodo("Version", "1.0");

        x.Abrir("IdDoc");
        x.Nodo("TipoeCF", f.Tipo);
        x.Nodo("eNCF", f.Encf);
        if (f.Tipo != "32") x.Nodo("FechaVencimientoSecuencia", Fecha(fechaVencimiento));
        if (f.Tipo is not ("43" or "44" or "46" or "47")) x.Nodo("IndicadorMontoGravado", "0");
        if (f.Tipo is not ("43" or "47")) x.Nodo("TipoIngresos", "01");
        if (f.Credito)
        {
            x.Nodo("TipoPago", "2");
            x.Nodo("FechaLimitePago", Fecha(f.Fecha.AddDays(30)));
        }
        else x.Nodo("TipoPago", "1");
        if (f.Tipo == "44")
        {
            x.Abrir("TablaFormasPago");
            x.Abrir("FormaDePago");
            x.Nodo("FormaPago", "1");
            x.Nodo("MontoPago", M(f.Total));
            x.Cerrar();
            x.Cerrar();
        }
        x.Cerrar();

        x.Abrir("Emisor");
        x.Nodo("RNCEmisor", e.Rnc.Trim());
        x.Nodo("RazonSocialEmisor", e.RazonSocial.Trim());
        x.Nodo("NombreComercial", e.RazonSocial.Trim());
        x.Nodo("DireccionEmisor", e.Direccion.Trim());
        if (f.Tipo is "44" or "45" && TelefonoDgii(e.Telefono) is { } tel)
        {
            x.Abrir("TablaTelefonoEmisor");
            x.Nodo("TelefonoEmisor", tel);
            x.Cerrar();
        }
        x.Nodo("NumeroFacturaInterna", f.Factura.ToString(CultureInfo.InvariantCulture));
        x.Nodo("FechaEmision", Fecha(f.Fecha));
        x.Cerrar();

        if (f.Tipo != "43")
        {
            var rnc = Rnc(f);
            if (!(rnc == "" && EsResumen(f)))
            {
                var tipo = IdentificacionRD.Determinar(rnc, out var limpio);
                x.Abrir("Comprador");
                x.Nodo(tipo is TipoIdentificacion.Cedula or TipoIdentificacion.Rnc ? "RNCComprador" : "IdentificadorExtranjero", limpio);
                x.Nodo("RazonSocialComprador", rnc == "" ? "" : (f.NombreCliente ?? "").Trim());
                x.Nodo("FechaEntrega", Fecha(f.Fecha));
                x.Cerrar();
            }
        }

        var gravada = f.Itbis > 0;
        x.Abrir("Totales");
        if (gravada)
        {
            x.Nodo("MontoGravadoTotal", M(f.SubTotal));
            x.Nodo("MontoGravadoI1", M(f.SubTotal));
            x.Nodo("ITBIS1", "18");
            x.Nodo("TotalITBIS", M(f.Itbis));
            x.Nodo("TotalITBIS1", M(f.Itbis));
        }
        if (!gravada && f.Tipo != "46") x.Nodo("MontoExento", M(f.SubTotal));
        if (f.Propina > 0)
        {
            x.Nodo("MontoImpuestoAdicional", M(f.Propina));
            x.Abrir("ImpuestosAdicionales");
            x.Abrir("ImpuestoAdicional");
            x.Nodo("TipoImpuesto", "001");
            x.Nodo("TasaImpuestoAdicional", "10.00");
            x.Nodo("OtrosImpuestosAdicionales", M(f.Propina));
            x.Cerrar();
            x.Cerrar();
        }
        x.Nodo("MontoTotal", M(f.Total));
        if (f.Tipo != "45") x.Nodo("MontoPeriodo", M(f.Total));
        x.Nodo("ValorPagar", M(f.Total));
        x.Cerrar();
        x.Cerrar();   // Encabezado

        x.Abrir("DetallesItems");
        var n = 0;
        foreach (var i in f.Items)
        {
            x.Abrir("Item");
            x.Nodo("NumeroLinea", (++n).ToString(CultureInfo.InvariantCulture));
            x.Nodo("IndicadorFacturacion", gravada ? "1" : "4");   // 1 = ITBIS 18 %, 4 = exento
            x.Nodo("NombreItem", i.Descripcion);
            x.Nodo("IndicadorBienoServicio", "1");
            x.Nodo("CantidadItem", M(i.Cantidad));
            x.Nodo("UnidadMedida", "43");
            x.Nodo("PrecioUnitarioItem", i.Precio.ToString("0.0000", CultureInfo.InvariantCulture));
            if (f.Propina > 0)
            {
                x.Abrir("TablaImpuestoAdicional");
                x.Abrir("ImpuestoAdicional");
                x.Nodo("TipoImpuesto", "001");
                x.Cerrar();
                x.Cerrar();
            }
            x.Nodo("MontoItem", M(i.Importe));
            x.Cerrar();
        }
        x.Cerrar();
        x.Nodo("FechaHoraFirma", fechaHoraFirma.ToString("dd-MM-yyyy HH:mm:ss", CultureInfo.InvariantCulture));
        x.Cerrar();
        return x.Texto();
    }

    /// <summary>El resumen de la E32 de menos de 250 mil, con el codigo de seguridad del e-CF completo ya firmado.</summary>
    public static string Rfce(FacturaEcf f, EmisorEcf e, string codigoSeguridad)
    {
        if (string.IsNullOrWhiteSpace(codigoSeguridad)) throw new ErrorEcf($"No se tiene el codigo de seguridad del E32 {f.Encf}: no se puede crear el RFCE.");
        var x = new Xml();
        x.Abrir("RFCE");
        x.Abrir("Encabezado");
        x.Nodo("Version", "1.0");
        x.Abrir("IdDoc");
        x.Nodo("TipoeCF", "32");
        x.Nodo("eNCF", f.Encf);
        x.Nodo("TipoIngresos", "01");
        x.Nodo("TipoPago", f.Credito ? "2" : "1");
        x.Cerrar();
        x.Abrir("Emisor");
        x.Nodo("RNCEmisor", e.Rnc.Trim());
        x.Nodo("RazonSocialEmisor", e.RazonSocial.Trim());
        x.Nodo("FechaEmision", Fecha(f.Fecha));
        x.Cerrar();
        var rnc = Rnc(f);
        var tipo = IdentificacionRD.Determinar(rnc, out var limpio);
        if (tipo is TipoIdentificacion.Cedula or TipoIdentificacion.Rnc or TipoIdentificacion.Pasaporte)
        {
            x.Abrir("Comprador");
            x.Nodo(tipo == TipoIdentificacion.Pasaporte ? "IdentificadorExtranjero" : "RNCComprador", limpio);
            x.Nodo("RazonSocialComprador", (f.NombreCliente ?? "").Trim());
            x.Cerrar();
        }
        x.Abrir("Totales");
        if (f.Itbis > 0)
        {
            x.Nodo("MontoGravadoTotal", M(f.SubTotal));
            x.Nodo("MontoGravadoI1", M(f.SubTotal));
            x.Nodo("TotalITBIS", M(f.Itbis));
            x.Nodo("TotalITBIS1", M(f.Itbis));
        }
        else x.Nodo("MontoExento", M(f.SubTotal));
        if (f.Propina > 0)
        {
            x.Nodo("MontoImpuestoAdicional", M(f.Propina));
            x.Abrir("ImpuestosAdicionales");
            x.Abrir("ImpuestoAdicional");
            x.Nodo("TipoImpuesto", "001");
            x.Nodo("OtrosImpuestosAdicionales", M(f.Propina));
            x.Cerrar();
            x.Cerrar();
        }
        x.Nodo("MontoTotal", M(f.Total));
        x.Cerrar();
        x.Nodo("CodigoSeguridadeCF", codigoSeguridad.Trim());
        x.Cerrar();
        x.Cerrar();
        return x.Texto();
    }

    /// <summary>999-999-9999 si tiene 10 digitos (el nodo es opcional), como TelefonoEmisorDgii.</summary>
    public static string? TelefonoDgii(string? telefono)
    {
        var d = new string((telefono ?? "").Where(char.IsAsciiDigit).ToArray());
        return d.Length == 10 ? $"{d[..3]}-{d[3..6]}-{d[6..]}" : null;
    }

    private static string Fecha(DateTime d) => d.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
    private static string M(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>Como el XML de Delphi (TXMLDocument, doNodeAutoIndent): 2 espacios, CRLF.</summary>
    private sealed class Xml
    {
        private readonly StringBuilder _sb = new("<?xml version=\"1.0\"?>\r\n");
        private readonly Stack<string> _abiertos = new();

        private string Sangria => new(' ', _abiertos.Count * 2);

        public void Abrir(string nombre)
        {
            _sb.Append(Sangria).Append('<').Append(nombre).Append(">\r\n");
            _abiertos.Push(nombre);
        }

        public void Cerrar()
        {
            var n = _abiertos.Pop();
            _sb.Append(Sangria).Append("</").Append(n).Append(">\r\n");
        }

        public void Nodo(string nombre, string valor) =>
            _sb.Append(Sangria).Append('<').Append(nombre).Append('>').Append(Escapar(valor)).Append("</").Append(nombre).Append(">\r\n");

        public string Texto() => _sb.ToString();

        private static string Escapar(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
