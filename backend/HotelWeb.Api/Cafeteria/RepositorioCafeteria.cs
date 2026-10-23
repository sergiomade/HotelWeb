using System.Data;
using Dapper;
using HotelWeb.Api.Catalogos;
using HotelWeb.Api.Datos;
using HotelWeb.Api.Ecf;
using HotelWeb.Core.Cafeteria;
using HotelWeb.Core.Ecf;

namespace HotelWeb.Api.Cafeteria;

public enum EstadoMesa { Libre, Abierta, PreCuenta }

public sealed record MesaCafeteria(int Mesa, string Descripcion, int X, int Y, EstadoMesa Estado, long? Factura, decimal Total,
    int Lineas, int? Habitacion, DateTime? Abierta);

public sealed record ArticuloCafeteria(string Referencia, string Descripcion, int Familia, string NombreFamilia, decimal Precio,
    decimal Precio2, int Fraccion);

public sealed record CuentaMesa(int Mesa, string NombreMesa, long Factura, DateTime Fecha, long Cliente, string? NombreCliente,
    int? Habitacion, bool ConItbis, bool ConPropina, IReadOnlyList<LineaMesa> Lineas, TotalesMesa Totales, bool PreCuentaAlDia,
    decimal TasaDolar, DateTime Abierta);

public sealed record PreCuentaImpresa(EmisorEcf Emisor, CuentaMesa Cuenta, DateTime Hora, string? Usuario);

/// <summary>
/// La cuenta abierta de cada mesa de la cafeteria (etapa 8a), sobre las tablas de Delphi (RestaurantHotel):
/// FaFactura (EstatusFactura_id 1) y FaFacturaDetalle. Cada cambio vuelve a totalizar el encabezado, como
/// CalcularTotales + Salvar. Todo bajo un candado (sp_getapplock) para que dos cajas no se pisen.
/// </summary>
public sealed class RepositorioCafeteria
{
    private const string Candado = "HotelWeb_Cafeteria";
    private const int Abierta = 1;
    private readonly Conexiones _conexiones;

    public RepositorioCafeteria(Conexiones conexiones) => _conexiones = conexiones;

    private static DbString A(string? v, int largo) => new() { Value = v, IsAnsi = true, Length = largo };
    private static DbString U(string u) => A(u.Length > 10 ? u[..10] : u, 10);

    public IReadOnlyList<MesaCafeteria> Mesas()
    {
        using var cn = _conexiones.Hotel();
        return cn.Query<(int Mesa, string Descripcion, int X, int Y, int? Factura, decimal? Total, int? Lineas, int? Impresas, int? Habitacion,
                DateTime? Abierta)>("""
            SELECT m.Mesa_id, RTRIM(m.Descripcion), m.ValorX, m.ValorY, f.InvFactura, f.Total,
                   (SELECT COUNT(*) FROM FaFacturaDetalle d WHERE d.InvFactura = f.InvFactura),
                   (SELECT COUNT(*) FROM FaFacturaDetalle d WHERE d.InvFactura = f.InvFactura AND d.Impreso = 1),
                   NULLIF(f.Habitacion_id, 0), f.FechaAdicion
            FROM FaMesa m WITH (NOLOCK)
            LEFT JOIN FaFactura f WITH (NOLOCK) ON f.Mesa_id = m.Mesa_id AND f.EstatusFactura_id = 1
            WHERE m.Estatus = 1
            ORDER BY m.Mesa_id
            """).Select(x => new MesaCafeteria(x.Mesa, x.Descripcion, x.X, x.Y,
                x.Factura is null || (x.Lineas ?? 0) == 0 ? EstadoMesa.Libre
                : x.Impresas == x.Lineas ? EstadoMesa.PreCuenta : EstadoMesa.Abierta,
                x.Factura, x.Total ?? 0, x.Lineas ?? 0, x.Habitacion, x.Abierta)).ToList();
    }

