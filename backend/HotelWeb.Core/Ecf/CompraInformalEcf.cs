using System.Globalization;

namespace HotelWeb.Core.Ecf;

/// <summary>Una linea de la compra (HotelCompraInformalLinea). Precio sin ITBIS.</summary>
/// <param name="BienServicio">1 = bien, 2 = servicio (el ISR solo se retiene sobre servicios).</param>
public sealed record LineaCompra(string Descripcion, int BienServicio, decimal Cantidad, decimal Precio, bool Gravado,
    decimal Monto = 0, decimal Itbis = 0, decimal ItbisRetenido = 0, decimal IsrRetenido = 0);

/// <summary>La compra a un proveedor informal (HotelCompraInformal, MIG_16 de Delphi), con los montos ya calculados.</summary>
/// <param name="TipoIsr">N sin retencion (solo bienes), P 15 % profesionales, A 10 % alquileres, T 3 % servicios tecnicos.</param>
/// <param name="FormaPago">Codigo del 606: 01 efectivo, 02 cheque / transferencia, 03 tarjeta, 04 credito.</param>
public sealed record CompraInformal(string Encf, long Numero, DateTime Fecha, DateTime FechaPago, string Cedula, string Proveedor, string? Direccion,
    string TipoIsr, string FormaPago, string TipoGasto, string? Observacion, IReadOnlyList<LineaCompra> Lineas,
    decimal MontoGravado, decimal MontoExento, decimal Itbis, decimal Total, decimal ItbisRetenido, decimal IsrRetenido, decimal NetoPagar,
    string? Usuario = null);

public sealed record CalculoCompra(IReadOnlyList<LineaCompra> Lineas, decimal MontoServicios, decimal MontoBienes, decimal MontoGravado,
    decimal MontoExento, decimal Itbis, decimal Total, decimal ItbisRetenido, decimal IsrRetenido, decimal NetoPagar);

/// <summary>
/// El Comprobante Electronico de Compras (E41), como CrearCompraInformalXml de Delphi (Hotel\Source\UFrmCompraInformal.pas),
/// aceptado por la DGII (E410000000900 y E410000000901 del hotel). Norma General 05-19, Art. 7: lo emite el hotel cuando
/// COMPRA a personas fisicas no registradas como contribuyentes. Se retiene el 100 % del ITBIS y, en los servicios, el ISR;
/// al proveedor se le paga el total menos las retenciones. Los calculos son los de Stp_CrearCompraInformal.
/// </summary>
public static class CompraInformalEcf
{
    public const int MaxLineas = 20;

    public static readonly IReadOnlyDictionary<string, (string Nombre, decimal Porcentaje)> RetencionesIsr = new Dictionary<string, (string, decimal)>
    {
        ["N"] = ("Sin retención (solo compras de bienes)", 0),
        ["P"] = ("15 % honorarios / profesionales", 15),
        ["A"] = ("10 % alquileres", 10),
        ["T"] = ("3 % servicios técnicos", 3),
    };

