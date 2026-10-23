using System.Globalization;

namespace HotelWeb.Core.Ecf;

/// <summary>Un gasto menor (HotelGastoMenor, MIG_14 de Delphi).</summary>
/// <param name="BienServicio">1 = bien, 2 = servicio (IndicadorBienoServicio).</param>
/// <param name="FormaPago">Codigo del 606: 01 efectivo, 02 cheque / transferencia, 03 tarjeta, 04 credito.</param>
/// <param name="TipoGasto">Tipo de bienes y servicios comprados del 606 (01 a 11).</param>
public sealed record GastoMenor(string Encf, long Numero, DateTime Fecha, string Descripcion, int BienServicio, decimal Cantidad, decimal Precio,
    decimal Total, string FormaPago, string TipoGasto, string Beneficiario, string? Cedula, string Detalle, string? Usuario);

/// <summary>
/// El Comprobante Electronico para Gastos Menores (E43), como CrearGastoMenorXml de Delphi (Hotel\Source\UFrmGastoMenor.pas
/// y Comunes\uCreateXml.pas, aceptado por la DGII en Sanchez, Maplasa y Massoti). Norma General 05-19, Art. 8: lo emite el
/// hotel para sustentar pagos de su personal por consumibles, pasajes, estacionamiento, peajes... Formato v1.0 del tipo 43:
/// sin comprador, sin IndicadorMontoGravado ni TipoIngresos, todo exento (IndicadorFacturacion 4). Siempre el ECF completo.
/// </summary>
public static class GastoMenorEcf
{
    public const int MaxNombreItem = 80;

    public static readonly IReadOnlyDictionary<string, string> FormasPago = new Dictionary<string, string>
    {
        ["01"] = "Efectivo",
        ["02"] = "Cheque / transferencia / depósito",
        ["03"] = "Tarjeta de crédito / débito",
        ["04"] = "A crédito",
    };

    public static readonly IReadOnlyDictionary<string, string> TiposGasto = new Dictionary<string, string>
    {
        ["01"] = "Gastos de personal",
        ["02"] = "Gastos por trabajos, suministros y servicios",
        ["03"] = "Arrendamientos",
        ["04"] = "Gastos de activos fijos",
        ["05"] = "Gastos de representación",
        ["06"] = "Otras deducciones admitidas",
        ["07"] = "Gastos financieros",
        ["08"] = "Gastos extraordinarios",
        ["09"] = "Compras y gastos que forman parte del costo de venta",
        ["10"] = "Adquisiciones de activos",
        ["11"] = "Gastos de seguros",
    };

    /// <summary>Las mismas revisiones que Stp_CrearGastoMenor, para avisar antes de pedir el numero.</summary>
    public static string? Validar(string? descripcion, int bienServicio, decimal cantidad, decimal precio, string? formaPago, string? tipoGasto,
        string? beneficiario, string? detalle, DateTime fecha, DateTime hoy)
    {
        if ((descripcion ?? "").Trim().Length < 3) return "Escriba la descripción del gasto.";
        if ((descripcion ?? "").Trim().Length > MaxNombreItem) return $"La descripción va hasta {MaxNombreItem} letras.";
        if (bienServicio is not (1 or 2)) return "Indique si el gasto es un bien o un servicio.";
        if (cantidad <= 0) return "La cantidad debe ser mayor que cero.";
        if (precio <= 0) return "El precio debe ser mayor que cero.";
        if (cantidad != Math.Round(cantidad, 2) || precio != Math.Round(precio, 2)) return "La cantidad y el precio van con 2 decimales.";
        if (!FormasPago.ContainsKey(formaPago ?? "")) return "Forma de pago no válida.";
        if (!TiposGasto.ContainsKey(tipoGasto ?? "")) return "Tipo de gasto (606) no válido.";
        if ((beneficiario ?? "").Trim().Length < 3) return "Escriba a nombre de quién se hace el pago.";
        var d = (detalle ?? "").Trim();
        if (d.Length < 10) return "Explique qué se recibió y por qué se paga (mínimo 10 letras).";
        if (d.Length > 500) return "El detalle va hasta 500 letras.";
        if (fecha.Date > hoy.Date) return "La fecha del gasto no puede ser futura.";
        return null;
    }

    public static decimal Total(decimal cantidad, decimal precio) => Math.Round(cantidad * precio, 2, MidpointRounding.AwayFromZero);

    public static string Xml(GastoMenor g, EmisorEcf e, DateTime fechaVencimiento, DateTime fechaHoraFirma)
    {
        if (g.Encf.Length != 13 || !g.Encf.StartsWith("E43")) throw new ErrorEcf($"El gasto {g.Numero} no tiene un e-NCF E43 válido: {g.Encf}");
        if (g.Total <= 0) throw new ErrorEcf($"El gasto {g.Encf} no tiene monto.");
        var x = new XmlDelphi();
        x.Abrir("ECF");
        x.Abrir("Encabezado");
        x.Nodo("Version", "1.0");
        x.Abrir("IdDoc");
        x.Nodo("TipoeCF", "43");
        x.Nodo("eNCF", g.Encf);
        x.Nodo("FechaVencimientoSecuencia", F(fechaVencimiento));
        x.Nodo("TipoPago", "1");
        x.Cerrar();
        x.Abrir("Emisor");
        x.Nodo("RNCEmisor", e.Rnc.Trim());
        x.Nodo("RazonSocialEmisor", e.RazonSocial.Trim());
        x.Nodo("NombreComercial", e.RazonSocial.Trim());
        x.Nodo("DireccionEmisor", e.Direccion.Trim());
        x.Nodo("NumeroFacturaInterna", g.Numero.ToString(CultureInfo.InvariantCulture));
        x.Nodo("FechaEmision", F(g.Fecha));
        x.Cerrar();
        x.Abrir("Totales");
        x.Nodo("MontoExento", M(g.Total));
        x.Nodo("MontoTotal", M(g.Total));
        x.Cerrar();
        x.Cerrar();   // Encabezado
        x.Abrir("DetallesItems");
        x.Abrir("Item");
        x.Nodo("NumeroLinea", "1");
        x.Nodo("IndicadorFacturacion", "4");   // exento: obligatorio en el tipo 43
        var nombre = g.Descripcion.Trim();
        x.Nodo("NombreItem", nombre.Length > MaxNombreItem ? nombre[..MaxNombreItem] : nombre);
        x.Nodo("IndicadorBienoServicio", g.BienServicio.ToString(CultureInfo.InvariantCulture));
        x.Nodo("DescripcionItem", g.Detalle.Trim());
        x.Nodo("CantidadItem", M(g.Cantidad));
        x.Nodo("UnidadMedida", "43");
        x.Nodo("PrecioUnitarioItem", g.Precio.ToString("0.0000", CultureInfo.InvariantCulture));
        x.Nodo("MontoItem", M(g.Total));
        x.Cerrar();
        x.Cerrar();
        x.Nodo("FechaHoraFirma", fechaHoraFirma.ToString("dd-MM-yyyy HH:mm:ss", CultureInfo.InvariantCulture));
        x.Cerrar();
        return x.Texto();
    }

    /// <summary>El gasto visto como un e-CF (para el estado y el QR): sin comprador, todo exento.</summary>
    public static FacturaEcf ComoFactura(GastoMenor g) =>
        new(g.Encf, g.Numero, g.Fecha.Date, null, g.Beneficiario, g.Total, 0, 0, false, [new ItemEcf(g.Descripcion, g.Cantidad, g.Precio, g.Total)], g.Total);

    private static string F(DateTime d) => d.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
    private static string M(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture);
}
