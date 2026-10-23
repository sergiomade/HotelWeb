using System.Data;
using Dapper;
using HotelWeb.Api.Catalogos;
using HotelWeb.Api.Datos;
using HotelWeb.Core.Hospedaje;
using HotelWeb.Core.Reservas;

namespace HotelWeb.Api.Hospedaje;

public sealed record Llegada(long Reserva, string Cliente, long? ClienteId, string? Huesped, DateTime Entrada, DateTime Salida,
    int Habitaciones, string? Detalle, decimal Adelantos, bool Atrasada);

public sealed record LugarVista(int Secuencia, int Indice, int Tipo, string TipoNombre, string? Categoria, decimal Tarifa,
    int Adultos, int Ninos, int Bebes, bool Credito, int? Sugerida);

public sealed record LibreVista(int Numero, int Tipo, string TipoNombre, bool Disponible);

public sealed record PreparacionCheckIn(long Reserva, string Cliente, long? ClienteId, string? Huesped, DateTime Entrada,
    DateTime Salida, int Noches, decimal Adelantos, IReadOnlyList<LugarVista> Lugares, IReadOnlyList<LibreVista> Libres);

public sealed record CuentaAbierta(int Habitacion, long Factura);

public sealed record ResultadoCheckIn(long Reserva, IReadOnlyList<CuentaAbierta> Cuentas, IReadOnlyList<string> Avisos);

/// <summary>
/// Check-in desde la reserva, con el procedimiento de Delphi (STP_CheckInHabitaciones): abre la cuenta de cada
/// habitacion (HotelFactura), aplica los adelantos activos (MIG_23) y pone la reserva en Hospedado.
/// </summary>
public sealed class RepositorioCheckIn
{
    private readonly Conexiones _conexiones;

    public RepositorioCheckIn(Conexiones conexiones) => _conexiones = conexiones;

    private static DbString A(string? v, int largo) => new() { Value = v, IsAnsi = true, Length = largo };

    /// <summary>Reservas activas que ya deberian entrar: entrada hasta hoy (las atrasadas tambien) y salida despues de hoy.</summary>
    public IReadOnlyList<Llegada> Llegadas(DateTime hoy)
    {
        using var cn = _conexiones.Hotel();
        return cn.Query<(decimal Id, string Cliente, decimal? ClienteId, string? Huesped, DateTime Entrada, DateTime Salida,
                int? Habitaciones, string? Detalle, decimal? Adelantos)>("""
            SELECT r.HabitacionReserva_id, RTRIM(r.NombreCliente), r.Cliente_id, RTRIM(r.Huesped), r.FechaEntrada, r.FechaSalida,
                   (SELECT SUM(d.Cantidad) FROM HotelHabitacionReservada d WHERE d.HabitacionReserva_id = r.HabitacionReserva_id),
                   STUFF((SELECT ', ' + CAST(d.Cantidad AS varchar) + ' ' + RTRIM(d.Habitacion) FROM HotelHabitacionReservada d
                          WHERE d.HabitacionReserva_id = r.HabitacionReserva_id ORDER BY d.Secuencia FOR XML PATH('')), 1, 2, ''),
                   (SELECT SUM(a.Monto) FROM HotelReservaAdelanto a WHERE a.Reserva_id = r.HabitacionReserva_id AND RTRIM(a.Estatus) = 'Activa')
            FROM HotelHabitacionReserva r WITH (NOLOCK)
            WHERE r.EstatusReservacion_id = 1 AND r.FechaEntrada <= @hoy AND r.FechaSalida > @hoy
            ORDER BY r.FechaEntrada, r.HabitacionReserva_id
            """, new { hoy = hoy.Date })
            .Select(x => new Llegada((long)x.Id, x.Cliente ?? "", (long?)x.ClienteId is > 0 ? (long?)x.ClienteId : null, x.Huesped,
                x.Entrada, x.Salida, x.Habitaciones ?? 0, x.Detalle, x.Adelantos ?? 0, x.Entrada.Date < hoy.Date)).ToList();
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
        public decimal Tarifa { get; set; }
        public string? CategoriaPrecio_id { get; set; }
    }

