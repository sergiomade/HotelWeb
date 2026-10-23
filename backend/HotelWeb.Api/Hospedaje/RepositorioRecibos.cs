using System.Data;
using Dapper;
using HotelWeb.Api.Catalogos;
using HotelWeb.Api.Datos;
using HotelWeb.Core.Hospedaje;

namespace HotelWeb.Api.Hospedaje;

public sealed record PagoVista(int FormaPago, string FormaPagoNombre, string Descripcion, decimal Monto, decimal? MontoUs,
    string? NoCheque, string? Banco);

public sealed record Recibo(long Numero, DateTime Fecha, int Habitacion, string Nombre, string? Concepto, decimal Monto,
    string Estatus, bool Posteado, string? Usuario, IReadOnlyList<PagoVista> Pagos);

/// <summary>
/// Recibos de pago a la cuenta de la habitacion (HotelReciboIngreso + HotelReciboDetalle), como UFrmMntRecibos de
/// Delphi: numero = mayor + 1, y STP_CARGARPAGOAHABITACION pasa cada forma de pago a la cuenta. No hacen asiento: el
/// cuadre de caja (STP_CuadreDeCaja) los toma (Posteado = 0) y los marca. Anular (decision del usuario 2026-10-22):
/// estatus Anulado, el pago sale de la cuenta y Posteado = 1 para que el cuadre no lo cuente. (El "Anular" de Delphi
/// llama a Stp_AnularRecibodeIngreso, que trabaja sobre IngRecibos de cuentas por cobrar: no anula el del hotel.)
/// </summary>
public sealed class RepositorioRecibos
{
    private readonly Conexiones _conexiones;

    public RepositorioRecibos(Conexiones conexiones) => _conexiones = conexiones;

    private static DbString A(string? v, int largo) => new() { Value = v, IsAnsi = true, Length = largo };
    private static DbString Usuario(string u) => A(u.Length > 10 ? u[..10] : u, 10);

    private sealed class FilaRecibo
    {
        public decimal NumeroRecibo { get; set; }
        public DateTime Fecha { get; set; }
        public decimal Habitacion { get; set; }
        public string? Nombre { get; set; }
        public string? Concepto { get; set; }
        public decimal Monto { get; set; }
        public string? Estatus { get; set; }
        public bool? Posteado { get; set; }
        public string? Usuario { get; set; }
        public decimal Factura_id { get; set; }
    }

    private sealed class FilaPago
    {
        public decimal NumeroRecibo { get; set; }
        public decimal Monedas_id { get; set; }
        public string? NombreMoneda { get; set; }
        public string? Descripcion { get; set; }
        public decimal Monto { get; set; }
        public decimal? MontoUs { get; set; }
        public string? NoCheque { get; set; }
        public string? BancoTc { get; set; }
    }

    private const string SelRecibo = """
        SELECT NumeroRecibo, Fecha, Habitacion, RTRIM(Nombre) AS Nombre, CAST(Concepto AS varchar(500)) AS Concepto, Monto,
               RTRIM(Estatus) AS Estatus, Posteado, RTRIM(Usuario) AS Usuario, Factura_id
        FROM HotelReciboIngreso
        """;

    private static List<Recibo> Armar(IDbConnection cn, IDbTransaction? tx, IEnumerable<FilaRecibo> filas)
    {
        var lista = filas.ToList();
        if (lista.Count == 0) return new();
        var numeros = lista.Select(r => r.NumeroRecibo).ToArray();
        var pagos = cn.Query<FilaPago>("""
            SELECT d.NumeroRecibo, d.Monedas_id, RTRIM(m.NombreMoneda) AS NombreMoneda, RTRIM(d.Descripcion) AS Descripcion, d.Monto,
                   d.MontoUs, RTRIM(d.NoCheque) AS NoCheque, RTRIM(d.BancoTc) AS BancoTc
            FROM HotelReciboDetalle d LEFT JOIN ConMonedas m ON m.Monedas_id = d.Monedas_id
            WHERE d.NumeroRecibo IN @numeros
            """, new { numeros }, tx).ToList();
        return lista.Select(r => new Recibo((long)r.NumeroRecibo, r.Fecha, (int)r.Habitacion, r.Nombre ?? "", r.Concepto?.Trim(),
            r.Monto, r.Estatus ?? "", r.Posteado == true, r.Usuario,
            pagos.Where(p => p.NumeroRecibo == r.NumeroRecibo).Select(p => new PagoVista((int)p.Monedas_id,
                p.NombreMoneda ?? ((int)p.Monedas_id).ToString(), p.Descripcion ?? "", p.Monto, p.MontoUs, p.NoCheque, p.BancoTc)).ToList()))
            .ToList();
    }

