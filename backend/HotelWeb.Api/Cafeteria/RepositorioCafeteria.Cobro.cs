using System.Data;
using Dapper;
using HotelWeb.Api.Catalogos;
using HotelWeb.Api.Ecf;
using HotelWeb.Core.Cafeteria;
using HotelWeb.Core.Ecf;
using HotelWeb.Core.Hospedaje;
using HotelWeb.Core.Identificacion;

namespace HotelWeb.Api.Cafeteria;

public sealed record BancoTarjeta(int Id, string Nombre);

public sealed record ClienteCobro(long Numero, string Nombre, string? Rnc, bool Itbis, bool Propina, int? Tipo, bool Credito);

/// <param name="Cliente">Cliente de CxC (para credito o para tomar su RNC y nombre); null = de contado.</param>
public sealed record DatosCobro(int Tipo, long? Cliente, string? Rnc, string? Nombre, IReadOnlyList<PagoCafeteria> Pagos);

public sealed record ResultadoCobro(long Factura, int Mesa, string Encf, decimal SubTotal, decimal Itbis, decimal Propina, decimal Total,
    decimal Recibido, decimal Devuelta, bool Credito);

public sealed record TicketCafeteria(long Factura, int Mesa, string? Encf, string TipoNombre, DateTime Fecha, DateTime? Vencimiento,
    EmisorEcf Emisor, string? Nombre, string? Identificacion, IReadOnlyList<LineaImpresa> Items, decimal SubTotal, decimal Itbis,
    decimal Propina, decimal Total, IReadOnlyList<PagoImpreso> Pagos, decimal Devuelta, string? Usuario, TimbreEcf Timbre);

/// <summary>
/// El cobro de la cuenta de una mesa con e-CF (etapa 8b), como UFrmPagar.SpeedButton2Click, en UNA transaccion: cliente y
/// totales, e-NCF (STp_NumeroComprobanteFiscal_E, con todo validado antes), pagos aplicados (faFacturaPago), credito a
/// CxC (STP_PasarACXC) y el paso a FaTmpFactura (Stp_TransferirCerrarFactura). El envio a la DGII va despues, afuera.
/// </summary>
public sealed partial class RepositorioCafeteria
{
    public IReadOnlyList<BancoTarjeta> Bancos()
    {
        using var cn = _conexiones.Hotel();
        return cn.Query<BancoTarjeta>("SELECT TipoTarjeta_id AS Id, RTRIM(Descripcion) AS Nombre FROM fatipoTarjeta ORDER BY Descripcion").ToList();
    }

    /// <summary>El nombre registrado en la DGII de un RNC o cedula (Seguridad.DGRnc, como GetRncCliente de Delphi).</summary>
    public string? NombreDgii(string rnc)
    {
        var limpio = IdentificacionRD.Limpiar(rnc);
        if (limpio.Length is < 9 or > 11 || !IdentificacionRD.SoloDigitos(limpio)) return null;
        using var cn = _conexiones.Seguridad();
        return cn.ExecuteScalar<string?>("SELECT TOP 1 RTRIM(NOMBRE) FROM DGRnc WHERE RNC = @limpio",
            new { limpio = new DbString { Value = limpio, IsAnsi = false, Length = 13 } });
    }

    public ClienteCobro? ClienteDeCobro(long numero)
    {
        using var cn = _conexiones.Hotel();
        cn.Open();
        return LeerCliente(cn, null, numero);
    }

