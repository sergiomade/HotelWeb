using HotelWeb.Core.Rack;

namespace HotelWeb.Core.Hospedaje;

/// <summary>Un cargo que se agrega a la cuenta de la habitacion (HotelFacturaDetalle), como el boton de Delphi.</summary>
public sealed record NuevoCargo(int Servicio, string Descripcion, int Cantidad, decimal Precio)
{
    public decimal Importe => Math.Round(Cantidad * Precio, 2);
}

/// <summary>Totales del encabezado de la cuenta (HotelFactura), como CalcularTotales de Delphi.</summary>
public sealed record TotalesEncabezado(decimal SubTotal, decimal Itbis, decimal Propina, decimal Total);

/// <summary>
/// Reglas de los cargos a la cuenta (UfrmfacturaModificada de Delphi). Agregar: servicio del catalogo, descripcion,
/// cantidad y precio distinto de cero; importe = cantidad x precio. Anular: la linea queda inactiva y en 0 (como
/// desmarcar Activo en Delphi). Los servicios que el sistema pone por su cuenta no se agregan a mano, y las lineas que
/// tienen otro documento detras (pagos, adelantos, transferencias, cafeteria) no se anulan aqui.
/// </summary>
public static class ReglasCargo
{
    public const int LargoDescripcion = 50;   // varchar(50)

    /// <summary>Servicios que pone el sistema (transferencia, cafeteria, adelantos): no se cargan a mano.</summary>
    public static readonly IReadOnlySet<int> SoloDelSistema = new HashSet<int> { 3, 7, 11 };

    public static NuevoCargo Normalizar(NuevoCargo c) =>
        c with { Descripcion = (c.Descripcion ?? "").Trim().ToUpperInvariant() };

    public static string? Validar(NuevoCargo c, bool cuentaTransferida)
    {
        if (cuentaTransferida) return "La cuenta esta transferida a otra habitacion: no recibe cargos.";
        if (SoloDelSistema.Contains(c.Servicio)) return "Ese servicio lo pone el sistema (transferencias, cafeteria, adelantos): no se carga a mano.";
        if (c.Descripcion.Length == 0) return "Falta la descripcion del cargo.";
        if (c.Descripcion.Length > LargoDescripcion) return $"La descripcion lleva a lo sumo {LargoDescripcion} letras.";
        if (c.Cantidad < 1 || c.Cantidad > 999) return "La cantidad va de 1 a 999.";
        if (c.Precio <= 0) return "El cargo no tiene precio.";
        if (c.Precio > 99_999_999m) return "El precio es demasiado grande.";
        if (decimal.Round(c.Precio, 2) != c.Precio) return "El precio lleva 2 decimales.";
        return null;
    }

    /// <summary>Motivo por el que una linea no se anula desde aqui, o null.</summary>
    public static string? ValidarAnular(LineaCuenta l)
    {
        if (!l.Activo) return "Ese cargo ya esta anulado.";
        if (l.Importe < 0) return "Los pagos y adelantos no se anulan aqui (se anula su recibo o su adelanto).";
        var d = l.Descripcion.ToUpperInvariant();
        if (d.StartsWith("TRANSF")) return "Los cargos de una transferencia no se anulan aqui.";
        if (d.StartsWith("CAFETERIA - FACTURA")) return "Los consumos de la cafeteria se corrigen con una nota de credito de la cafeteria.";
        if (l.EsPayOut) return "Los PAY-OUT se corrigen con su recibo.";
        return null;
    }

    /// <summary>
    /// Totales del encabezado con las reglas del saldo (CalculoCuenta: ITBIS 18 % salvo E44, propina 10 %): SubTotal = los
    /// cargos, Total = lo que queda por pagar redondeado a pesos, como el Round de CalcularTotales de Delphi.
    /// </summary>
    public static TotalesEncabezado Encabezado(IEnumerable<LineaCuenta> lineas, bool itbis, bool propina, bool regimenEspecial)
    {
        var lista = lineas.ToList();
        var t = CalculoCuenta.Calcular(lista, itbis, propina, regimenEspecial);
        // Delphi guarda ITBIS y propina TRUNCADOS a 2 decimales (2734.37 x 0.18 = 492.1866 -> 492.18): igual aqui, para
        // que la web deje en HotelFactura los mismos numeros que Delphi. El Total (saldo a pesos) no cambia.
        static decimal Truncar(decimal v) => Math.Truncate(v * 100) / 100;
        var itb = itbis && !regimenEspecial ? Truncar(t.Cargos * CalculoCuenta.PorcItbis) : 0m;
        var pro = propina ? Truncar(t.Cargos * CalculoCuenta.PorcPropina) : 0m;
        return new TotalesEncabezado(t.Cargos, itb, pro, t.Saldo);
    }
}