    /// <summary>Los articulos que se venden (con precio para el cliente), por familia, como Stp_MaestrodeArticulos.</summary>
    public IReadOnlyList<ArticuloCafeteria> Articulos(long cliente = 1)
    {
        using var cn = _conexiones.Hotel();
        return cn.Query<(string Referencia, string Descripcion, decimal? Familia, string? Nombre, decimal Precio, decimal Precio2, decimal? Fraccion)>("""
            SELECT * FROM (
                SELECT RTRIM(a.Referencia) Referencia, RTRIM(a.Descripcion) Descripcion, a.NumeroFamilia, RTRIM(f.Familia) Familia,
                       dbo.Fn_Precio1(@cliente, a.Referencia, 1) Precio, dbo.Fn_Precio1(@cliente, a.Referencia, 2) Precio2, a.Fraccion,
                       f.Orden
                FROM InvArticulo a WITH (NOLOCK) LEFT JOIN InvFamilia f WITH (NOLOCK) ON f.NumeroFamilia = a.NumeroFamilia
                WHERE a.Referencia NOT IN ('00', '**')) x
            WHERE x.Precio >= 1 OR (x.Fraccion > 1 AND x.Precio2 >= 1)
            ORDER BY x.Orden, x.Familia, x.Descripcion
            """, new { cliente = (decimal)cliente })
            .Select(x => new ArticuloCafeteria(x.Referencia, x.Descripcion, (int)(x.Familia ?? 0), x.Nombre ?? "OTROS", x.Precio, x.Precio2,
                (int)(x.Fraccion ?? 1))).ToList();
    }

    public CuentaMesa? Cuenta(int mesa)
    {
        using var cn = _conexiones.Hotel();
        cn.Open();
        var f = FacturaDeMesa(cn, null, mesa);
        return f is null ? null : Leer(cn, null, f.Value);
    }

    /// <summary>Agrega un articulo del menu. Si la mesa esta libre, abre su cuenta.</summary>
    public CuentaMesa Agregar(int mesa, string referencia, decimal cantidad, bool? porFraccion, string usuario)
    {
        if (ReglasCafeteria.ValidarCantidad(cantidad) is { } malo) throw new ErrorCatalogo(malo);
        return EnTransaccion(mesa, (cn, tx, f) =>
        {
            var cliente = Cliente(cn, tx, f);
            var a = Articulo(cn, tx, cliente, referencia) ?? throw new ErrorCatalogo($"No existe el artículo {referencia}.");
            var p = ReglasCafeteria.Precio(a.Precio, porFraccion)
                    ?? throw new ErrorCatalogo(porFraccion == true ? $"\"{a.Precio.Descripcion}\" no se vende por trago."
                        : $"\"{a.Precio.Descripcion}\" no tiene precio para este cliente.");
            if (ReglasCafeteria.ValidarPrecio(p.Precio) is { } m) throw new ErrorCatalogo(m);
            Insertar(cn, tx, f, a.Id, a.Precio.Referencia, a.Sustituye, ReglasCafeteria.Descripcion(a.Precio.Descripcion), cantidad,
                a.Precio.Fraccion, p, usuario);
        }, usuario, crear: true);
    }

    /// <summary>El articulo especial de Delphi (descripcion y precio a mano): alimento ('00', art. 8) o bebida ('**', art. 7).</summary>
    public CuentaMesa Especial(int mesa, bool bebida, string descripcion, decimal precio, string usuario)
    {
        if (ReglasCafeteria.ValidarEspecial(descripcion, precio) is { } malo) throw new ErrorCatalogo(malo);
        precio = Math.Round(precio, 2, MidpointRounding.AwayFromZero);
        return EnTransaccion(mesa, (cn, tx, f) =>
            Insertar(cn, tx, f, bebida ? ReglasCafeteria.ArticuloBebida : ReglasCafeteria.ArticuloAlimento,
                bebida ? ReglasCafeteria.ReferenciaBebida : ReglasCafeteria.ReferenciaAlimento, bebida ? "Bebidas" : "Alimentos",
                ReglasCafeteria.Descripcion(descripcion).ToUpperInvariant(), 1, 1, new PrecioLinea(precio, 1, 0), usuario),
            usuario, crear: true);
    }