    public ResultadoCobro Cobrar(int mesa, DatosCobro d, string usuario)
    {
        using var cn = _conexiones.Hotel();
        cn.Open();
        using var tx = cn.BeginTransaction();
        Bloquear(cn, tx);
        var f = FacturaDeMesa(cn, tx, mesa) ?? throw new ErrorCatalogo($"La mesa {mesa} no tiene cuenta abierta.");
        var lineas = LeerLineas(cn, tx, f).Select(x => x.Linea).ToList();
        if (!ReglasCafeteria.PreCuentaAlDia(lineas))
            throw new ErrorCatalogo(lineas.Count == 0 ? "La mesa no tiene nada que cobrar." : "Saque la pre-cuenta de todo antes de cobrar.");
        if (cn.ExecuteScalar<decimal>("SELECT NumeroCliente FROM FaFactura WHERE InvFactura = @f", new { f }, tx) == ReglasCobroCafeteria.ClienteHabitacion)
            throw new ErrorCatalogo("La cuenta está cargada a una habitación.");

        var tipo = ReglasCheckout.Tipos.FirstOrDefault(t => t.Id == d.Tipo) ?? throw new ErrorCatalogo("Escoja el tipo de comprobante.");
        var cliente = d.Cliente is { } c && c != ReglasCobroCafeteria.ClienteContado
            ? LeerCliente(cn, tx, c) ?? throw new ErrorCatalogo($"No existe el cliente {c}.")
            : LeerCliente(cn, tx, ReglasCobroCafeteria.ClienteContado)!;
        var comodin = cn.ExecuteScalar<int?>("SELECT TOP 1 ClienteComodin FROM FaParametro", transaction: tx) ?? 0;

        // RNC y nombre: lo digitado, o los del cliente de CxC; sin nombre, el de la DGII
        var rnc = (string.IsNullOrWhiteSpace(d.Rnc) ? cliente.Numero == ReglasCobroCafeteria.ClienteContado ? "" : cliente.Rnc : d.Rnc)?.Trim() ?? "";
        var sinId = IdentificacionRD.SinIdentificacion(rnc);
        IdentificacionRD.Determinar(sinId ? "" : rnc, out var limpio);
        var nombre = d.Nombre?.Trim();
        if (string.IsNullOrWhiteSpace(nombre))
            nombre = sinId ? cliente.Numero == ReglasCobroCafeteria.ClienteContado ? "CLIENTE DE CONTADO" : cliente.Nombre
                : cliente.Numero != ReglasCobroCafeteria.ClienteContado ? cliente.Nombre : NombreDgii(limpio) ?? "";
        if (nombre.Length > 60) nombre = nombre[..60];
        if (!sinId && limpio.Length > 11) throw new ErrorCatalogo("La identificación no cabe (11 caracteres como máximo).");

        var p = cn.QueryFirst<(decimal PorcItbis, decimal PorcPropina)>("SELECT TOP 1 PorcItbis, PorcPropina FROM FaParametro", transaction: tx);
        var t = ReglasCafeteria.Totales(lineas, cliente.Itbis && tipo.Id != 13, cliente.Propina, p.PorcItbis, p.PorcPropina);
        if (ReglasCheckout.Validar(tipo.Id, sinId ? "" : limpio, nombre, t.Total, t.Itbis) is { } malo) throw new ErrorCatalogo(malo);
        if (!sinId && string.IsNullOrWhiteSpace(nombre)) throw new ErrorCatalogo("Falta el nombre o razón social del cliente.");

        var (pagos, motivo) = ReglasCobroCafeteria.Pagos(d.Pagos ?? [], t.Total,
            ReglasCobroCafeteria.AdmiteCredito(cliente.Numero, comodin) && cliente.Credito);
        if (pagos is null) throw new ErrorCatalogo(motivo!);
        var bancos = cn.Query<(int Id, string Nombre)>("SELECT TipoTarjeta_id, RTRIM(Descripcion) FROM fatipoTarjeta", transaction: tx)
            .ToDictionary(x => x.Id, x => x.Nombre);
        foreach (var x in pagos.Aplicados.Where(x => x.TipoTarjeta is not null))
            if (!bancos.ContainsKey(x.TipoTarjeta!.Value)) throw new ErrorCatalogo("Elija el banco de la tarjeta.");

        // el e-CF completo se revisa ANTES de gastar el numero (con un numero de muestra del tipo)
        var credito = ReglasCobroCafeteria.EsCredito(pagos.Aplicados);
        var items = EcfHotel.Items(lineas.Select(l => (l.Descripcion, l.Cantidad, l.Precio, l.Importe, true)));
        var muestra = new FacturaEcf($"{tipo.Codigo}0000000001", f, DateTime.Today, sinId ? "" : limpio, nombre, t.SubTotal, t.Itbis,
            t.Propina, credito, items, t.Total);
        if (EcfHotel.Validar(muestra) is { } malEcf) throw new ErrorCatalogo(malEcf);

        var encf = cn.QueryFirstOrDefault<string?>("STp_NumeroComprobanteFiscal_E", new { NumeroCliente = 1m, NotasCRDB = tipo.Id }, tx,
            commandType: CommandType.StoredProcedure)?.Trim();
        if (encf is not { Length: 13 } || encf[0] != 'E') throw new ErrorCatalogo("No se pudo obtener el e-NCF del tipo de comprobante elegido.");
        var u = U(usuario);

        // fecha: la del cobro (decision 2026-10-23: la del dia); EstatusFactura_id queda en 1 como lo deja Delphi en FaTmpFactura
        cn.Execute("""
            UPDATE FaFactura SET NCF = @encf, NumeroCliente = @cliente, NombreCliente = @nombre, RncCliente = @rnc, Fecha = CAST(GETDATE() AS date),
                   Subtotal = @SubTotal, Itbis = @Itbis, Propina = @Propina, Total = @Total, Descuento = 0, PorcDescuento = 0, Cambio = @devuelta
            WHERE InvFactura = @f
            """, new
        {
            encf = A(encf, 19), cliente = (decimal)cliente.Numero, nombre = A(nombre, 60), rnc = A(sinId ? "00000000000" : limpio, 11),
            t.SubTotal, t.Itbis, t.Propina, t.Total, devuelta = pagos.Devuelta, f,
        }, tx);
        cn.Execute("DELETE FROM faFacturaPago WHERE InvFactura = @f", new { f }, tx);
        var sec = 0;
        foreach (var x in pagos.Aplicados)
        {
            var banco = x.TipoTarjeta is { } b ? bancos[b] : null;
            cn.Execute("""
                INSERT INTO faFacturaPago (InvFactura, Secuencia, FormaPago_id, FormadePago, TipoTarjeta_id, MontoPago, Usuario, FechaAdicion, Banco, NTarjeta)
                VALUES (@f, @sec, @forma, @descripcion, @tarjeta, @monto, @u, GETDATE(), @banco, @nTarjeta)
                """, new
            {
                f, sec = ++sec, forma = x.FormaPago, descripcion = A(x.Descripcion.Length > 16 ? x.Descripcion[..16] : x.Descripcion, 16),
                tarjeta = x.TipoTarjeta, monto = x.Monto, u, banco = A(banco, 50), nTarjeta = A(banco is { Length: > 25 } ? banco[..25] : banco, 25),
            }, tx);
        }
        // el credito a CxC (lee FaFactura.NumeroCliente y la suma de la forma 3) y el paso a FaTmpFactura
        cn.Execute("STP_PasarACXC", new { InvFactura = (decimal)f, usuario = A(u.Value, 10) }, tx, commandType: CommandType.StoredProcedure);
        cn.Execute("Stp_TransferirCerrarFactura", new { InvFactura = (decimal)f }, tx, commandType: CommandType.StoredProcedure);
        if (cn.ExecuteScalar<int>("SELECT COUNT(*) FROM FaFactura WHERE InvFactura = @f", new { f }, tx) != 0)
            throw new ErrorCatalogo("No se pudo cerrar la cuenta: intente de nuevo.");
        tx.Commit();
        return new ResultadoCobro(f, mesa, encf, t.SubTotal, t.Itbis, t.Propina, t.Total, pagos.Recibido, pagos.Devuelta, credito);
    }

