using System.Data;
using Dapper;
using HotelWeb.Api.Catalogos;
using HotelWeb.Api.Datos;
using HotelWeb.Api.Seguridad;
using HotelWeb.Core.Ecf;
using HotelWeb.Core.Hospedaje;

namespace HotelWeb.Api.Ecf;

public sealed record NotaPrevia(long Numero, string? Encf, DateTime Fecha, decimal Total, int? Codigo, string? Razon);

public sealed record PreparacionNota(string Origen, long Factura, string Encf, DateTime Fecha, string? Cliente, string? Rnc,
    decimal SubTotal, decimal Itbis, decimal Propina, decimal Total, EstadoEcf Dgii, IReadOnlyList<NotaPrevia> Notas, decimal Pendiente,
    int Dias, bool SinItbis, bool PuedeAnular, bool PuedeMonto);

public sealed record ResultadoNota(long Numero, string Encf, decimal Monto, string Mensaje, EstadoEcf Dgii);

public sealed record NotaImpresa(string Origen, long Numero, string Encf, DateTime Fecha, EmisorEcf Emisor, string? Cliente, string? Rnc,
    string NcfModificado, DateTime FechaModificado, int Codigo, string? Razon, DesgloseNota Desglose, bool SinItbis, TimbreEcf Timbre);

/// <summary>
/// Notas de credito (E34) del check-out y del walk-in (etapa 7). La anulacion total usa el procedimiento de Delphi
/// (Stp_AnularFacturaHotelNC: numero, e-NCF, nota con codigo 1 y el registro en HotelFacturaAnulada); la nota por un
/// monto se graba como SalvarNC de UFrmMntNCFrontDesk / NCWalking (codigo 3). Despues se envia con el armador de la E34.
/// Autoriza un responsable con su usuario y clave (Seg_Usuario.CancelaFactura), como PedirAutorizacion.
/// </summary>
public sealed class RepositorioNotas
{
    private readonly Conexiones _conexiones;
    private readonly RepositorioUsuarios _usuarios;

    public RepositorioNotas(Conexiones conexiones, RepositorioUsuarios usuarios)
    {
        _conexiones = conexiones;
        _usuarios = usuarios;
    }

    private static DbString A(string? v, int largo) => new() { Value = v, IsAnsi = true, Length = largo };

    public static OrigenFactura OrigenDe(string origen) => origen.ToLowerInvariant() switch
    {
        "hospedaje" => OrigenFactura.Hospedaje,
        "walkin" => OrigenFactura.WalkIn,
        "cafeteria" => OrigenFactura.Cafeteria,
        _ => throw new ErrorCatalogo("Origen no valido: hospedaje, walkin o cafeteria."),
    };

    public static OrigenFactura DeNota(OrigenFactura o) => o switch
    {
        OrigenFactura.WalkIn => OrigenFactura.NotaWalkIn,
        OrigenFactura.Cafeteria => OrigenFactura.NotaCafeteria,
        _ => OrigenFactura.NotaHospedaje,
    };

    /// <summary>Como va el origen en las rutas (/api/notas/{origen}, /nota/{origen}/{n}).</summary>
    public static string Ruta(OrigenFactura o) => o switch
    {
        OrigenFactura.WalkIn or OrigenFactura.NotaWalkIn => "walkin",
        OrigenFactura.Cafeteria or OrigenFactura.NotaCafeteria => "cafeteria",
        _ => "hospedaje",
    };

    private static List<NotaPrevia> Notas(IDbConnection cn, IDbTransaction? tx, OrigenFactura origen, long factura) =>
        cn.Query<(decimal Numero, string? Ncf, DateTime Fecha, decimal? Total, int? Codigo, string? Razon)>(origen == OrigenFactura.Cafeteria
                ? "SELECT Devolucion_id, RTRIM(NCF), Fecha, Total, CAST(CodigoModificacion AS int), RTRIM(RazonModificacion) FROM faFacturaNC WHERE InvFactura = @factura ORDER BY Devolucion_id"
                : origen == OrigenFactura.WalkIn
                ? "SELECT Devolucion_id, RTRIM(NCF), Fecha, Total, CodigoModificacion, RTRIM(RazonModificacion) FROM HotelFacturaWalkingNC WHERE FacturaWalking_id = @factura ORDER BY Devolucion_id"
                : "SELECT Devolucion_id, RTRIM(NCF), Fecha, Total, CodigoModificacion, RTRIM(RazonModificacion) FROM HotelFacturaNC WHERE Factura_id = @factura ORDER BY Devolucion_id",
                new { factura }, tx)
            .Select(x => new NotaPrevia((long)x.Numero, x.Ncf, x.Fecha, x.Total ?? 0, x.Codigo, x.Razon)).ToList();