    /// <summary>Cambia la cantidad (y si va por unidad o por trago) de una linea que no ha salido en la pre-cuenta.</summary>
    public CuentaMesa Cambiar(int mesa, int secuencia, decimal cantidad, bool? porFraccion, string usuario)
    {
        if (ReglasCafeteria.ValidarCantidad(cantidad) is { } malo) throw new ErrorCatalogo(malo);
        return EnTransaccion(mesa, (cn, tx, f) =>
        {
            var l = Linea(cn, tx, f, secuencia);
            if (ReglasCafeteria.PuedeCambiar(l.Linea) is { } m) throw new ErrorCatalogo(m);
            var precio = l.Linea.Precio;
            var tragos = l.Linea.Tragos;
            var costo = l.Costo;
            var esFraccion = l.Linea.Tragos == 2;
            if (porFraccion is { } pf && pf != esFraccion)
            {
                var a = Articulo(cn, tx, Cliente(cn, tx, f), l.Linea.Referencia)
                        ?? throw new ErrorCatalogo($"\"{l.Linea.Descripcion}\" no se puede cambiar: es un artículo especial.");
                var p = ReglasCafeteria.Precio(a.Precio, pf)
                        ?? throw new ErrorCatalogo(pf ? $"\"{l.Linea.Descripcion}\" no se vende por trago." : $"\"{l.Linea.Descripcion}\" no tiene precio por unidad.");
                (precio, tragos, costo) = (p.Precio, p.Tragos, p.Costo);
            }
            cn.Execute("""
                UPDATE FaFacturaDetalle SET Cantidad = @cantidad, Precio = @precio, Tragos = @tragos, Costo = @costo, Importe = @importe
                WHERE InvFactura = @f AND Secuencia = @secuencia
                """, new { f, secuencia, cantidad, precio, tragos, costo, importe = ReglasCafeteria.Importe(cantidad, precio) }, tx);
        }, usuario);
    }

    /// <summary>Borra una linea no impresa (queda en FaLog). Si era la ultima, la mesa queda libre.</summary>
    public CuentaMesa? Borrar(int mesa, int secuencia, string usuario)
    {
        var vacia = false;
        var r = EnTransaccion(mesa, (cn, tx, f) =>
        {
            var l = Linea(cn, tx, f, secuencia);
            if (ReglasCafeteria.PuedeBorrar(l.Linea) is { } m) throw new ErrorCatalogo(m);
            cn.Execute("DELETE FROM FaFacturaDetalle WHERE InvFactura = @f AND Secuencia = @secuencia", new { f, secuencia }, tx);
            Log(cn, tx, f, usuario, "Borro", l.Linea.Referencia, l.Linea.Descripcion, l.Linea.Cantidad, l.Linea.Precio);
            if (cn.ExecuteScalar<int>("SELECT COUNT(*) FROM FaFacturaDetalle WHERE InvFactura = @f", new { f }, tx) == 0)
            {
                // nada impreso y sin lineas: la cuenta no existio (Delphi no la grababa si no tenia detalle)
                cn.Execute("DELETE FROM FaFactura WHERE InvFactura = @f AND EstatusFactura_id = 1", new { f }, tx);
                vacia = true;
            }
        }, usuario, totalizar: () => !vacia);
        return vacia ? null : r;
    }

    /// <summary>Pre-cuenta (como RzRapidFireButton1Click): todo lo de la mesa queda impreso y se anota la hora.</summary>
    public PreCuentaImpresa PreCuenta(int mesa, string usuario)
    {
        var c = EnTransaccion(mesa, (cn, tx, f) =>
        {
            if (cn.ExecuteScalar<int>("SELECT COUNT(*) FROM FaFacturaDetalle WHERE InvFactura = @f", new { f }, tx) == 0)
                throw new ErrorCatalogo("La mesa no tiene nada que imprimir.");
            cn.Execute("UPDATE FaFacturaDetalle SET Impreso = 1 WHERE InvFactura = @f", new { f }, tx);
            cn.Execute("INSERT INTO FaFacturaHoraImpresion (InvFactura) VALUES (@f)", new { f }, tx);
        }, usuario);
        return Imprimible(c.Factura);
    }