    /// <summary>El ticket de la factura cobrada (POS 80 mm, como ImprimirSolucionNoFiscal) con el QR.</summary>
    public async Task<TicketCafeteria> Ticket(long factura, ServicioEcf ecf)
    {
        FacturaEcf f;
        EmisorEcf emisor;
        DateTime? venc;
        List<PagoImpreso> pagos;
        (int Mesa, decimal Cambio, string? Usuario) extra;
        using (var cn = _conexiones.Hotel())
        {
            f = ServicioEcf.LeerCafeteria(cn, null, factura) ?? throw new ErrorCatalogo($"No existe la factura {factura} de la cafetería.");
            emisor = ServicioEcf.Emisor(cn);
            venc = f.Encf.StartsWith('E') ? ServicioEcf.FechaVencimiento(cn, f.Encf) : null;
            pagos = cn.Query<(string? Forma, string? Banco, decimal Monto)>(
                    "SELECT RTRIM(FormadePago), RTRIM(Banco), MontoPago FROM faFacturaPago WHERE InvFactura = @f ORDER BY Secuencia", new { f = (int)factura })
                .Select(x => new PagoImpreso(x.Forma ?? "", x.Banco ?? "", x.Monto)).ToList();
            extra = cn.QueryFirst<(int, decimal, string?)>("SELECT Mesa_id, Cambio, RTRIM(Usuario) FROM FaTmpFactura WHERE InvFactura = @f",
                new { f = (int)factura });
        }
        var tipo = ReglasCheckout.Tipos.FirstOrDefault(x => f.Encf.StartsWith(x.Codigo));
        var timbre = f.Encf.StartsWith('E') ? await ecf.Timbre(f, emisor.Rnc) : new TimbreEcf(null, null, null);
        return new TicketCafeteria(factura, extra.Mesa, f.Encf == "" ? null : f.Encf,
            tipo is null ? "Factura" : $"Factura de {tipo.Nombre.ToLowerInvariant()} electronica", f.Fecha, venc, emisor, f.NombreCliente,
            IdentificacionRD.SinIdentificacion(f.Rnc) ? null : f.Rnc?.Trim(),
            f.Items.Select(i => new LineaImpresa(null, i.Descripcion, i.Cantidad, i.Precio, i.Importe)).ToList(),
            f.SubTotal, f.Itbis, f.Propina, f.Total, pagos, extra.Cambio, extra.Usuario, timbre);
    }

    private static ClienteCobro? LeerCliente(IDbConnection cn, IDbTransaction? tx, long numero)
    {
        var c = cn.QueryFirstOrDefault<(decimal Numero, string? Nombre, string? Rnc, bool? Itbis, bool? Propina, int? Tipo, string? Estatus)>("""
            SELECT NumeroCliente, RTRIM(Nombre), RTRIM(Rnc), Itbis, Propina, CodigoCompFiscal, RTRIM(Estatus) FROM CxcCliente WHERE NumeroCliente = @numero
            """, new { numero = (decimal)numero }, tx);
        if (c.Numero == 0) return null;
        if (numero != ReglasCobroCafeteria.ClienteContado && string.Equals(c.Estatus, "Inactivo", StringComparison.OrdinalIgnoreCase))
            throw new ErrorCatalogo($"El cliente {numero} está inactivo.");
        return new ClienteCobro((long)c.Numero, c.Nombre ?? "", IdentificacionRD.SinIdentificacion(c.Rnc) ? null : c.Rnc, c.Itbis ?? true,
            c.Propina ?? true, c.Tipo, numero != ReglasCobroCafeteria.ClienteContado);
    }
}