    public async Task<PreparacionNota> Preparar(OrigenFactura origen, long factura, ServicioEcf ecf)
    {
        FacturaEcf f;
        List<NotaPrevia> notas;
        using (var cn = _conexiones.Hotel())
        {
            f = ServicioEcf.Leer(cn, null, origen, factura, out var transferida) ?? throw new ErrorCatalogo($"No existe la factura {factura}.");
            if (transferida) throw new ErrorCatalogo("La factura es de una cuenta transferida: no lleva nota de credito.");
            notas = Notas(cn, null, origen, factura);
        }
        // consulta la DGII (y la deja en EcfEstadoDgii: Stp_AnularFacturaHotelNC la exige aceptada)
        var dgii = f.Encf.StartsWith('E') ? await ecf.Consultar(factura, origen) : new EstadoEcf(f.Encf, factura, "Sin e-NCF", null, false);
        var pendiente = ReglasNotaCredito.Pendiente(f.Total, notas.Select(n => n.Total));
        var dias = (int)(DateTime.Today - f.Fecha.Date).TotalDays;
        bool Puede(int codigo, decimal monto) =>
            ReglasNotaCredito.Validar(codigo, monto, new string('x', 10), f.Encf, dgii.Aceptado, f.Total, pendiente, notas.Count > 0) is null;
        return new PreparacionNota(Ruta(origen), factura, f.Encf, f.Fecha, f.NombreCliente,
            f.Rnc, f.SubTotal, f.Itbis, f.Propina, f.Total, dgii, notas, pendiente, dias, NotaCreditoEcf.SinItbis(DateTime.Today, f.Fecha),
            Puede(ReglasNotaCredito.Anulacion, f.Total), Puede(ReglasNotaCredito.CorrigeMonto, 0.01m));
    }

    /// <summary>El responsable: usuario y clave validos, activo y con el permiso de cancelar facturas (como UsuarioAutorizado).</summary>
    private Usuario Responsable(string? usuario, string? clave)
    {
        var u = _usuarios.Validar(usuario ?? "", clave ?? "", out var motivo) ?? throw new ErrorCatalogo(motivo);
        if (!u.CancelaFactura) throw new ErrorCatalogo($"{u.Login} no esta autorizado para cancelar facturas (Seg_Usuario.CancelaFactura).");
        return u;
    }