    /// <summary>La pre-cuenta para imprimir (ticket de 80 mm). No es comprobante fiscal.</summary>
    public PreCuentaImpresa Imprimible(long factura)
    {
        using var cn = _conexiones.Hotel();
        cn.Open();
        if (cn.ExecuteScalar<int>("SELECT COUNT(*) FROM FaFactura WHERE InvFactura = @factura AND EstatusFactura_id = 1", new { factura = (int)factura }) == 0)
            throw new ErrorCatalogo($"La cuenta {factura} ya no está abierta.");
        var c = Leer(cn, null, (int)factura);
        var hora = cn.ExecuteScalar<DateTime?>("SELECT MAX(HoradeImpresion) FROM FaFacturaHoraImpresion WHERE InvFactura = @factura",
            new { factura = (decimal)factura }) ?? DateTime.Now;
        var usuario = cn.ExecuteScalar<string?>("SELECT RTRIM(Usuario) FROM FaFactura WHERE InvFactura = @factura", new { factura = (int)factura });
        return new PreCuentaImpresa(ServicioEcf.Emisor(cn), c, hora, usuario);
    }

    /// <summary>
    /// Mover la cuenta a otra mesa. Libre: solo cambia la mesa. Ocupada: las lineas pasan a la cuenta de esa mesa (con
    /// lo impreso como esta) y la cuenta vacia se cierra como Delphi (Stp_TransferirMesas: TipoVenta 3, linea
    /// "Factura Transferida" y FaFacturaTransferida).
    /// </summary>
    public CuentaMesa Mover(int mesa, int destino, string usuario)
    {
        if (mesa == destino) throw new ErrorCatalogo("Elija otra mesa.");
        using var cn = _conexiones.Hotel();
        cn.Open();
        using var tx = cn.BeginTransaction();
        Bloquear(cn, tx);
        var f = FacturaDeMesa(cn, tx, mesa) ?? throw new ErrorCatalogo($"La mesa {mesa} no tiene cuenta abierta.");
        if (cn.ExecuteScalar<int>("SELECT COUNT(*) FROM FaMesa WHERE Mesa_id = @destino AND Estatus = 1", new { destino }, tx) == 0)
            throw new ErrorCatalogo($"No existe la mesa {destino}.");
        var u = U(usuario);
        var otra = FacturaDeMesa(cn, tx, destino);
        int resultado;
        if (otra is null)
        {
            cn.Execute("UPDATE FaFactura SET Mesa_id = @destino WHERE InvFactura = @f", new { destino, f }, tx);
            resultado = f;
        }
        else
        {
            var ultima = cn.ExecuteScalar<int?>("SELECT MAX(Secuencia) FROM FaFacturaDetalle WHERE InvFactura = @otra", new { otra }, tx) ?? 0;
            cn.Execute("""
                INSERT INTO FaFacturaDetalle (InvFactura, Secuencia, ArticuloRecord_id, Referencia, Sustituye, NumeroAlmacen, Descripcion, Comanda,
                       Cantidad, Libras, Fraccion, Tragos, Costo, CostoFraccion, MontoDescuento, Precio, Precio2, Importe, Impreso, CantidadAnterior,
                       Ximporte, Usuario, FechaAdicion)
                SELECT @otra, Secuencia + @ultima, ArticuloRecord_id, Referencia, Sustituye, NumeroAlmacen, Descripcion, Comanda,
                       Cantidad, Libras, Fraccion, Tragos, Costo, CostoFraccion, MontoDescuento, Precio, Precio2, Importe, Impreso, CantidadAnterior,
                       Ximporte, Usuario, FechaAdicion
                FROM FaFacturaDetalle WHERE InvFactura = @f
                """, new { otra = otra.Value, ultima, f }, tx);
            cn.Execute("DELETE FROM FaFacturaDetalle WHERE InvFactura = @f", new { f }, tx);
            Totalizar(cn, tx, otra.Value);
            cn.Execute("""
                UPDATE FaFactura SET SubTotal = 0, Itbis = 0, Propina = 0, Descuento = 0, PorcDescuento = 0, Total = 0, Cambio = 0, PropinaV = 0,
                       TipoVenta_id = 1
                WHERE InvFactura = @f
                """, new { f }, tx);
            cn.Execute("Stp_TransferirCerrarFactura", new { InvFactura = (decimal)f }, tx, commandType: CommandType.StoredProcedure);
            cn.Execute("UPDATE FaTmpFactura SET TipoVenta_id = 3 WHERE InvFactura = @f", new { f }, tx);
            cn.Execute("""
                INSERT INTO FaTmpFacturaDetalle (InvFactura, Secuencia, ArticuloRecord_id, Referencia, Sustituye, NumeroAlmacen, Descripcion,
                       Cantidad, Libras, Fraccion, Tragos, Costo, CostoFraccion, MontoDescuento, Precio, Precio2, Importe, CantidadAnterior,
                       Usuario, FechaAdicion, Impreso)
                VALUES (@f, 1, 7, '*T*', 'BEBIDAS', 1, 'Factura Transferida', 1, 0, 1, 1, 0, 0, 0, 0, 0, 0, 1, @u, GETDATE(), 1)
                """, new { f, u }, tx);
            cn.Execute("INSERT INTO FaFacturaTransferida (InvFactura, Mesa_id1, Mesa_id2, FechaAdicion, Usuario) VALUES (@f, @mesa, @destino, GETDATE(), @u)",
                new { f = (decimal)f, mesa, destino, u }, tx);
            resultado = otra.Value;
        }
        Log(cn, tx, f, usuario, "Envio.", "Transferio", $"Mesa#{mesa} a la #{destino}", 0, 0);
        var c = Leer(cn, tx, resultado);
        tx.Commit();
        return c;
    }