    private sealed class FilaReserva
    {
        public decimal HabitacionReserva_id { get; set; }
        public string? NombreCliente { get; set; }
        public decimal? Cliente_id { get; set; }
        public string? Huesped { get; set; }
        public DateTime FechaEntrada { get; set; }
        public DateTime FechaSalida { get; set; }
        public int EstatusReservacion_id { get; set; }
    }

    private static FilaReserva LeerReserva(IDbConnection cn, IDbTransaction? tx, long reserva, bool bloquear) =>
        cn.QueryFirstOrDefault<FilaReserva>($"""
            SELECT HabitacionReserva_id, RTRIM(NombreCliente) AS NombreCliente, Cliente_id, RTRIM(Huesped) AS Huesped,
                   FechaEntrada, FechaSalida, EstatusReservacion_id
            FROM HotelHabitacionReserva {(bloquear ? "WITH (UPDLOCK)" : "")} WHERE HabitacionReserva_id = @reserva
            """, new { reserva }, tx) ?? throw new ErrorCatalogo($"No existe la reserva {reserva}.");

    private static List<FilaLinea> Lineas(IDbConnection cn, IDbTransaction? tx, long reserva) =>
        cn.Query<FilaLinea>("""
            SELECT Secuencia, Habitacion_id, RTRIM(Habitacion) AS Habitacion, Cantidad, Adultos, Ninos, Bebes, Credito, Tarifa,
                   RTRIM(CategoriaPrecio_id) AS CategoriaPrecio_id
            FROM HotelHabitacionReservada WHERE HabitacionReserva_id = @reserva ORDER BY Secuencia
            """, new { reserva }, tx).ToList();

    /// <summary>Habitaciones sin cuenta abierta. Dentro de una transaccion, bloquea para que nadie las ocupe mientras tanto.</summary>
    private static List<(int Numero, int Tipo, string TipoNombre, bool Disponible)> Libres(IDbConnection cn, IDbTransaction? tx) =>
        cn.Query<(decimal Numero, int Tipo, string TipoNombre, bool? Disponible)>($"""
            SELECT h.Habitacion_id, h.HabitacionTipo, RTRIM(t.Descripcion), e.Disponible
            FROM hotelHabitacion h
            JOIN HotelHabitacionTipo t ON t.HabitacionTipo = h.HabitacionTipo
            JOIN HotelHabitacionEstado e ON e.HabitacionEstado_id = h.HabitacionEstado_id
            WHERE NOT EXISTS (SELECT 1 FROM HotelFactura f {(tx is null ? "WITH (NOLOCK)" : "WITH (UPDLOCK, HOLDLOCK)")}
                              WHERE f.Habitacion_id = h.Habitacion_id)
            ORDER BY h.Habitacion_id
            """, transaction: tx).Select(x => ((int)x.Numero, x.Tipo, x.TipoNombre, x.Disponible == true)).ToList();

    public PreparacionCheckIn Preparar(long reserva)
    {
        using var cn = _conexiones.Hotel();
        var r = LeerReserva(cn, null, reserva, false);
        if (r.EstatusReservacion_id != EstadoReserva.Activa)
            throw new ErrorCatalogo($"La reserva esta {EstadoReserva.Nombre(r.EstatusReservacion_id).ToLowerInvariant()}: no se le hace check-in.");
        var lineas = Lineas(cn, null, reserva);
        var libres = Libres(cn, null);
        var lugares = ReglasCheckIn.Lugares(lineas.Select(l => (l.Secuencia, l.Habitacion_id, l.Cantidad)));
        var sugeridas = ReglasCheckIn.Sugerir(lugares, libres.Select(x => new HabitacionLibre(x.Numero, x.Tipo, x.Disponible)).ToList());
        var adelantos = cn.ExecuteScalar<decimal?>(
            "SELECT SUM(Monto) FROM HotelReservaAdelanto WHERE Reserva_id = @reserva AND RTRIM(Estatus) = 'Activa'", new { reserva }) ?? 0;
        return new PreparacionCheckIn(reserva, r.NombreCliente ?? "", (long?)r.Cliente_id is > 0 ? (long?)r.Cliente_id : null, r.Huesped,
            r.FechaEntrada, r.FechaSalida, ReglasReserva.Noches(r.FechaEntrada, r.FechaSalida), adelantos,
            lugares.Select(l =>
            {
                var ln = lineas.First(x => x.Secuencia == l.Secuencia);
                return new LugarVista(l.Secuencia, l.Indice, l.Tipo, ln.Habitacion ?? "", ln.CategoriaPrecio_id, ln.Tarifa, ln.Adultos,
                    ln.Ninos, ln.Bebes, ln.Credito, sugeridas.FirstOrDefault(s => s.Secuencia == l.Secuencia && s.Indice == l.Indice)?.Habitacion);
            }).ToList(),
            libres.Select(x => new LibreVista(x.Numero, x.Tipo, x.TipoNombre, x.Disponible)).ToList());
    }