    /// <summary>Los recibos de la estadia actual de la habitacion (los que apuntan a la ClaveUnica de su cuenta).</summary>
    public IReadOnlyList<Recibo> DeLaCuenta(int habitacion)
    {
        using var cn = _conexiones.Hotel();
        var clave = cn.ExecuteScalar<decimal?>(
            "SELECT TOP 1 ClaveUnica FROM HotelFactura WHERE Habitacion_id = @habitacion ORDER BY Factura_id DESC", new { habitacion });
        if (clave is null) return Array.Empty<Recibo>();
        return Armar(cn, null, cn.Query<FilaRecibo>(SelRecibo + " WHERE Factura_id = @clave AND Habitacion = @habitacion ORDER BY NumeroRecibo",
            new { clave, habitacion }));
    }

    private sealed class Cuenta
    {
        public decimal Factura_id { get; set; }
        public decimal ClaveUnica { get; set; }
        public decimal? Cliente_id { get; set; }
        public int? TipoHabitacion { get; set; }
        public decimal? HabitacionReserva_id { get; set; }
        public bool Transferida { get; set; }
        public string? Nombre { get; set; }
    }

    public Recibo Crear(int habitacion, string? concepto, IReadOnlyList<PagoRecibo> pagos, string usuario)
    {
        var lista = pagos.Select(ReglasRecibo.Normalizar).ToList();
        concepto = string.IsNullOrWhiteSpace(concepto) ? ReglasRecibo.ConceptoPorOmision : concepto.Trim();
        if (ReglasRecibo.Validar(lista, concepto) is { } motivo) throw new ErrorCatalogo(motivo);

        using var cn = _conexiones.Hotel();
        cn.Open();
        using var tx = cn.BeginTransaction();
        var c = cn.QueryFirstOrDefault<Cuenta>("""
            SELECT TOP 1 f.Factura_id, ISNULL(NULLIF(f.ClaveUnica, 0), f.Factura_id) AS ClaveUnica, f.Cliente_id, f.TipoHabitacion,
                   f.HabitacionReserva_id, CAST(ISNULL(f.Transferida, 0) AS bit) AS Transferida,
                   COALESCE(NULLIF(RTRIM(cl.Nombre), ''), NULLIF(RTRIM(f.Huesped), '')) AS Nombre
            FROM HotelFactura f WITH (UPDLOCK) LEFT JOIN CxcCliente cl ON cl.NumeroCliente = f.Cliente_id
            WHERE f.Habitacion_id = @habitacion ORDER BY f.Factura_id DESC
            """, new { habitacion }, tx) ?? throw new ErrorCatalogo($"La habitacion {habitacion} no tiene cuenta abierta.");
        if (c.Transferida) throw new ErrorCatalogo("La cuenta esta transferida a otra habitacion: el pago va a la otra cuenta.");
        foreach (var f in lista.Select(p => p.FormaPago).Distinct())
            if (cn.ExecuteScalar<int>("SELECT COUNT(*) FROM ConMonedas WHERE Monedas_id = @f", new { f }, tx) == 0)
                throw new ErrorCatalogo($"No existe la forma de pago {f}.");

        // mayor + 1, como Delphi (Qmaximo); UPDLOCK para que dos a la vez no tomen el mismo numero
        var numero = cn.ExecuteScalar<long>(
            "SELECT ISNULL(MAX(NumeroRecibo), 0) + 1 FROM HotelReciboIngreso WITH (UPDLOCK, HOLDLOCK)", transaction: tx);
        // como ReciboNewRecord + Grabar de Delphi: fecha de hoy, Activo, EntradaRec_id 1, la clave de la estadia en Factura_id
        cn.Execute("""
            INSERT INTO HotelReciboIngreso (NumeroRecibo, Fecha, Habitacion, Habitacion_id, Cliente_id, EntradaRec_id, Factura_id,
                   FacturaWalking_id, Reserva_id, Nombre, Concepto, Monto, Estatus, Usuario, FechaAdicion, Posteado)
            VALUES (@numero, CAST(GETDATE() AS date), @habitacion, @tipo, @cliente, 1, @clave, NULL, @reserva, @nombre, @concepto,
                    @total, 'Activo', @usuario, CAST(GETDATE() AS date), 0)
            """, new
        {
            numero, habitacion, tipo = c.TipoHabitacion ?? 0, cliente = c.Cliente_id ?? 0, clave = c.ClaveUnica,
            reserva = c.HabitacionReserva_id, nombre = A((c.Nombre ?? "").Length > 60 ? c.Nombre![..60] : c.Nombre ?? "", 60),
            concepto = A(concepto, -1), total = ReglasRecibo.Total(lista), usuario = Usuario(usuario),
        }, tx);
        foreach (var p in lista)
            cn.Execute("""
                INSERT INTO HotelReciboDetalle (NumeroRecibo, Monedas_id, Descripcion, Monto, MontoUs, NoCheque, Fechacheque, BancoTc)
                VALUES (@numero, @forma, @descripcion, @monto, @us, @cheque, @fechaCheque, @banco)
                """, new
            {
                numero, forma = p.FormaPago, descripcion = A(p.Descripcion, 40), monto = p.Monto,
                us = p.FormaPago == ReglasRecibo.Dolares ? p.MontoUs : null, cheque = A(p.NoCheque, 15), fechaCheque = p.FechaCheque,
                banco = A(p.Banco, 50),
            }, tx);

        // el procedimiento de Delphi pasa cada forma de pago a la cuenta de la habitacion
        cn.Execute("STP_CARGARPAGOAHABITACION", new { NumeroRecibo = numero, Usuario = Usuario(usuario) }, tx,
            commandType: CommandType.StoredProcedure);
        RepositorioCuentas.Recalcular(cn, tx, habitacion);
        var r = Armar(cn, tx, cn.Query<FilaRecibo>(SelRecibo + " WHERE NumeroRecibo = @numero", new { numero }, tx)).Single();
        tx.Commit();
        return r;
    }