    // ---------------------------------------------------------------------------------------------

    private CuentaMesa EnTransaccion(int mesa, Action<IDbConnection, IDbTransaction, int> accion, string usuario, bool crear = false,
        Func<bool>? totalizar = null)
    {
        using var cn = _conexiones.Hotel();
        cn.Open();
        using var tx = cn.BeginTransaction();
        Bloquear(cn, tx);
        var f = FacturaDeMesa(cn, tx, mesa);
        if (f is null)
        {
            if (!crear) throw new ErrorCatalogo($"La mesa {mesa} no tiene cuenta abierta.");
            f = Abrir(cn, tx, mesa, usuario);
        }
        accion(cn, tx, f.Value);
        CuentaMesa? c = null;
        if (totalizar?.Invoke() ?? true)
        {
            Totalizar(cn, tx, f.Value);
            c = Leer(cn, tx, f.Value);
        }
        tx.Commit();
        return c!;
    }

    private static void Bloquear(IDbConnection cn, IDbTransaction tx) =>
        cn.Execute($"EXEC sp_getapplock @Resource = '{Candado}', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 20000",
            transaction: tx);

    private static int? FacturaDeMesa(IDbConnection cn, IDbTransaction? tx, int mesa)
    {
        var f = cn.Query<int>("SELECT InvFactura FROM FaFactura WHERE Mesa_id = @mesa AND EstatusFactura_id = @Abierta ORDER BY InvFactura",
            new { mesa, Abierta }, tx).ToList();
        return f.Count == 0 ? null : f[0];
    }

    /// <summary>La cuenta nueva, como FacturaNewRecord: cliente de contado, camarero 1, turno A; numero = mayor de abiertas y cerradas + 1.</summary>
    private static int Abrir(IDbConnection cn, IDbTransaction tx, int mesa, string usuario)
    {
        var seccion = cn.ExecuteScalar<int?>("SELECT faSeccion_id FROM FaMesa WHERE Mesa_id = @mesa AND Estatus = 1", new { mesa }, tx)
                      ?? throw new ErrorCatalogo($"No existe la mesa {mesa}.");
        var numero = cn.ExecuteScalar<int>("""
            SELECT ISNULL(MAX(x), 0) + 1 FROM (
                SELECT MAX(InvFactura) x FROM FaFactura WITH (UPDLOCK, HOLDLOCK)
                UNION ALL SELECT MAX(InvFactura) FROM FaTmpFactura WITH (UPDLOCK, HOLDLOCK)) t
            """, transaction: tx);
        cn.Execute("""
            INSERT INTO FaFactura (InvFactura, FacturaAnt, NCF, faSeccion_id, Mesa_id, Habitacion_id, PuntodeVenta_id, Fecha, NumeroCliente,
                   NombreCliente, RncCliente, Personas, Turno, Camarero_id, Subtotal, Itbis, Propina, PropinaV, Descuento, PorcDescuento, Total,
                   Cambio, EstatusFactura_id, TipoVenta_id, Usuario, FechaAdicion)
            VALUES (@numero, 0, NULL, @seccion, @mesa, 0, 1, CAST(GETDATE() AS date), 1, 'CLIENTE DE CONTADO', '00000000000', 1, 'A', 1,
                    0, 0, 0, 0, 0, 0, 0, 0, @Abierta, 1, @u, GETDATE())
            """, new { numero, seccion, mesa, Abierta, u = U(usuario) }, tx);
        return numero;
    }