    private static LineaReservada Original(FilaLinea l) =>
        new(l.Secuencia, l.Habitacion_id, l.CategoriaPrecio_id ?? "A", l.Tarifa, l.Cantidad);

    /// <summary>
    /// Cada habitacion con su categoria y su tarifa: si recepcion no mando tarifa y cambio el tipo o la categoria, la de
    /// la tabla de precios (sin impuestos, como la reserva); si no cambio nada, la reservada.
    /// </summary>
    private static List<Asignacion> ConTarifa(IDbConnection cn, IDbTransaction tx, List<FilaLinea> lineas,
        IReadOnlyList<Asignacion> elegidas, Func<int, int> tipoDe, Dictionary<(int, string), decimal> precios)
    {
        var categorias = cn.Query<string>("SELECT UPPER(RTRIM(CategoriaPrecio_id)) FROM HotelCategoriaPrecio", transaction: tx).ToHashSet();
        var r = new List<Asignacion>();
        foreach (var a in elegidas)
        {
            var l = lineas.First(x => x.Secuencia == a.Secuencia);
            var reservada = (l.CategoriaPrecio_id ?? "A").Trim().ToUpperInvariant();
            var categoria = string.IsNullOrWhiteSpace(a.Categoria) ? reservada : a.Categoria.Trim().ToUpperInvariant();
            if (categoria != reservada && !categorias.Contains(categoria)) throw new ErrorCatalogo($"No existe la categoria de precio {categoria}.");
            var tipo = tipoDe(a.Habitacion);
            var tarifa = a.Tarifa;
            if (tarifa is null)
            {
                if (tipo == l.Habitacion_id && categoria == reservada) tarifa = l.Tarifa;
                else if (precios.TryGetValue((tipo, categoria), out var p)) tarifa = p;
                else throw new ErrorCatalogo($"La habitacion {a.Habitacion} no tiene precio en la categoria {categoria}: pongalo en Precios o escriba la tarifa.");
            }
            if (tarifa < 0 || tarifa > 999_999_999m) throw new ErrorCatalogo($"La tarifa de la habitacion {a.Habitacion} no es valida.");
            r.Add(a with { Categoria = categoria, Tarifa = tarifa });
        }
        return r;
    }