    public async Task<ResultadoNota> Crear(OrigenFactura origen, long factura, int codigo, decimal monto, string? motivo, string? responsable,
        string? clave, string usuarioSesion, string? equipo, ServicioEcf ecf)
    {
        var quien = Responsable(responsable, clave);
        // el estado de la DGII al dia (y en EcfEstadoDgii) antes de nada
        var prep = await Preparar(origen, factura, ecf);
        var razon = (motivo ?? "").Trim();
        if (ReglasNotaCredito.Validar(codigo, monto, razon, prep.Encf, prep.Dgii.Aceptado, prep.Total, prep.Pendiente, prep.Notas.Count > 0) is { } m)
            throw new ErrorCatalogo(m);
        var original = new OriginalNota(prep.Encf, prep.Fecha, prep.Rnc, prep.Cliente, prep.SubTotal, prep.Itbis, prep.Propina, prep.Total);
        // la E34 entera se revisa antes de gastar el numero (con un numero de muestra)
        if (NotaCreditoEcf.Validar(new NotaCredito("E340000000001", 1, DateTime.Today, monto, codigo, razon), original) is { } malo)
            throw new ErrorCatalogo(malo);

        long numero;
        string encf;
        string mensaje;
        using (var cn = _conexiones.Hotel())
        {
            cn.Open();
            if (origen == OrigenFactura.Cafeteria)
                (numero, encf, mensaje) = NotaCafeteria(cn, prep, codigo, monto, razon, quien.Login, usuarioSesion, equipo);
            else if (codigo == ReglasNotaCredito.Anulacion)
            {
                // el procedimiento de Delphi (MIG_13): nota codigo 1 + HotelFacturaAnulada, en su propia transaccion
                var r = cn.QueryFirst<(int Resultado, string? NCFNota, string? Mensaje)>("dbo.Stp_AnularFacturaHotelNC", new
                {
                    Origen = A(origen == OrigenFactura.WalkIn ? "WALKIN" : "CHECKOUT", 10),
                    Factura_id = (decimal)factura,
                    UsuarioAutoriza = A(quien.Login, 30),
                    UsuarioSesion = A(usuarioSesion, 30),
                    Equipo = A(equipo is { Length: > 60 } e ? e[..60] : equipo ?? "HotelWeb", 60),
                    Motivo = A(razon, 500),
                }, commandType: CommandType.StoredProcedure);
                if (r.Resultado <= 0) throw new ErrorCatalogo(r.Mensaje ?? "No se pudo anular.");
                numero = r.Resultado;
                encf = r.NCFNota!.Trim();
                mensaje = r.Mensaje ?? "";
            }
            else
            {
                using var tx = cn.BeginTransaction(IsolationLevel.Serializable);
                var walk = origen == OrigenFactura.WalkIn;
                var tabla = walk ? "HotelFacturaWalkingNC" : "HotelFacturaNC";
                // como SalvarNC de Delphi: mayor + 1, E34 (RecordKey 4), SubTotal = Total = monto, ITBIS 0 en la tabla
                numero = cn.ExecuteScalar<long>($"SELECT ISNULL(MAX(Devolucion_id), 0) + 1 FROM {tabla} WITH (UPDLOCK, HOLDLOCK)", transaction: tx);
                encf = cn.QueryFirstOrDefault<string?>("STp_NumeroComprobanteFiscal_E", new { NumeroCliente = 1m, NotasCRDB = 4 }, tx,
                    commandType: CommandType.StoredProcedure)?.Trim() ?? "";
                if (!encf.StartsWith("E34") || encf.Length != 13) throw new ErrorCatalogo("No se pudo generar el e-NCF de la nota de credito (E34).");
                cn.Execute($"""
                    INSERT INTO {tabla} (Devolucion_id, {(walk ? "FacturaWalking_id" : "Factura_id")}, Fecha, NCF, SubTotal, Itbis, Total, Estatus,
                           usuario, fechaadicion, CodigoModificacion, RazonModificacion)
                    VALUES (@numero, @factura, CAST(GETDATE() AS date), @encf, @monto, 0, @monto, 1, @usuario, GETDATE(), 3, @razon)
                    """, new
                {
                    numero, factura = (decimal)factura, encf = A(encf, 19), monto,
                    usuario = A(usuarioSesion.Length > 10 ? usuarioSesion[..10] : usuarioSesion, 10), razon = A(razon, 90),
                }, tx);
                tx.Commit();
                mensaje = $"Nota de credito {encf} por {monto:N2} al {prep.Encf} (autorizo {quien.Login}).";
            }
        }
        EstadoEcf dgii;
        try { dgii = await ecf.Enviar(numero, DeNota(origen)); }
        catch (Exception e) when (e is ErrorCatalogo or ErrorEcf) { dgii = new EstadoEcf(encf, numero, "Sin enviar", e.Message, false); }
        var total = codigo == ReglasNotaCredito.Anulacion ? prep.Total : monto;
        return new ResultadoNota(numero, encf, total, mensaje, dgii);
    }