    private static long Cliente(IDbConnection cn, IDbTransaction tx, int f) =>
        (long)cn.ExecuteScalar<decimal>("SELECT NumeroCliente FROM FaFactura WHERE InvFactura = @f", new { f }, tx);

    private static (PrecioArticulo Precio, int Id, string? Sustituye)? Articulo(IDbConnection cn, IDbTransaction tx, long cliente, string referencia)
    {
        var a = cn.QueryFirstOrDefault<(string Referencia, string Descripcion, decimal? Precio, decimal? Precio2, decimal? Costo, decimal? Fraccion,
                decimal Id, string? Sustituye)>("""
            SELECT RTRIM(Referencia), RTRIM(Descripcion), dbo.Fn_Precio1(@cliente, Referencia, 1), dbo.Fn_Precio1(@cliente, Referencia, 2),
                   Costo, Fraccion, ArticuloRecord_Id, RTRIM(ReferenciaFabrica)
            FROM InvArticulo WHERE Referencia = @referencia AND Referencia NOT IN ('00', '**')
            """, new { cliente = (decimal)cliente, referencia = A(referencia.Trim(), 16) }, tx);
        if (a.Referencia is null) return null;
        return (new PrecioArticulo(a.Referencia, a.Descripcion, a.Precio ?? 0, a.Precio2 ?? 0, a.Costo ?? 0, (int)(a.Fraccion ?? 1)), (int)a.Id,
            a.Sustituye is { Length: > 10 } s ? s[..10] : a.Sustituye);
    }

    private static void Insertar(IDbConnection cn, IDbTransaction tx, int f, int articulo, string referencia, string? sustituye, string descripcion,
        decimal cantidad, int fraccion, PrecioLinea p, string usuario)
    {
        var sec = cn.ExecuteScalar<int>("SELECT ISNULL(MAX(Secuencia), 0) + 1 FROM FaFacturaDetalle WHERE InvFactura = @f", new { f }, tx);
        // como FacturaDetalleNewRecord: almacen 1, Precio2 0, CantidadAnterior 0, sin imprimir
        cn.Execute("""
            INSERT INTO FaFacturaDetalle (InvFactura, Secuencia, ArticuloRecord_id, Referencia, Sustituye, NumeroAlmacen, Descripcion, Cantidad,
                   Fraccion, Tragos, Costo, CostoFraccion, Precio, Precio2, Importe, Impreso, CantidadAnterior, Usuario, FechaAdicion)
            VALUES (@f, @sec, @articulo, @referencia, @sustituye, 1, @descripcion, @cantidad, @fraccion, @tragos, @costo, 0, @precio, 0,
                    @importe, 0, 0, @u, GETDATE())
            """, new
        {
            f, sec, articulo = (decimal)articulo, referencia = A(referencia, 16), sustituye = A(sustituye, 10), descripcion = A(descripcion, 25),
            cantidad, fraccion, tragos = p.Tragos, costo = p.Costo, precio = p.Precio, importe = ReglasCafeteria.Importe(cantidad, p.Precio),
            u = U(usuario),
        }, tx);
    }

    private static (LineaMesa Linea, decimal Costo) Linea(IDbConnection cn, IDbTransaction tx, int f, int secuencia)
    {
        var l = LeerLineas(cn, tx, f, secuencia).FirstOrDefault();
        return l.Linea is null ? throw new ErrorCatalogo("Esa línea ya no está en la cuenta.") : l;
    }