    private static decimal R2(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    /// <summary>Por linea, redondeado a 2 decimales; los totales son la suma de las lineas (como el SP: cuadran con el XML).</summary>
    public static CalculoCompra Calcular(IEnumerable<LineaCompra> lineas, string tipoIsr)
    {
        var porcentaje = RetencionesIsr.TryGetValue(tipoIsr, out var r) ? r.Porcentaje : 0;
        var l = lineas.Select(x =>
        {
            var monto = R2(x.Cantidad * x.Precio);
            var itbis = x.Gravado ? R2(monto * 0.18m) : 0;
            return x with { Monto = monto, Itbis = itbis, ItbisRetenido = itbis, IsrRetenido = x.BienServicio == 2 ? R2(monto * porcentaje / 100) : 0 };
        }).ToList();
        var total = l.Sum(x => x.Monto) + l.Sum(x => x.Itbis);
        return new CalculoCompra(l, l.Where(x => x.BienServicio == 2).Sum(x => x.Monto), l.Where(x => x.BienServicio != 2).Sum(x => x.Monto),
            l.Where(x => x.Gravado).Sum(x => x.Monto), l.Where(x => !x.Gravado).Sum(x => x.Monto), l.Sum(x => x.Itbis), total,
            l.Sum(x => x.ItbisRetenido), l.Sum(x => x.IsrRetenido), total - l.Sum(x => x.ItbisRetenido) - l.Sum(x => x.IsrRetenido));
    }

    /// <summary>Las revisiones de Stp_CrearCompraInformal, para avisar antes de pedir el numero.</summary>
    public static string? Validar(DateTime fecha, DateTime fechaPago, string? cedula, string? proveedor, string? direccion, bool verificado,
        string? tipoIsr, string? formaPago, string? tipoGasto, string? observacion, IReadOnlyList<LineaCompra> lineas, DateTime hoy)
    {
        var c = (cedula ?? "").Replace("-", "").Replace(" ", "").Trim();
        if (fecha.Date > hoy.Date) return "La fecha del comprobante no puede ser futura.";
        if (!verificado) return "Confirme que verificó en la DGII (consulta de RNC) que el proveedor NO está registrado como contribuyente: si lo está, el E41 no procede.";
        if (c.Length != 11 || !c.All(char.IsAsciiDigit)) return "La cédula del proveedor debe tener 11 dígitos.";
        var p = (proveedor ?? "").Trim();
        if (p.Length < 3) return "Escriba el nombre del proveedor.";
        if (p.Length > 150) return "El nombre del proveedor admite hasta 150 letras.";
        if ((direccion ?? "").Trim().Length > 100) return "La dirección del proveedor admite hasta 100 letras.";
        if (!RetencionesIsr.ContainsKey(tipoIsr ?? "")) return "Tipo de retención de ISR no válido.";
        if (!GastoMenorEcf.FormasPago.ContainsKey(formaPago ?? "")) return "Forma de pago no válida.";
        if (!GastoMenorEcf.TiposGasto.ContainsKey(tipoGasto ?? "")) return "Tipo de gasto (606) no válido.";
        if (fechaPago.Date < fecha.Date) return "La fecha de pago no puede ser anterior a la del comprobante.";
        if ((observacion ?? "").Trim().Length > 500) return "La observación admite hasta 500 letras.";
        if (lineas.Count == 0) return "La compra debe tener al menos una línea.";
        if (lineas.Count > MaxLineas) return $"La compra admite hasta {MaxLineas} líneas.";
        for (var i = 0; i < lineas.Count; i++)
        {
            var l = lineas[i];
            var d = (l.Descripcion ?? "").Trim();
            var m = d.Length < 3 ? "escriba la descripción."
                : d.Length > 80 ? "la descripción admite hasta 80 letras."
                : l.BienServicio is not (1 or 2) ? "indique si es un bien o un servicio."
                : l.Cantidad <= 0 ? "la cantidad debe ser mayor que cero."
                : l.Precio <= 0 ? "el precio debe ser mayor que cero."
                : l.Cantidad != Math.Round(l.Cantidad, 2) || l.Precio != Math.Round(l.Precio, 2) ? "la cantidad y el precio van con 2 decimales."
                : null;
            if (m is not null) return $"Línea {i + 1}: {m}";
        }
        var servicios = lineas.Any(l => l.BienServicio == 2);
        if (tipoIsr != "N" && !servicios) return "La retención de ISR solo se aplica a servicios y la compra no tiene ninguna línea de servicio.";
        // la DGII rechaza un E41 con una linea de servicio sin MontoISRRetenido
        if (tipoIsr == "N" && servicios)
            return "Los servicios de personas físicas llevan retención de ISR: elija 15 % (profesionales), 10 % (alquiler) o 3 % (técnicos). \"Sin retención\" es solo para compras de bienes.";
        return null;
    }

    public static string Xml(CompraInformal c, EmisorEcf e, DateTime fechaVencimiento, DateTime fechaHoraFirma)
    {
        if (c.Encf.Length != 13 || !c.Encf.StartsWith("E41")) throw new ErrorEcf($"La compra {c.Numero} no tiene un e-NCF E41 válido: {c.Encf}");
        if (c.Lineas.Count == 0) throw new ErrorEcf($"La compra {c.Encf} no tiene líneas.");
        var credito = c.FormaPago == "04";
        var x = new XmlDelphi();
        x.Abrir("ECF");
        x.Abrir("Encabezado");
        x.Nodo("Version", "1.0");
        // sin TipoIngresos; los precios se digitan sin ITBIS (IndicadorMontoGravado 0)
        x.Abrir("IdDoc");
        x.Nodo("TipoeCF", "41");
        x.Nodo("eNCF", c.Encf);
        x.Nodo("FechaVencimientoSecuencia", F(fechaVencimiento));
        x.Nodo("IndicadorMontoGravado", "0");
        if (credito)
        {
            x.Nodo("TipoPago", "2");
            x.Nodo("FechaLimitePago", F(c.FechaPago));
        }
        else x.Nodo("TipoPago", "1");
        x.Cerrar();
        x.Abrir("Emisor");
        x.Nodo("RNCEmisor", e.Rnc.Trim());
        x.Nodo("RazonSocialEmisor", e.RazonSocial.Trim());
        x.Nodo("DireccionEmisor", e.Direccion.Trim());
        x.Nodo("FechaEmision", F(c.Fecha));
        x.Cerrar();
        x.Abrir("Comprador");
        x.Nodo("RNCComprador", c.Cedula.Trim());
        x.Nodo("RazonSocialComprador", c.Proveedor.Trim());
        if (!string.IsNullOrWhiteSpace(c.Direccion)) x.Nodo("DireccionComprador", c.Direccion.Trim());
        x.Cerrar();
        // totales: solo los que tienen monto, en el orden del esquema
        x.Abrir("Totales");
        if (c.MontoGravado > 0)
        {
            x.Nodo("MontoGravadoTotal", M(c.MontoGravado));
            x.Nodo("MontoGravadoI1", M(c.MontoGravado));
        }
        if (c.MontoExento > 0) x.Nodo("MontoExento", M(c.MontoExento));
        if (c.MontoGravado > 0)
        {
            x.Nodo("ITBIS1", "18");
            x.Nodo("TotalITBIS", M(c.Itbis));
            x.Nodo("TotalITBIS1", M(c.Itbis));
        }
        x.Nodo("MontoTotal", M(c.Total));
        if (c.ItbisRetenido > 0) x.Nodo("TotalITBISRetenido", M(c.ItbisRetenido));
        if (c.IsrRetenido > 0) x.Nodo("TotalISRRetencion", M(c.IsrRetenido));
        x.Cerrar();
        x.Cerrar();   // Encabezado
        x.Abrir("DetallesItems");
        var n = 0;
        foreach (var l in c.Lineas)
        {
            x.Abrir("Item");
            x.Nodo("NumeroLinea", (++n).ToString(CultureInfo.InvariantCulture));
            x.Nodo("IndicadorFacturacion", l.Gravado ? "1" : "4");
            // el XSD del tipo 41 exige Retencion en TODAS las lineas: el indicador siempre, los montos si los hay
            x.Abrir("Retencion");
            x.Nodo("IndicadorAgenteRetencionoPercepcion", "1");
            if (l.ItbisRetenido > 0) x.Nodo("MontoITBISRetenido", M(l.ItbisRetenido));
            if (l.IsrRetenido > 0) x.Nodo("MontoISRRetenido", M(l.IsrRetenido));
            x.Cerrar();
            var d = l.Descripcion.Trim();
            x.Nodo("NombreItem", d.Length > 80 ? d[..80] : d);
            x.Nodo("IndicadorBienoServicio", l.BienServicio.ToString(CultureInfo.InvariantCulture));
            x.Nodo("CantidadItem", M(l.Cantidad));
            x.Nodo("UnidadMedida", "43");
            x.Nodo("PrecioUnitarioItem", l.Precio.ToString("0.0000", CultureInfo.InvariantCulture));
            x.Nodo("MontoItem", M(l.Monto));
            x.Cerrar();
        }
        x.Cerrar();
        x.Nodo("FechaHoraFirma", fechaHoraFirma.ToString("dd-MM-yyyy HH:mm:ss", CultureInfo.InvariantCulture));
        x.Cerrar();
        return x.Texto();
    }

    /// <summary>La compra vista como un e-CF (estado y QR): el comprador es el proveedor; el MontoTotal es el total con ITBIS.</summary>
    public static FacturaEcf ComoFactura(CompraInformal c) =>
        new(c.Encf, c.Numero, c.Fecha.Date, c.Cedula, c.Proveedor, c.MontoGravado + c.MontoExento, c.Itbis, 0, c.FormaPago == "04",
            c.Lineas.Select(l => new ItemEcf(l.Descripcion, l.Cantidad, l.Precio, l.Monto)).ToList(), c.Total);

    private static string F(DateTime d) => d.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
    private static string M(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture);
}
