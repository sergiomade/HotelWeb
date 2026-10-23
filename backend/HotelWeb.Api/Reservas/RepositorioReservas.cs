using System.Data;
using Dapper;
using HotelWeb.Api.Catalogos;
using HotelWeb.Api.Datos;
using HotelWeb.Api.Ecf;
using HotelWeb.Core.Ecf;
using HotelWeb.Core.Reservas;

namespace HotelWeb.Api.Reservas;

public sealed record ReservaResumen(long Id, string Cliente, string? Huesped, DateTime Entrada, DateTime Salida, int Noches,
    int Estado, string EstadoNombre, string? Habitaciones, int CantidadHabitaciones, decimal Adelantos, string? Telefono);

public sealed record LineaVista(int Secuencia, int Tipo, string TipoNombre, int Cantidad, int Adultos, int Ninos, int Bebes,
    bool Credito, string? Categoria, decimal Tarifa, decimal Importe, int? NoHabitacion);

public sealed record AdelantoVista(long Id, DateTime Fecha, decimal Monto, decimal? MontoUs, int FormaPago, string FormaPagoNombre,
    string Estatus, string? Usuario);

public sealed record Reserva(long Id, string Cliente, long? ClienteId, string? Contacto, string? Telefono, string? Huesped,
    string? Cedula, string? Pasaporte, string? Rnc, DateTime Entrada, DateTime Salida, int Noches, int Personas,
    string? Comentario, int Estado, string EstadoNombre, int? FormaPago, string? Categoria, string? Usuario, DateTime? Creada,
    IReadOnlyList<LineaVista> Lineas, IReadOnlyList<AdelantoVista> Adelantos)
{
    public decimal Total => Lineas.Sum(l => l.Importe);
    public decimal TotalAdelantos => Adelantos.Where(a => a.Estatus == ReglasAdelanto.Activa).Sum(a => a.Monto);
}

/// <summary>La confirmacion de la reserva para imprimir (como ppReport2 de UFrmMntReservacion), con los adelantos.</summary>
public sealed record ReservaImpresa(EmisorEcf Emisor, Reserva Reserva);

public sealed record Opcion(int Id, string Nombre);

public sealed record ReservaGuardada(Reserva Reserva, IReadOnlyList<string> Avisos);

/// <summary>
/// Reservas (HotelHabitacionReserva + HotelHabitacionReservada) y sus adelantos (HotelReservaAdelanto), sobre las
/// mismas tablas de Delphi. La reserva aparta TIPOS de habitacion; el numero se asigna en el check-in.
/// </summary>
public sealed class RepositorioReservas
{
    private readonly Conexiones _conexiones;

    public RepositorioReservas(Conexiones conexiones) => _conexiones = conexiones;

    private static DbString A(string? v, int largo) => new() { Value = v, IsAnsi = true, Length = largo };
    private static DbString Usuario(string u) => A(u.Length > 10 ? u[..10] : u, 10);

    // ---------------------------------------------------------------- consultas

    private sealed class FilaResumen
    {
        public decimal HabitacionReserva_id { get; set; }
        public string? NombreCliente { get; set; }
        public string? Huesped { get; set; }
        public DateTime FechaEntrada { get; set; }
        public DateTime FechaSalida { get; set; }
        public int CantidadNoches { get; set; }
        public int EstatusReservacion_id { get; set; }
        public string? Habitaciones { get; set; }
        public int? Cantidad { get; set; }
        public decimal? Adelantos { get; set; }
        public string? TelefonoContacto { get; set; }
    }