    private static List<(LineaMesa Linea, decimal Costo)> LeerLineas(IDbConnection cn, IDbTransaction? tx, int f, int? secuencia = null) =>
        cn.Query<(int Secuencia, string Referencia, string Descripcion, decimal Cantidad, decimal Precio, decimal Importe, int? Tragos, bool Impreso,
                decimal Costo)>("""
            SELECT Secuencia, RTRIM(Referencia), RTRIM(Descripcion), Cantidad, Precio, Importe, Tragos, Impreso, Costo
            FROM FaFacturaDetalle WHERE InvFactura = @f AND (@secuencia IS NULL OR Secuencia = @secuencia) ORDER BY Secuencia
            """, new { f, secuencia }, tx)
            .Select(x => (new LineaMesa(x.Secuencia, x.Referencia, x.Descripcion, x.Cantidad, x.Precio, x.Importe, x.Tragos ?? 1, x.Impreso), x.Costo))
            .ToList();

    private static (bool Itbis, bool Propina, decimal PorcItbis, decimal PorcPropina, decimal TasaDolar) Impuestos(IDbConnection cn, IDbTransaction? tx, int f) =>
        cn.QueryFirst<(bool, bool, decimal, decimal, decimal)>("""
            SELECT ISNULL(c.Itbis, 1), ISNULL(c.Propina, 1), p.PorcItbis, p.PorcPropina, ISNULL(p.tasadollar, 0)
            FROM FaFactura f LEFT JOIN CxcCliente c ON c.NumeroCliente = f.NumeroCliente CROSS JOIN (SELECT TOP 1 * FROM FaParametro) p
            WHERE f.InvFactura = @f
            """, new { f }, tx);

    /// <summary>Vuelve a totalizar el encabezado (CalcularTotales): ITBIS y propina truncados, sin descuento.</summary>
    private static void Totalizar(IDbConnection cn, IDbTransaction tx, int f)
    {
        var i = Impuestos(cn, tx, f);
        var t = ReglasCafeteria.Totales(LeerLineas(cn, tx, f).Select(x => x.Linea), i.Itbis, i.Propina, i.PorcItbis, i.PorcPropina);
        cn.Execute("""
            UPDATE FaFactura SET Subtotal = @SubTotal, Itbis = @Itbis, Propina = @Propina, Total = @Total, Descuento = 0, PorcDescuento = 0
            WHERE InvFactura = @f
            """, new { t.SubTotal, t.Itbis, t.Propina, t.Total, f }, tx);
    }

    private static CuentaMesa Leer(IDbConnection cn, IDbTransaction? tx, int f)
    {
        var h = cn.QueryFirst<(int Mesa, string? NombreMesa, DateTime Fecha, decimal Cliente, string? Nombre, int? Habitacion, DateTime Abierta)>("""
            SELECT f.Mesa_id, RTRIM(m.Descripcion), f.Fecha, f.NumeroCliente, RTRIM(f.NombreCliente), NULLIF(f.Habitacion_id, 0), f.FechaAdicion
            FROM FaFactura f LEFT JOIN FaMesa m ON m.Mesa_id = f.Mesa_id WHERE f.InvFactura = @f
            """, new { f }, tx);
        var lineas = LeerLineas(cn, tx, f).Select(x => x.Linea).ToList();
        var i = Impuestos(cn, tx, f);
        var t = ReglasCafeteria.Totales(lineas, i.Itbis, i.Propina, i.PorcItbis, i.PorcPropina);
        return new CuentaMesa(h.Mesa, h.NombreMesa ?? $"MESA {h.Mesa}", f, h.Fecha, (long)h.Cliente, h.Nombre, h.Habitacion, i.Itbis, i.Propina,
            lineas, t, ReglasCafeteria.PreCuentaAlDia(lineas), i.TasaDolar, h.Abierta);
    }

    private static void Log(IDbConnection cn, IDbTransaction tx, int f, string usuario, string tipo, string referencia, string descripcion,
        decimal cantidad, decimal precio) =>
        cn.Execute("""
            INSERT INTO FaLog (InvFactura, usuario, fechahora, tipo, referencia, Descripcion, Cantidad, Precio, Impreso)
            VALUES (@f, @u, GETDATE(), @tipo, @referencia, @descripcion, @cantidad, @precio, 0)
            """, new
        {
            f = (decimal)f, u = A(usuario.Length > 16 ? usuario[..16] : usuario, 16), tipo = A(tipo, 6),
            referencia = A(referencia.Length > 16 ? referencia[..16] : referencia, 16),
            descripcion = A(descripcion.Length > 30 ? descripcion[..30] : descripcion, 30), cantidad, precio,
        }, tx);
}
