using HotelWeb.Core.Ecf;
using HotelWeb.Core.Identificacion;

namespace HotelWeb.Core.Hospedaje;

/// <summary>Un tipo de comprobante del hotel (ComprobanteFiscal.RecordKey_id = CxcCliente.CodigoCompFiscal).</summary>
public sealed record TipoComprobante(int Id, string Codigo, string Nombre);

/// <summary>
/// El check-out con e-CF, como UfrmfacturaModificada.RzRapidFireButton3Click + UFrmMntComprobanteFiscal.SpeedButton2Click:
/// todo lo que la DGII rechazaria se revisa ANTES de tomar el e-NCF (si no, el numero queda gastado).
/// </summary>
public static class ReglasCheckout
{
    public static readonly IReadOnlyList<TipoComprobante> Tipos = new[]
    {
        new TipoComprobante(1, "E31", "Credito fiscal"),
        new TipoComprobante(2, "E32", "Consumo"),
        new TipoComprobante(13, "E44", "Regimenes especiales"),
        new TipoComprobante(14, "E45", "Gubernamental"),
    };

    /// <summary>Sin RNC (vacio o ceros) solo hay consumo (E32), como dblcktipocomprobanteEnter.</summary>
    public static IReadOnlyList<TipoComprobante> Permitidos(string? rnc) =>
        IdentificacionRD.SinIdentificacion(rnc) ? Tipos.Where(t => t.Id == 2).ToList() : Tipos;

    /// <summary>El de la ficha del cliente si se puede; si no, consumo.</summary>
    public static int Sugerido(int? codigoCliente, string? rnc) =>
        Permitidos(rnc).FirstOrDefault(t => t.Id == codigoCliente)?.Id ?? 2;

    /// <summary>Motivo por el que no se puede tomar el e-NCF de ese tipo, o null.</summary>
    public static string? Validar(int tipo, string? rnc, string? nombreCliente, decimal total, decimal itbis)
    {
        var t = Tipos.FirstOrDefault(x => x.Id == tipo);
        if (t is null) return "Escoja el tipo de comprobante.";
        var sin = IdentificacionRD.SinIdentificacion(rnc);
        var clase = IdentificacionRD.Determinar(sin ? "" : rnc, out _);
        switch (tipo)
        {
            case 2 when total >= EcfHotel.LimiteResumen:
                // E32 de 250,000 o mas: RNC o cedula (o pasaporte si es extranjero) y nombre (Formato e-CF v1.0)
                if (sin || clase == TipoIdentificacion.Invalido)
                    return "Consumo de 250,000 o mas: digite el RNC o la cedula del cliente (el pasaporte si es extranjero).";
                if (string.IsNullOrWhiteSpace(nombreCliente)) return "Consumo de 250,000 o mas: falta el nombre o razon social del cliente.";
                break;
            case 2:
                if (!sin && clase == TipoIdentificacion.Invalido) return $"La identificacion {rnc} no es valida: corrijala o dejela en blanco.";
                break;
            default:
                if (sin) return $"{t.Codigo} necesita el RNC del cliente.";
                if (tipo == 1 && !IdentificacionRD.EsRncOCedulaValido(rnc)) return "Credito fiscal (E31): el RNC o cedula no es valido.";
                if (clase == TipoIdentificacion.Invalido) return $"La identificacion {rnc} no es valida.";
                if (tipo == 13 && itbis > 0) return "Regimen especial (E44) no puede llevar ITBIS: revise el cliente.";
                break;
        }
        return null;
    }

    /// <summary>Lo que impide el check-out antes de escoger comprobante (Stp_CHECKOUT exige balance 0), o null.</summary>
    public static string? PuedeCerrar(decimal saldo, decimal totalEncabezado, bool hayCargos, bool transferida)
    {
        if (saldo != 0) return $"Check-out solo con el balance en 0.00: balance actual {saldo:N2}.";
        if (!transferida && totalEncabezado <= 0 && hayCargos)
            return "Los totales de la factura estan en cero y tiene cargos: vuelva a abrir la cuenta e intente de nuevo.";
        return null;
    }
}
