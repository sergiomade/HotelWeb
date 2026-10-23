using System.Globalization;
using HotelWeb.Core.Identificacion;

namespace HotelWeb.Core.Ecf;

/// <summary>La nota de credito (HotelFacturaNC / HotelFacturaWalkingNC).</summary>
/// <param name="Codigo">CodigoModificacion: 1 anula el NCF modificado, 2 corrige texto, 3 corrige montos.</param>
public sealed record NotaCredito(string Encf, long Numero, DateTime Fecha, decimal Monto, int Codigo, string? Razon);

/// <summary>La factura que la nota modifica.</summary>
public sealed record OriginalNota(string Ncf, DateTime Fecha, string? Rnc, string? Nombre, decimal SubTotal, decimal Itbis, decimal Propina)
{
    public decimal Total => SubTotal + Itbis + Propina;
}

public sealed record DesgloseNota(decimal Gravado, decimal Exento, decimal Itbis, decimal Propina, decimal Monto);

/// <summary>
/// La nota de credito electronica (E34) del hotel, copia de ArmarYEnviarE34 (Comunes\UCreateXmlHotel.pas), que la DGII
/// ya acepta (E340000000001): un solo item por lo acreditado, ITBIS y propina en proporcion a la factura original; la
/// anulacion total (codigo 1) lleva los montos exactos del original. Mas de 30 dias: IndicadorNotaCredito = 1.
/// </summary>
public static class NotaCreditoEcf
{
    /// <summary>NotaCreditoSinItbis de Delphi: mas de 30 dias despues de la factura no se rebaja el ITBIS.</summary>
    public static bool SinItbis(DateTime fechaNota, DateTime fechaOriginal) => (fechaNota.Date - fechaOriginal.Date).TotalDays > 30;

    /// <summary>DesglosarNotaCredito + el caso de la anulacion total.</summary>
    public static DesgloseNota Desglose(decimal monto, int codigo, OriginalNota o)
    {
        if (codigo == 1)
            return o.Itbis > 0
                ? new DesgloseNota(o.SubTotal, 0, o.Itbis, o.Propina, o.Total)
                : new DesgloseNota(0, o.SubTotal, o.Itbis, o.Propina, o.Total);
        if (o.Total <= 0) return new DesgloseNota(0, monto, 0, 0, monto);
        var factor = monto / o.Total;
        var itbis = R2(o.Itbis * factor);
        var propina = R2(o.Propina * factor);
        var baseNota = monto - itbis - propina;
        return o.Itbis > 0 ? new DesgloseNota(baseNota, 0, itbis, propina, monto) : new DesgloseNota(0, baseNota, itbis, propina, monto);
    }