    /// <summary>
    /// Reservas por fecha de entrada (o de salida: la proyeccion de Delphi) entre dos fechas, texto (cliente, huesped
    /// o numero) y estado. Hasta 300. porAlta: por el dia en que se hizo la reserva (el tablero: "reservas de hoy").
    /// </summary>
    public IReadOnlyList<ReservaResumen> Buscar(DateTime desde, DateTime hasta, bool porSalida, string? texto, int? estado,
        bool porAlta = false)
    {
        texto = (texto ?? "").Trim();
        var campo = porAlta ? "r.fechaadicion" : porSalida ? "r.FechaSalida" : "r.FechaEntrada";
        var filtro = $"{campo} >= @desde AND {campo} < @hastaMas1";
        if (estado is > 0) filtro += " AND r.EstatusReservacion_id = @estado";
        if (texto.Length > 0)
            filtro += " AND (r.NombreCliente LIKE @like OR r.Huesped LIKE @like OR r.NombreContacto LIKE @like" +
                      (long.TryParse(texto, out _) ? " OR r.HabitacionReserva_id = @numero" : "") + ")";
        using var cn = _conexiones.Hotel();
        return cn.Query<FilaResumen>($"""
            SELECT TOP 300 r.HabitacionReserva_id, RTRIM(r.NombreCliente) AS NombreCliente, RTRIM(r.Huesped) AS Huesped,
                   r.FechaEntrada, r.FechaSalida, r.CantidadNoches, r.EstatusReservacion_id, RTRIM(r.TelefonoContacto) AS TelefonoContacto,
                   (SELECT SUM(d.Cantidad) FROM HotelHabitacionReservada d WITH (NOLOCK) WHERE d.HabitacionReserva_id = r.HabitacionReserva_id) AS Cantidad,
                   STUFF((SELECT ', ' + CAST(d.Cantidad AS varchar) + ' ' + RTRIM(d.Habitacion)
                          FROM HotelHabitacionReservada d WITH (NOLOCK) WHERE d.HabitacionReserva_id = r.HabitacionReserva_id
                          ORDER BY d.Secuencia FOR XML PATH('')), 1, 2, '') AS Habitaciones,
                   (SELECT SUM(a.Monto) FROM HotelReservaAdelanto a WITH (NOLOCK)
                     WHERE a.Reserva_id = r.HabitacionReserva_id AND RTRIM(a.Estatus) = 'Activa') AS Adelantos
            FROM HotelHabitacionReserva r WITH (NOLOCK)
            WHERE {filtro}
            ORDER BY {campo}, r.HabitacionReserva_id
            """, new
        {
            desde = desde.Date, hastaMas1 = hasta.Date.AddDays(1), estado,
            like = A("%" + texto.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%", 70),
            numero = long.TryParse(texto, out var n) ? n : 0,
        }).Select(f => new ReservaResumen((long)f.HabitacionReserva_id, f.NombreCliente ?? "", f.Huesped, f.FechaEntrada,
            f.FechaSalida, f.CantidadNoches, f.EstatusReservacion_id, EstadoReserva.Nombre(f.EstatusReservacion_id),
            f.Habitaciones, f.Cantidad ?? 0, f.Adelantos ?? 0, f.TelefonoContacto)).ToList();
    }

    private sealed class FilaReserva
    {
        public decimal HabitacionReserva_id { get; set; }
        public string? NombreCliente { get; set; }
        public decimal? Cliente_id { get; set; }
        public string? NombreContacto { get; set; }
        public string? TelefonoContacto { get; set; }
        public string? Huesped { get; set; }
        public string? Cedula { get; set; }
        public string? Passaporte { get; set; }
        public string? RNC { get; set; }
        public DateTime FechaEntrada { get; set; }
        public DateTime FechaSalida { get; set; }
        public int CantidadNoches { get; set; }
        public int CantidadPersona { get; set; }
        public string? Comentario { get; set; }
        public int EstatusReservacion_id { get; set; }
        public int? FormadePago_id { get; set; }
        public string? CategoriaPrecio_id { get; set; }
        public string? Usuario { get; set; }
        public DateTime? FechaAdicion { get; set; }
    }

    private sealed class FilaLinea
    {
        public int Secuencia { get; set; }
        public int Habitacion_id { get; set; }
        public string? Habitacion { get; set; }
        public int Cantidad { get; set; }
        public int Adultos { get; set; }
        public int Ninos { get; set; }
        public int Bebes { get; set; }
        public bool Credito { get; set; }
        public string? CategoriaPrecio_id { get; set; }
        public decimal Tarifa { get; set; }
        public decimal? Importe { get; set; }
        public int? NoHabitacion { get; set; }
    }

    private sealed class FilaAdelanto
    {
        public decimal ReservaAdelando_id { get; set; }
        public DateTime Fecha { get; set; }
        public decimal Monto { get; set; }
        public decimal? Montous { get; set; }
        public int Monedas_id { get; set; }
        public string? NombreMoneda { get; set; }
        public string? Estatus { get; set; }
        public string? Usuario { get; set; }
    }

    public ReservaImpresa? Impresa(long id)
    {
        if (Obtener(id) is not { } r) return null;
        using var cn = _conexiones.Hotel();
        return new ReservaImpresa(ServicioEcf.Emisor(cn), r);
    }

    public Reserva? Obtener(long id)
    {
        using var cn = _conexiones.Hotel();
        return Obtener(cn, null, id);
    }

    private static Reserva? Obtener(IDbConnection cn, IDbTransaction? tx, long id)
    {
        var r = cn.QueryFirstOrDefault<FilaReserva>("""
            SELECT HabitacionReserva_id, RTRIM(NombreCliente) AS NombreCliente, Cliente_id, RTRIM(NombreContacto) AS NombreContacto,
                   RTRIM(TelefonoContacto) AS TelefonoContacto, RTRIM(Huesped) AS Huesped, RTRIM(Cedula) AS Cedula,
                   RTRIM(Passaporte) AS Passaporte, RTRIM(RNC) AS RNC, FechaEntrada, FechaSalida, CantidadNoches, CantidadPersona,
                   CAST(Comentario AS varchar(1000)) AS Comentario, EstatusReservacion_id, FormadePago_id,
                   RTRIM(CategoriaPrecio_id) AS CategoriaPrecio_id, RTRIM(usuario) AS Usuario, fechaadicion AS FechaAdicion
            FROM HotelHabitacionReserva WHERE HabitacionReserva_id = @id
            """, new { id }, tx);
        if (r is null) return null;
        var lineas = cn.Query<FilaLinea>("""
            SELECT Secuencia, Habitacion_id, RTRIM(Habitacion) AS Habitacion, Cantidad, Adultos, Ninos, Bebes, Credito,
                   RTRIM(CategoriaPrecio_id) AS CategoriaPrecio_id, Tarifa, Importe, NoHabitacion
            FROM HotelHabitacionReservada WHERE HabitacionReserva_id = @id ORDER BY Secuencia
            """, new { id }, tx).Select(l => new LineaVista(l.Secuencia, l.Habitacion_id, l.Habitacion ?? "", l.Cantidad,
            l.Adultos, l.Ninos, l.Bebes, l.Credito, l.CategoriaPrecio_id, l.Tarifa, l.Importe ?? 0,
            l.NoHabitacion is > 0 ? l.NoHabitacion : null)).ToList();
        var adelantos = cn.Query<FilaAdelanto>("""
            SELECT a.ReservaAdelando_id, a.Fecha, a.Monto, a.Montous, a.Monedas_id, RTRIM(m.NombreMoneda) AS NombreMoneda,
                   RTRIM(a.Estatus) AS Estatus, RTRIM(a.Usuario) AS Usuario
            FROM HotelReservaAdelanto a LEFT JOIN ConMonedas m ON m.Monedas_id = a.Monedas_id
            WHERE a.Reserva_id = @id ORDER BY a.ReservaAdelando_id
            """, new { id }, tx).Select(a => new AdelantoVista((long)a.ReservaAdelando_id, a.Fecha, a.Monto, a.Montous,
            a.Monedas_id, a.NombreMoneda ?? a.Monedas_id.ToString(), a.Estatus ?? "", a.Usuario)).ToList();
        return new Reserva((long)r.HabitacionReserva_id, r.NombreCliente ?? "", (long?)r.Cliente_id, Vacio(r.NombreContacto),
            r.TelefonoContacto, r.Huesped, Sin(r.Cedula), Sin(r.Passaporte), Sin(r.RNC), r.FechaEntrada, r.FechaSalida,
            r.CantidadNoches, r.CantidadPersona, r.Comentario, r.EstatusReservacion_id, EstadoReserva.Nombre(r.EstatusReservacion_id),
            r.FormadePago_id, r.CategoriaPrecio_id, r.Usuario, r.FechaAdicion, lineas, adelantos);
    }

    /// <summary>Habitaciones libres por tipo entre dos fechas (sin contar la reserva <paramref name="excluir"/>).</summary>
    public IReadOnlyList<Disponibilidad> Disponibilidad(DateTime entrada, DateTime salida, long? excluir = null)
    {
        using var cn = _conexiones.Hotel();
        return Disponibilidad(cn, null, entrada, salida, excluir);
    }

    private static IReadOnlyList<Disponibilidad> Disponibilidad(IDbConnection cn, IDbTransaction? tx, DateTime entrada,
        DateTime salida, long? excluir)
    {
        // ocupadas: cuenta abierta en una habitacion del tipo cuya salida (de su reserva) cae despues de la entrada
        // pedida (sin reserva o sin fecha: se cuenta ocupada). Reservadas: reservas activas del tipo que se cruzan.
        return cn.Query<(int Tipo, string Descripcion, int Habitaciones, int Ocupadas, int Reservadas)>("""
            SELECT t.HabitacionTipo, RTRIM(t.Descripcion),
                   (SELECT COUNT(*) FROM hotelHabitacion h JOIN HotelHabitacionEstado e ON e.HabitacionEstado_id = h.HabitacionEstado_id
                     WHERE h.HabitacionTipo = t.HabitacionTipo AND e.Disponible = 1),
                   (SELECT COUNT(DISTINCT f.Habitacion_id) FROM HotelFactura f WITH (NOLOCK)
                      JOIN hotelHabitacion h ON h.Habitacion_id = f.Habitacion_id
                      LEFT JOIN HotelHabitacionReserva r WITH (NOLOCK) ON r.HabitacionReserva_id = f.HabitacionReserva_id
                     WHERE h.HabitacionTipo = t.HabitacionTipo AND ISNULL(dbo.Fn_SalidaCuenta(f.Factura_id), @salida) > @entrada),
                   ISNULL((SELECT SUM(d.Cantidad) FROM HotelHabitacionReservada d WITH (NOLOCK)
                             JOIN HotelHabitacionReserva r WITH (NOLOCK) ON r.HabitacionReserva_id = d.HabitacionReserva_id
                            WHERE d.Habitacion_id = t.HabitacionTipo AND r.EstatusReservacion_id = 1
                              AND r.FechaEntrada < @salida AND r.FechaSalida > @entrada
                              AND r.HabitacionReserva_id <> @excluir), 0)
            FROM HotelHabitacionTipo t ORDER BY t.HabitacionTipo
            """, new { entrada = entrada.Date, salida = salida.Date, excluir = excluir ?? -1 }, tx)
            .Select(x => new Disponibilidad(x.Tipo, x.Descripcion, x.Habitaciones, x.Ocupadas, x.Reservadas)).ToList();
    }

    public IReadOnlyList<Opcion> FormasPagoReserva()
    {
        using var cn = _conexiones.Hotel();
        return cn.Query<(int, string)>("SELECT Formapago_id, RTRIM(Formadepago) FROM faFormadePago ORDER BY Formapago_id")
            .Select(x => new Opcion(x.Item1, x.Item2)).ToList();
    }

    /// <summary>Formas de pago de los adelantos (ConMonedas, como Delphi).</summary>
    public IReadOnlyList<Opcion> FormasPagoAdelanto()
    {
        using var cn = _conexiones.Hotel();
        return cn.Query<(int, string)>("SELECT Monedas_id, RTRIM(NombreMoneda) FROM ConMonedas ORDER BY Monedas_id")
            .Select(x => new Opcion(x.Item1, x.Item2)).ToList();
    }

    // ---------------------------------------------------------------- escrituras

    public ReservaGuardada Crear(DatosReserva datos, string usuario, DateTime ahora)
    {
        var d = Validado(datos, ahora, nueva: true);
        using var cn = _conexiones.Hotel();
        cn.Open();
        using var tx = cn.BeginTransaction();
        var avisos = ReglasReserva.Sobreventa(d, Disponibilidad(cn, tx, d.Entrada, d.Salida, null));
        // mayor + 1, como Delphi (Maximo); UPDLOCK para que dos a la vez no tomen el mismo numero
        var id = cn.ExecuteScalar<long>(
            "SELECT ISNULL(MAX(HabitacionReserva_id), 0) + 1 FROM HotelHabitacionReserva WITH (UPDLOCK, HOLDLOCK)", transaction: tx);
        var p = Encabezado(id, d, usuario);
        cn.Execute("""
            INSERT INTO HotelHabitacionReserva (HabitacionReserva_id, NombreCliente, NombreContacto, TelefonoContacto, Huesped,
                   Cliente_id, Cedula, Passaporte, RNC, CantidadNoches, CantidadHabitacion, FechaEntrada, FechaSalida,
                   CantidadPersona, Comentario, EstatusReservacion_id, FormadePago_id, CategoriaPrecio_id, usuario, fechaadicion)
            VALUES (@id, @cliente, @contacto, @telefono, @huesped, @clienteId, @cedula, @pasaporte, @rnc, @noches, @habitaciones,
                    @entrada, @salida, @personas, @comentario, 1, @formaPago, @categoria, @usuario, GETDATE())
            """, p, tx);
        InsertarLineas(cn, tx, id, d);
        tx.Commit();
        return new ReservaGuardada(Obtener(cn, null, id)!, avisos);
    }

    public ReservaGuardada Modificar(long id, DatosReserva datos, string usuario, DateTime ahora)
    {
        var d = Validado(datos, ahora, nueva: false);
        using var cn = _conexiones.Hotel();
        cn.Open();
        using var tx = cn.BeginTransaction();
        var estado = cn.ExecuteScalar<int?>(
            "SELECT EstatusReservacion_id FROM HotelHabitacionReserva WITH (UPDLOCK) WHERE HabitacionReserva_id = @id", new { id }, tx)
            ?? throw new ErrorCatalogo($"No existe la reserva {id}.");
        if (estado != EstadoReserva.Activa)
            throw new ErrorCatalogo($"La reserva esta {EstadoReserva.Nombre(estado).ToLowerInvariant()}: ya no se modifica.");
        var avisos = ReglasReserva.Sobreventa(d, Disponibilidad(cn, tx, d.Entrada, d.Salida, id));
        cn.Execute("""
            UPDATE HotelHabitacionReserva
               SET NombreCliente = @cliente, NombreContacto = @contacto, TelefonoContacto = @telefono, Huesped = @huesped,
                   Cliente_id = @clienteId, Cedula = @cedula, Passaporte = @pasaporte, RNC = @rnc, CantidadNoches = @noches,
                   CantidadHabitacion = @habitaciones, FechaEntrada = @entrada, FechaSalida = @salida, CantidadPersona = @personas,
                   Comentario = @comentario, FormadePago_id = @formaPago, CategoriaPrecio_id = @categoria, usuario = @usuario
             WHERE HabitacionReserva_id = @id
            """, Encabezado(id, d, usuario), tx);
        // las lineas se reemplazan, como el UpdateBatch de Delphi (el numero de habitacion se asigna en el check-in)
        cn.Execute("DELETE HotelHabitacionReservada WHERE HabitacionReserva_id = @id", new { id }, tx);
        InsertarLineas(cn, tx, id, d);
        tx.Commit();
        return new ReservaGuardada(Obtener(cn, null, id)!, avisos);
    }

    /// <summary>Cancelar = estado 2, como el boton Anular de Delphi (no se borra nada).</summary>
    public Reserva Cancelar(long id, bool aunConAdelantos)
    {
        using var cn = _conexiones.Hotel();
        cn.Open();
        using var tx = cn.BeginTransaction();
        var r = Obtener(cn, tx, id) ?? throw new ErrorCatalogo($"No existe la reserva {id}.");
        if (r.Estado != EstadoReserva.Activa)
            throw new ErrorCatalogo($"La reserva esta {r.EstadoNombre.ToLowerInvariant()}: no se cancela.");
        if (r.TotalAdelantos > 0 && !aunConAdelantos)
            throw new ErrorConfirmar($"La reserva tiene adelantos activos por {r.TotalAdelantos:N2}. Anulelos o devuelvalos, o confirme que la cancela igual.");
        cn.Execute("UPDATE HotelHabitacionReserva SET EstatusReservacion_id = 2 WHERE HabitacionReserva_id = @id", new { id }, tx);
        tx.Commit();
        return Obtener(cn, null, id)!;
    }

    public Reserva RegistrarAdelanto(long id, decimal monto, int formaPago, decimal? montoUs, string usuario)
    {
        using var cn = _conexiones.Hotel();
        cn.Open();
        using var tx = cn.BeginTransaction();
        var r = cn.QueryFirstOrDefault<(int Estado, decimal? Cliente)>(
            "SELECT EstatusReservacion_id, Cliente_id FROM HotelHabitacionReserva WITH (UPDLOCK) WHERE HabitacionReserva_id = @id",
            new { id }, tx);
        if (r == default) throw new ErrorCatalogo($"No existe la reserva {id}.");
        if (ReglasAdelanto.Validar(monto, formaPago, montoUs, r.Estado) is { } motivo) throw new ErrorCatalogo(motivo);
        if (cn.ExecuteScalar<int>("SELECT COUNT(*) FROM ConMonedas WHERE Monedas_id = @formaPago", new { formaPago }, tx) == 0)
            throw new ErrorCatalogo("Esa forma de pago no existe.");
        // como Delphi (QCambioDivisaNewRecord): fecha de hoy sin hora, el cliente de la reserva y estado Activa.
        // Posteado = 0, como Delphi (el valor por omision de la tabla): lo toma el cuadre de caja y lo marca al cerrar.
        // (Hasta 2026-10-23 la web ponia 1 y sus adelantos no entraban en ningun cuadre.)
        cn.Execute("""
            INSERT INTO HotelReservaAdelanto (Fecha, Reserva_id, Cliente_id, Monto, Montous, Monedas_id, Usuario, Estatus, Posteado)
            VALUES (CAST(GETDATE() AS date), @id, @cliente, @monto, @montoUs, @formaPago, @usuario, 'Activa', 0)
            """, new { id, cliente = r.Cliente ?? 0, monto, montoUs = formaPago == ReglasAdelanto.Dolares ? montoUs : null,
                       formaPago, usuario = Usuario(usuario) }, tx);
        tx.Commit();
        return Obtener(cn, null, id)!;
    }

    public Reserva AnularAdelanto(long id, long adelanto)
    {
        using var cn = _conexiones.Hotel();
        cn.Open();
        using var tx = cn.BeginTransaction();
        var estado = cn.ExecuteScalar<int?>(
            "SELECT EstatusReservacion_id FROM HotelHabitacionReserva WITH (UPDLOCK) WHERE HabitacionReserva_id = @id", new { id }, tx)
            ?? throw new ErrorCatalogo($"No existe la reserva {id}.");
        var estatus = cn.ExecuteScalar<string?>(
            "SELECT Estatus FROM HotelReservaAdelanto WHERE ReservaAdelando_id = @adelanto AND Reserva_id = @id", new { id, adelanto }, tx)
            ?? throw new ErrorCatalogo($"El adelanto {adelanto} no es de la reserva {id}.");
        if (ReglasAdelanto.ValidarAnular(estatus, estado) is { } motivo) throw new ErrorCatalogo(motivo);
        // anulado antes del cierre: no entra en ningun cuadre (tampoco en el de Delphi, que no mira el estatus)
        cn.Execute("UPDATE HotelReservaAdelanto SET Estatus = 'Anulada', Posteado = 1 WHERE ReservaAdelando_id = @adelanto", new { adelanto }, tx);
        tx.Commit();
        return Obtener(cn, null, id)!;
    }

    // ---------------------------------------------------------------- ayudas

    private static DatosReserva Validado(DatosReserva datos, DateTime ahora, bool nueva)
    {
        var d = ReglasReserva.Normalizar(datos);
        if (ReglasReserva.Validar(d, ahora, nueva) is { } motivo) throw new ErrorCatalogo(motivo);
        return d;
    }

    private static object Encabezado(long id, DatosReserva d, string usuario) => new
    {
        id,
        cliente = A(d.NombreCliente, 60),
        // NombreContacto es NOT NULL: vacio si no hay (Delphi deja "0" o el nombre)
        contacto = A(d.Contacto ?? "", 60),
        telefono = A(d.Telefono, 13),
        huesped = A(d.Huesped, 60),
        clienteId = d.ClienteId,
        cedula = A(d.Cedula ?? "00000000000", 13),   // como Delphi: vacio = once ceros
        pasaporte = A(d.Pasaporte, 15),
        rnc = A(d.Rnc ?? "000000000", 13),           // como Delphi: vacio = nueve ceros
        noches = ReglasReserva.Noches(d.Entrada, d.Salida),
        habitaciones = ReglasReserva.Habitaciones(d.Lineas),
        personas = ReglasReserva.Personas(d.Lineas),
        entrada = d.Entrada,
        salida = d.Salida,
        comentario = A(d.Comentario, -1),
        formaPago = d.FormaPago > 0 ? d.FormaPago : 1,
        categoria = A(d.Categoria, 1),
        usuario = Usuario(usuario),
    };

    private static void InsertarLineas(IDbConnection cn, IDbTransaction tx, long id, DatosReserva d)
    {
        var noches = ReglasReserva.Noches(d.Entrada, d.Salida);
        var tipos = cn.Query<(int Id, string Nombre)>("SELECT HabitacionTipo, RTRIM(Descripcion) FROM HotelHabitacionTipo", transaction: tx)
            .ToDictionary(t => t.Id, t => t.Nombre);
        foreach (var l in d.Lineas)
        {
            if (!tipos.TryGetValue(l.Tipo, out var nombre)) throw new ErrorCatalogo($"No existe el tipo de habitacion {l.Tipo}.");
            cn.Execute("""
                INSERT INTO HotelHabitacionReservada (HabitacionReserva_id, Habitacion_id, Habitacion, Cantidad, Adultos, Ninos, Bebes,
                                                      Credito, Tarifa, Importe, CategoriaPrecio_id, NoHabitacion)
                VALUES (@id, @tipo, @nombre, @cantidad, @adultos, @ninos, @bebes, @credito, @tarifa, @importe, @categoria, 0)
                """, new
            {
                id, tipo = l.Tipo, nombre = A(nombre.Length > 30 ? nombre[..30] : nombre, 30), cantidad = l.Cantidad,
                adultos = l.Adultos, ninos = l.Ninos, bebes = l.Bebes, credito = l.Credito, tarifa = l.Tarifa,
                importe = ReglasReserva.Importe(l, noches), categoria = A(l.Categoria, 1),
            }, tx);
        }
    }

    private static string? Vacio(string? s) => string.IsNullOrWhiteSpace(s) || s.Trim() == "0" ? null : s.Trim();
    private static string? Sin(string? s) => string.IsNullOrWhiteSpace(s) || s.Trim().All(c => c == '0') ? null : s.Trim();
}

/// <summary>Se puede hacer, pero hay que confirmarlo (la API responde 409).</summary>
public sealed class ErrorConfirmar(string mensaje) : Exception(mensaje);