    public Recibo Anular(long numero, string usuario)
    {
        using var cn = _conexiones.Hotel();
        cn.Open();
        using var tx = cn.BeginTransaction();
        var r = cn.QueryFirstOrDefault<FilaRecibo>(SelRecibo + " WITH (UPDLOCK) WHERE NumeroRecibo = @numero", new { numero }, tx)
                ?? throw new ErrorCatalogo($"No existe el recibo {numero}.");
        var factura = cn.ExecuteScalar<decimal?>("""
            SELECT TOP 1 Factura_id FROM HotelFactura WITH (UPDLOCK)
            WHERE Habitacion_id = @hab AND ISNULL(NULLIF(ClaveUnica, 0), Factura_id) = @clave
            """, new { hab = r.Habitacion, clave = r.Factura_id }, tx);
        if (ReglasRecibo.ValidarAnular(r.Estatus, r.Posteado == true, factura is not null) is { } motivo) throw new ErrorCatalogo(motivo);

        // las lineas que puso el recibo en la cuenta: inactivas y en 0, como se anula un cargo
        var prefijos = ReglasRecibo.PrefijosEnCuenta(numero);
        var n = cn.Execute("""
            UPDATE HotelFacturaDetalle SET Activo = 0, Importe = 0
            WHERE Factura_id = @factura AND Activo = 1 AND (Descripcion LIKE @p1 OR Descripcion LIKE @p2)
            """, new { factura, p1 = A(prefijos[0] + "%", 50), p2 = A(prefijos[1] + "%", 50) }, tx);
        if (n == 0) throw new ErrorCatalogo("No se encontraron en la cuenta las lineas de ese recibo: no se anulo (revise la cuenta).");
        // Posteado = 1: el cuadre de caja (STP_CuadreDeCaja) toma los Posteado = 0 sin mirar el estatus
        cn.Execute("UPDATE HotelReciboIngreso SET Estatus = 'Anulado', Posteado = 1 WHERE NumeroRecibo = @numero", new { numero }, tx);
        RepositorioCuentas.Recalcular(cn, tx, (int)r.Habitacion);
        var vista = Armar(cn, tx, cn.Query<FilaRecibo>(SelRecibo + " WHERE NumeroRecibo = @numero", new { numero }, tx)).Single();
        tx.Commit();
        return vista;
    }
}