    /// <summary>Redondear2 de Delphi: al centavo, el .5 hacia afuera.</summary>
    private static decimal R2(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    private static string RncComprador(OriginalNota o)
    {
        var r = (o.Rnc ?? "").Trim();
        return IdentificacionRD.SinIdentificacion(r) ? "" : r;
    }

    /// <summary>Lo que impide armar la nota, o null.</summary>
    public static string? Validar(NotaCredito n, OriginalNota o)
    {
        if (!n.Encf.StartsWith("E34") || n.Encf.Length != 13) return $"La nota de credito no tiene un e-NCF E34: {n.Encf}";
        if (string.IsNullOrWhiteSpace(o.Ncf)) return "No se encontro el NCF de la factura original.";
        if (n.Codigo is not (1 or 2 or 3)) return "La nota no tiene codigo de modificacion.";
        var rnc = RncComprador(o);
        if (rnc != "" && IdentificacionRD.Determinar(rnc, out _) == TipoIdentificacion.Invalido)
            return $"El RNC o cedula del cliente de la factura original no es valido: {rnc}";
        return null;
    }

    public static string Xml(NotaCredito n, OriginalNota o, EmisorEcf e, DateTime fechaHoraFirma)
    {
        if (Validar(n, o) is { } motivo) throw new ErrorEcf(motivo);
        var d = Desglose(n.Monto, n.Codigo, o);
        var x = new XmlDelphi();
        x.Abrir("ECF");
        x.Abrir("Encabezado");
        x.Nodo("Version", "1.0");
        x.Abrir("IdDoc");
        x.Nodo("TipoeCF", "34");
        x.Nodo("eNCF", n.Encf);
        x.Nodo("IndicadorNotaCredito", SinItbis(n.Fecha, o.Fecha) ? "1" : "0");
        x.Nodo("IndicadorMontoGravado", "0");
        x.Nodo("TipoIngresos", "01");
        x.Nodo("TipoPago", "1");
        x.Cerrar();
        x.Abrir("Emisor");
        x.Nodo("RNCEmisor", e.Rnc.Trim());
        x.Nodo("RazonSocialEmisor", e.RazonSocial.Trim());
        x.Nodo("NombreComercial", e.RazonSocial.Trim());
        x.Nodo("DireccionEmisor", e.Direccion.Trim());
        x.Nodo("NumeroFacturaInterna", n.Numero.ToString(CultureInfo.InvariantCulture));
        x.Nodo("FechaEmision", F(n.Fecha));
        x.Cerrar();
        // el comprador es el de la factura original; sin identificacion (consumo) no va
        var rnc = RncComprador(o);
        var tipo = IdentificacionRD.Determinar(rnc, out var limpio);
        if (rnc != "" && tipo != TipoIdentificacion.Invalido)
        {
            x.Abrir("Comprador");
            x.Nodo(tipo == TipoIdentificacion.Pasaporte ? "IdentificadorExtranjero" : "RNCComprador", limpio);
            x.Nodo("RazonSocialComprador", (o.Nombre ?? "").Trim());
            x.Cerrar();
        }
        x.Abrir("Totales");
        if (d.Gravado > 0)
        {
            x.Nodo("MontoGravadoTotal", M(d.Gravado));
            x.Nodo("MontoGravadoI1", M(d.Gravado));
        }
        if (d.Exento > 0) x.Nodo("MontoExento", M(d.Exento));
        if (d.Gravado > 0)
        {
            x.Nodo("ITBIS1", "18");
            x.Nodo("TotalITBIS", M(d.Itbis));
            x.Nodo("TotalITBIS1", M(d.Itbis));
        }
        if (d.Propina > 0)
        {
            x.Nodo("MontoImpuestoAdicional", M(d.Propina));
            x.Abrir("ImpuestosAdicionales");
            x.Abrir("ImpuestoAdicional");
            x.Nodo("TipoImpuesto", "001");
            x.Nodo("TasaImpuestoAdicional", "10.00");
            x.Nodo("OtrosImpuestosAdicionales", M(d.Propina));
            x.Cerrar();
            x.Cerrar();
        }
        x.Nodo("MontoTotal", M(d.Monto));
        x.Cerrar();
        x.Cerrar();   // Encabezado

        x.Abrir("DetallesItems");
        x.Abrir("Item");
        x.Nodo("NumeroLinea", "1");
        x.Nodo("IndicadorFacturacion", d.Gravado > 0 ? "1" : "4");
        var nombre = "NOTA DE CREDITO A " + o.Ncf.Trim();
        x.Nodo("NombreItem", nombre.Length > 80 ? nombre[..80] : nombre);
        x.Nodo("IndicadorBienoServicio", "1");
        x.Nodo("CantidadItem", "1.00");
        x.Nodo("UnidadMedida", "43");
        x.Nodo("PrecioUnitarioItem", M(d.Gravado + d.Exento));
        if (d.Propina > 0)
        {
            x.Abrir("TablaImpuestoAdicional");
            x.Abrir("ImpuestoAdicional");
            x.Nodo("TipoImpuesto", "001");
            x.Cerrar();
            x.Cerrar();
        }
        x.Nodo("MontoItem", M(d.Gravado + d.Exento));
        x.Cerrar();
        x.Cerrar();

        x.Abrir("InformacionReferencia");
        x.Nodo("NCFModificado", o.Ncf.Trim());
        x.Nodo("FechaNCFModificado", F(o.Fecha));
        x.Nodo("CodigoModificacion", n.Codigo.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(n.Razon)) x.Nodo("RazonModificacion", n.Razon.Trim());
        x.Cerrar();
        x.Nodo("FechaHoraFirma", fechaHoraFirma.ToString("dd-MM-yyyy HH:mm:ss", CultureInfo.InvariantCulture));
        x.Cerrar();
        return x.Texto();
    }

    private static string F(DateTime d) => d.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
    private static string M(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture);
}