    /// <summary>
    /// La nota de la cafeteria (faFacturaNC) en una transaccion. Por monto, como SalvarNC de UFrmMntNCFrontDesk de la
    /// cafeteria (SubTotal = Total = monto, ITBIS y propina 0, el cliente de la factura). La anulacion total hace lo que
    /// Stp_AnularFacturaHotelNC para el hotel (que no tiene la cafeteria): las mismas revisiones, la nota codigo 1 por el
    /// total (sin ITBIS pasados 30 dias) y el registro en HotelFacturaAnulada con Origen 'CAFETERIA'.
    /// </summary>
    private static (long Numero, string Encf, string Mensaje) NotaCafeteria(IDbConnection cn, PreparacionNota prep, int codigo, decimal monto,
        string razon, string autoriza, string usuarioSesion, string? equipo)
    {
        using var tx = cn.BeginTransaction(IsolationLevel.Serializable);
        var f = cn.QueryFirstOrDefault<(decimal Cliente, string? Nombre)>(
            "SELECT NumeroCliente, RTRIM(NombreCliente) FROM FaTmpFactura WITH (UPDLOCK) WHERE InvFactura = @factura", new { factura = (int)prep.Factura }, tx);
        if (f.Cliente == 0 && f.Nombre is null) throw new ErrorCatalogo($"No existe la factura {prep.Factura} de la cafetería.");
        var anula = codigo == ReglasNotaCredito.Anulacion;
        if (anula)
        {
            if (cn.ExecuteScalar<int>("SELECT COUNT(*) FROM HotelFacturaAnulada WHERE Origen = 'CAFETERIA' AND Factura_id = @factura",
                    new { factura = (decimal)prep.Factura }, tx) > 0)
                throw new ErrorCatalogo("La factura ya fue anulada.");
            if (cn.ExecuteScalar<int>("SELECT COUNT(*) FROM faFacturaNC WHERE InvFactura = @factura", new { factura = (int)prep.Factura }, tx) > 0)
                throw new ErrorCatalogo("La factura ya tiene una nota de credito: no admite la anulacion total.");
        }
        var numero = cn.ExecuteScalar<long>("SELECT ISNULL(MAX(Devolucion_id), 0) + 1 FROM faFacturaNC WITH (UPDLOCK, HOLDLOCK)", transaction: tx);
        var encf = cn.QueryFirstOrDefault<string?>("STp_NumeroComprobanteFiscal_E", new { NumeroCliente = 1m, NotasCRDB = 4 }, tx,
            commandType: CommandType.StoredProcedure)?.Trim() ?? "";
        if (!encf.StartsWith("E34") || encf.Length != 13) throw new ErrorCatalogo("No se pudo generar el e-NCF de la nota de credito (E34).");

        // anulacion: los montos del original; pasados 30 dias sin el ITBIS (como el procedimiento del hotel)
        var itbis = anula && !prep.SinItbis ? prep.Itbis : 0m;
        var propina = anula ? prep.Propina : 0m;
        var total = anula ? prep.Total - (prep.Itbis - itbis) : monto;
        var sub = anula ? prep.SubTotal : monto;
        var usuario = A(usuarioSesion.Length > 10 ? usuarioSesion[..10] : usuarioSesion, 10);
        cn.Execute("""
            INSERT INTO faFacturaNC (Devolucion_id, InvFactura, NCF, Fecha, NumeroCliente, NombreCliente, Subtotal, Itbis, Propina, Total, Estatus,
                   Usuario, FechaAdicion, CodigoModificacion, RazonModificacion)
            VALUES (@numero, @factura, @encf, CAST(GETDATE() AS date), @cliente, @nombre, @sub, @itbis, @propina, @total, 1, @usuario, GETDATE(),
                    @codigo, @razon)
            """, new
        {
            numero = (decimal)numero, factura = (int)prep.Factura, encf = A(encf, 19), cliente = f.Cliente, nombre = A(f.Nombre is { Length: > 50 } n ? n[..50] : f.Nombre, 50),
            sub, itbis, propina, total, usuario, codigo = (byte)codigo, razon = A(razon.Length > 90 ? razon[..90] : razon, 90),
        }, tx);
        if (anula)
            cn.Execute("""
                INSERT INTO HotelFacturaAnulada (Origen, Factura_id, NCFOriginal, FechaOriginal, Total, Devolucion_id, NCFNota, UsuarioAutoriza,
                       UsuarioSesion, Equipo, FechaAnulacion, Motivo)
                VALUES ('CAFETERIA', @factura, @ncf, @fecha, @totalOriginal, @numero, @encf, @autoriza, @sesion, @equipo, GETDATE(), @motivo)
                """, new
            {
                factura = (decimal)prep.Factura, ncf = A(prep.Encf, 19), fecha = prep.Fecha, totalOriginal = prep.Total, numero = (decimal)numero,
                encf = A(encf, 19), autoriza = A(autoriza, 30), sesion = A(usuarioSesion, 30),
                equipo = A(equipo is { Length: > 60 } e ? e[..60] : equipo ?? "HotelWeb", 60), motivo = A(razon, 500),
            }, tx);
        tx.Commit();
        return (numero, encf, anula
            ? $"Nota de credito {encf} creada para anular el {prep.Encf}{(prep.SinItbis ? " (sin ITBIS: mas de 30 dias)" : "")}."
            : $"Nota de credito {encf} por {monto:N2} al {prep.Encf} (autorizo {autoriza}).");
    }