    public ResultadoCheckIn Hacer(long reserva, IReadOnlyList<Asignacion> elegidas, long? clienteId, string usuario)
    {
        using var cn = _conexiones.Hotel();
        cn.Open();
        using var tx = cn.BeginTransaction(IsolationLevel.Serializable);
        var r = LeerReserva(cn, tx, reserva, true);
        if (r.EstatusReservacion_id != EstadoReserva.Activa)
            throw new ErrorCatalogo($"La reserva esta {EstadoReserva.Nombre(r.EstatusReservacion_id).ToLowerInvariant()}: no se le hace check-in.");

        // Delphi no deja hacer el check-in sin cliente (CmdAmarrarCliente lo pone en la reserva)
        if (clienteId is > 0)
        {
            if (cn.ExecuteScalar<int>("SELECT COUNT(*) FROM CxcCliente WHERE NumeroCliente = @clienteId", new { clienteId }, tx) == 0)
                throw new ErrorCatalogo($"No existe el cliente {clienteId}.");
            cn.Execute("UPDATE HotelHabitacionReserva SET Cliente_id = @clienteId WHERE HabitacionReserva_id = @reserva",
                new { clienteId, reserva }, tx);
        }
        else if (r.Cliente_id is null or 0)
            throw new ErrorCatalogo("La reserva no tiene cliente: escojalo antes del check-in (lo pide Delphi para la factura).");

        var lineas = Lineas(cn, tx, reserva);
        var libres = Libres(cn, tx).Select(x => new HabitacionLibre(x.Numero, x.Tipo, x.Disponible)).ToList();
        var lugares = ReglasCheckIn.Lugares(lineas.Select(l => (l.Secuencia, l.Habitacion_id, l.Cantidad)));
        if (ReglasCheckIn.Validar(lugares, elegidas, libres) is { } motivo) throw new ErrorCatalogo(motivo);
        var tipos = cn.Query<(int, string)>("SELECT HabitacionTipo, RTRIM(Descripcion) FROM HotelHabitacionTipo", transaction: tx)
            .ToDictionary(t => t.Item1, t => t.Item2);
        var adelanto = cn.ExecuteScalar<decimal?>(
            "SELECT SUM(Monto) FROM HotelReservaAdelanto WHERE Reserva_id = @reserva AND RTRIM(Estatus) = 'Activa'", new { reserva }, tx) ?? 0;

        // el precio es el del tipo de la habitacion que se da: la linea de la reserva se ajusta (o se separa) antes del
        // check-in, porque STP_CheckInHabitaciones y la carga de cada noche toman la tarifa de ahi
        var tipoDe = libres.ToDictionary(x => x.Numero, x => x.Tipo);
        var precios = cn.Query<(int Tipo, string Categoria, decimal Precio)>(
                "SELECT HabitacionTipo_id, UPPER(RTRIM(CategoriaPrecio_id)), PrecioHabitacion FROM HotelHabitacionTipoPrecio", transaction: tx)
            .ToDictionary(p => (p.Tipo, p.Categoria), p => p.Precio);
        var completas = ConTarifa(cn, tx, lineas, elegidas, n => tipoDe[n], precios);
        // se le respeta el precio acordado: otro tipo MAS CARO (en la categoria reservada) cobrado con la categoria y la
        // tarifa reservadas. Protege al cliente; si la habitacion es mas barata, se cobra la mas barata.
        var avisos = ReglasCheckIn.Avisos(lugares, completas, libres, t => tipos.TryGetValue(t, out var n) ? n : $"tipo {t}",
            a =>
            {
                var l = lineas.First(x => x.Secuencia == a.Secuencia);
                var cat = (l.CategoriaPrecio_id ?? "A").Trim().ToUpperInvariant();
                var mayor = !precios.TryGetValue((tipoDe[a.Habitacion], cat), out var nuevo) || nuevo > l.Tarifa;
                return mayor && a.Tarifa == l.Tarifa && a.Categoria == cat;
            });
        var finales = RepartoCheckIn.Repartir(lineas.Select(Original).ToList(), completas, n => tipoDe[n]);
        if (ReglasCambioHabitacion.Conflicto(finales.Select(f => (f.Tipo, f.Categoria, f.Tarifa)),
                t => tipos.TryGetValue(t, out var n) ? n : $"tipo {t}") is { } conflicto)
            throw new ErrorCatalogo(conflicto);
        var noches = ReglasReserva.Noches(r.FechaEntrada, r.FechaSalida);
        var filas = new List<(FilaLinea Linea, int Numero)>();
        foreach (var f in finales)
        {
            var l = lineas.First(x => x.Secuencia == f.Secuencia);
            var secuencia = l.Secuencia;
            var nombre = tipos.TryGetValue(f.Tipo, out var tn) ? tn : $"tipo {f.Tipo}";
            var datos = new
            {
                reserva, original = l.Secuencia, tipoViejo = l.Habitacion_id, tipo = f.Tipo,
                nombre = A(nombre.Length > 30 ? nombre[..30] : nombre, 30), cantidad = f.Cantidad, categoria = A(f.Categoria, 1),
                tarifa = f.Tarifa, importe = Math.Round(f.Cantidad * f.Tarifa * noches, 2),
            };
            if (f.Nueva)
                secuencia = cn.ExecuteScalar<int>("""
                    INSERT INTO HotelHabitacionReservada (HabitacionReserva_id, Habitacion_id, Habitacion, Cantidad, Adultos, Ninos, Bebes,
                                                          Credito, Tarifa, Importe, CategoriaPrecio_id, NoHabitacion)
                    SELECT HabitacionReserva_id, @tipo, @nombre, @cantidad, Adultos, Ninos, Bebes, Credito, @tarifa, @importe, @categoria, 0
                    FROM HotelHabitacionReservada WHERE HabitacionReserva_id = @reserva AND Secuencia = @original;
                    SELECT CAST(SCOPE_IDENTITY() AS int);
                    """, datos, tx);
            else if (!RepartoCheckIn.SinCambios(f, Original(l)))
                cn.Execute("""
                    UPDATE HotelHabitacionReservada
                    SET Habitacion_id = @tipo, Habitacion = @nombre, Cantidad = @cantidad, CategoriaPrecio_id = @categoria,
                        Tarifa = @tarifa, Importe = @importe
                    WHERE HabitacionReserva_id = @reserva AND Secuencia = @original AND Habitacion_id = @tipoViejo
                    """, datos, tx);
            var final = new FilaLinea
            {
                Secuencia = secuencia, Habitacion_id = f.Tipo, Habitacion = nombre, Cantidad = f.Cantidad, Adultos = l.Adultos,
                Ninos = l.Ninos, Bebes = l.Bebes, Credito = l.Credito, Tarifa = f.Tarifa, CategoriaPrecio_id = f.Categoria,
            };
            filas.AddRange(f.Habitaciones.Select(a => (final, a.Habitacion)));
        }

        // HotelHabitacionReservadaTmp es una sola tabla para todos: solo las filas de ESTA reserva
        cn.Execute("DELETE HotelHabitacionReservadaTmp WHERE HabitacionReserva_id = @reserva", new { reserva }, tx);
        var primera = true;
        foreach (var (l, numero) in filas)
        {
            // mismas columnas que llena STP_CrearHabitacionesReserva (Importe = la tarifa; el adelanto en la primera)
            cn.Execute("""
                INSERT INTO HotelHabitacionReservadaTmp (HabitacionReserva_id, Habitacion_id, Secuencia, Habitacion, NOHabitacion, Huesped,
                       Adultos, Ninos, Bebes, Credito, Importe, Adelanto, CategoriaPrecio_id, Tarifa, Cantidad)
                VALUES (@reserva, @tipo, @secuencia, @habitacion, @numero, @huesped, @adultos, @ninos, @bebes, @credito, @tarifa,
                        @adelanto, @categoria, @tarifa, @cantidad)
                """, new
            {
                reserva, tipo = l.Habitacion_id, secuencia = l.Secuencia, habitacion = A(l.Habitacion, 40), numero,
                huesped = A(r.Huesped is { Length: > 50 } h ? h[..50] : r.Huesped, 50), adultos = l.Adultos, ninos = l.Ninos,
                bebes = l.Bebes, credito = l.Credito, tarifa = l.Tarifa, adelanto = primera ? adelanto : 0m,
                categoria = A(l.CategoriaPrecio_id, 1), cantidad = l.Cantidad,
            }, tx);
            primera = false;
        }

        cn.Execute("STP_CheckInHabitaciones",
            new { HabitacionReserva = reserva, Usuario = A(usuario.Length > 10 ? usuario[..10] : usuario, 10), Abonoacuenta = 0m },
            tx, commandType: CommandType.StoredProcedure);
        cn.Execute("DELETE HotelHabitacionReservadaTmp WHERE HabitacionReserva_id = @reserva", new { reserva }, tx);

        var numeros = elegidas.Select(e => e.Habitacion).ToArray();
        var cuentas = cn.Query<(decimal Hab, decimal Factura)>(
            "SELECT Habitacion_id, Factura_id FROM HotelFactura WHERE HabitacionReserva_id = @reserva AND Habitacion_id IN @numeros",
            new { reserva, numeros }, tx).Select(c => new CuentaAbierta((int)c.Hab, (long)c.Factura)).ToList();
        if (cuentas.Count != numeros.Length)
            throw new ErrorCatalogo("El check-in no abrio todas las cuentas: no se hizo nada (revise la reserva).");
        tx.Commit();
        return new ResultadoCheckIn(reserva, cuentas, avisos);
    }
}
