using HotelWeb.Core.Rack;

namespace HotelWeb.Core.Hospedaje;

/// <summary>Una linea que se copia a la cuenta destino.</summary>
public sealed record LineaTransferida(string Servicio, string Descripcion, decimal Precio, decimal Importe);

/// <summary>
/// Lo que hace la transferencia: <paramref name="Destino"/> son las lineas que se agregan a la cuenta destino,
/// <paramref name="PagosOrigen"/> las secuencias de pagos del origen que pasan (quedan inactivas en el origen) y
/// <paramref name="Cierre"/> el importe de la linea que deja el origen en 0.
/// </summary>
public sealed record PlanTransferencia(decimal Cargos, decimal PayOut, decimal Pagos, IReadOnlyList<LineaTransferida> Destino,
    IReadOnlyList<int> PagosOrigen, decimal Cierre);

/// <summary>
/// Transferencia de la cuenta a otra habitacion OCUPADA. Delphi (Stp_TransferiraOcupada) pasa SUM(Importe) de todo en
/// una linea: mezcla cargos sin impuestos con pagos con impuestos, y cierra el origen con x1.28 solo si el cliente lleva
/// ITBIS (un cliente solo con propina, o E44, no queda en 0 y no puede hacer check-out). Decisiones del usuario
/// (2026-10-23): los cargos pasan en una linea (servicio 9, sin impuestos: el destino les pone los de su cliente, como
/// ya sale en el e-CF), el PAY-OUT en otra (sin impuestos) y los pagos y adelantos pasan como pagos; el origen queda en
/// 0 y se le hace el check-out en el mismo paso (sin e-NCF).
/// </summary>
public static class ReglasTransferencia
{
    public const string ServicioTransferencia = "9";
    public const string ServicioPayOut = "7";

    public static PlanTransferencia Planear(IReadOnlyList<(LineaCuenta Linea, string Servicio)> origen, int habOrigen, long facturaOrigen,
        bool llevaItbis, bool llevaPropina, bool regimenEspecial)
    {
        var cargos = origen.Where(x => x.Linea.EsCargo).Sum(x => x.Linea.Importe);
        var payOut = origen.Where(x => x.Linea.Activo && x.Linea.EsPayOut).Sum(x => x.Linea.Importe);
        var pagos = origen.Where(x => x.Linea.Activo && x.Linea.Importe < 0 && !x.Linea.EsPayOut).ToList();

        var destino = new List<LineaTransferida>();
        if (cargos != 0)
            destino.Add(new LineaTransferida(ServicioTransferencia, Corto($"Transf.de Hab.#{habOrigen} Fact.#{facturaOrigen}"), cargos, cargos));
        if (payOut != 0)
            destino.Add(new LineaTransferida(ServicioPayOut, Corto($"PAY-OUT Transf.de Hab.#{habOrigen}"), payOut, payOut));
        foreach (var (l, s) in pagos)
            destino.Add(new LineaTransferida(s, Corto($"{l.Descripcion} (Hab.#{habOrigen})"), l.Importe, l.Importe));

        // el origen: sus pagos se van (inactivos) y una linea negativa cancela los cargos con SUS impuestos y el PAY-OUT,
        // para que dbo.BalanceActualHabitacion de 0 (Stp_CHECKOUT lo exige)
        var t = CalculoCuenta.Calcular(origen.Select(x => x.Linea).Where(l => !(l.Activo && l.Importe < 0 && !l.EsPayOut)),
            llevaItbis, llevaPropina, regimenEspecial);
        var cierre = -Math.Round(t.Cargos + t.Itbis + t.Propina + t.PagosYPayOut, 2, MidpointRounding.AwayFromZero);
        return new PlanTransferencia(cargos, payOut, pagos.Sum(x => x.Linea.Importe), destino,
            pagos.Select(x => x.Linea.Secuencia).ToList(), cierre);
    }

    /// <summary>Motivo por el que no se puede transferir, o null.</summary>
    public static string? Validar(int desde, int hasta, bool origenTransferida, bool destinoTransferida, bool hayAlgo)
    {
        if (desde == hasta) return "No puede transferir a la misma habitacion.";
        if (origenTransferida) return "Esa cuenta ya fue transferida.";
        if (destinoTransferida) return $"La cuenta de la {hasta} esta transferida: escoja otra habitacion.";
        if (!hayAlgo) return "La cuenta no tiene cargos ni pagos que transferir.";
        return null;
    }

    private static string Corto(string s) => s.Length > 50 ? s[..50] : s;
}