    /// <summary>Las notas E34 de la cafeteria de un rango de fechas, para su lista de facturas.</summary>
    internal static List<(FacturaCerrada F, FacturaEcf E)> ListaCafeteria(IDbConnection cn, DateTime desde, DateTime hasta, string? t) =>
        cn.Query<(decimal Numero, DateTime Fecha, string? Ncf, decimal? Total, string? Nombre, string? Modifica, int? Mesa)>("""
            SELECT TOP 300 n.Devolucion_id, n.Fecha, RTRIM(n.NCF), n.Total, RTRIM(o.NombreCliente), RTRIM(o.NCF), o.Mesa_id
            FROM faFacturaNC n JOIN FaTmpFactura o ON o.InvFactura = n.InvFactura
            WHERE n.Fecha >= @desde AND n.Fecha < @hasta AND n.NCF LIKE 'E34%'
              AND (@t IS NULL OR RTRIM(n.NCF) = @t OR RTRIM(o.NCF) = @t OR CAST(n.Devolucion_id AS varchar(20)) = @t OR o.NombreCliente LIKE '%' + @t + '%')
            ORDER BY n.Devolucion_id DESC
            """, new { desde = desde.Date, hasta = hasta.Date.AddDays(1), t = new DbString { Value = t, IsAnsi = true, Length = 60 } })
            .Select(x => (new FacturaCerrada((long)x.Numero, x.Fecha, x.Mesa ?? 0, x.Nombre, null, x.Ncf, x.Total ?? 0, false, null, false,
                    "NotaCafeteria", x.Modifica),
                new FacturaEcf(x.Ncf ?? "", (long)x.Numero, x.Fecha.Date, null, x.Nombre, x.Total ?? 0, 0, 0, false, Array.Empty<ItemEcf>()))).ToList();

    public async Task<NotaImpresa> Imprimible(OrigenFactura origenNota, long numero, ServicioEcf ecf)
    {
        (NotaCredito Nota, OriginalNota Original, long OriginalId) n;
        EmisorEcf emisor;
        using (var cn = _conexiones.Hotel())
        {
            n = ServicioEcf.LeerNota(cn, null, origenNota, numero) ?? throw new ErrorCatalogo($"No existe la nota de credito {numero}.");
            emisor = ServicioEcf.Emisor(cn);
        }
        var timbre = await ecf.Timbre(ServicioEcf.ComoFactura(n.Nota, n.Original), emisor.Rnc);
        var rnc = (n.Original.Rnc ?? "").Trim();
        return new NotaImpresa(Ruta(origenNota), numero, n.Nota.Encf, n.Nota.Fecha, emisor,
            n.Original.Nombre, rnc == "" || rnc.All(c => c == '0') ? null : rnc, n.Original.Ncf, n.Original.Fecha, n.Nota.Codigo, n.Nota.Razon,
            NotaCreditoEcf.Desglose(n.Nota.Monto, n.Nota.Codigo, n.Original), NotaCreditoEcf.SinItbis(n.Nota.Fecha, n.Original.Fecha), timbre);
    }

    /// <summary>Las notas E34 de un rango de fechas para la lista de facturas.</summary>
    internal static List<(FacturaCerrada F, FacturaEcf E)> Lista(IDbConnection cn, DateTime desde, DateTime hasta, string? t)
    {
        var r = new List<(FacturaCerrada, FacturaEcf)>();
        foreach (var walk in new[] { false, true })
        {
            var tabla = walk ? "HotelFacturaWalkingNC n JOIN HotelFacturaWalking o ON o.FacturaWalking_id = n.FacturaWalking_id"
                             : "HotelFacturaNC n JOIN HotelTmpFactura o ON o.Factura_id = n.Factura_id LEFT JOIN CxcCliente c ON c.NumeroCliente = o.Cliente_id";
            var nombre = walk ? "o.Nombre" : "c.Nombre";
            r.AddRange(cn.Query<(decimal Numero, DateTime Fecha, string? Ncf, decimal? Total, string? Nombre, string? Modifica, int? Hab)>($"""
                SELECT TOP 300 n.Devolucion_id, n.Fecha, RTRIM(n.NCF), n.Total, RTRIM({nombre}), RTRIM(o.NCF), {(walk ? "0" : "o.Habitacion_id")}
                FROM {tabla}
                WHERE n.Fecha >= @desde AND n.Fecha < @hasta AND n.NCF LIKE 'E34%'
                  AND (@t IS NULL OR RTRIM(n.NCF) = @t OR RTRIM(o.NCF) = @t OR CAST(n.Devolucion_id AS varchar(20)) = @t OR {nombre} LIKE '%' + @t + '%')
                ORDER BY n.Devolucion_id DESC
                """, new { desde = desde.Date, hasta = hasta.Date.AddDays(1), t = new DbString { Value = t, IsAnsi = true, Length = 60 } })
                .Select(x => (new FacturaCerrada((long)x.Numero, x.Fecha, x.Hab ?? 0, x.Nombre, null, x.Ncf, x.Total ?? 0, false, null, walk,
                        walk ? "NotaWalkIn" : "NotaHospedaje", x.Modifica),
                    new FacturaEcf(x.Ncf ?? "", (long)x.Numero, x.Fecha.Date, null, x.Nombre, x.Total ?? 0, 0, 0, false, Array.Empty<ItemEcf>()))));
        }
        return r;
    }
}
