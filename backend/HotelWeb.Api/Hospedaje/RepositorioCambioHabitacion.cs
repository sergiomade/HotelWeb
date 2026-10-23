using System.Data;
using Dapper;
using HotelWeb.Api.Catalogos;
using HotelWeb.Api.Datos;
using HotelWeb.Core.Hospedaje;
using HotelWeb.Core.Reservas;

namespace HotelWeb.Api.Hospedaje;

/// <summary>Lo que necesita la pantalla: lo que se cobra hoy y las habitaciones libres.</summary>
public sealed record PreparacionCambio(int Habitacion, long Factura, long? Reserva, int Tipo, string TipoNombre, string Categoria,
    decimal Tarifa, bool TieneLinea, IReadOnlyList<LibreVista> Libres);

public sealed record ResultadoCambio(int Desde, int Hasta, long Factura, string Accion, IReadOnlyList<string> Avisos);

/// <summary>
/// Cambio de la cuenta a una habitacion LIBRE (Delphi: UFrmMntTransferirHuesped, "no ocupada", que solo hace
/// UPDATE HotelFactura SET Habitacion_id). La web, ademas, cobra el precio del tipo nuevo desde la proxima noche
/// (decision del usuario): ajusta la linea de la reserva como el check-in, y pasa a la habitacion nueva los recibos
/// de la estadia (HotelReciboIngreso.Habitacion), que si no se quedarian en la vieja.
/// </summary>
public sealed class RepositorioCambioHabitacion
{
    private readonly Conexiones _conexiones;

    public RepositorioCambioHabitacion(Conexiones conexiones) => _conexiones = conexiones;

    private static DbString A(string? v, int largo) => new() { Value = v, IsAnsi = true, Length = largo };

    private sealed class FilaCuenta
    {
        public decimal Factura_id { get; set; }
        public decimal ClaveUnica { get; set; }
        public decimal? HabitacionReserva_id { get; set; }
        public int TipoHabitacion { get; set; }
        public string? CategoriaPrecio_id { get; set; }
        public bool? Transferida { get; set; }
        public DateTime? FechaEntrada { get; set; }
        public DateTime? FechaSalida { get; set; }
    }

    private sealed class FilaLinea
    {
        public int Secuencia { get; set; }
        public int Habitacion_id { get; set; }
        public string? CategoriaPrecio_id { get; set; }
        public decimal Tarifa { get; set; }
        public int Cantidad { get; set; }
        public decimal? NoHabitacion { get; set; }
    }

    private static FilaCuenta Cuenta(IDbConnection cn, IDbTransaction? tx, int habitacion) =>
        cn.QueryFirstOrDefault<FilaCuenta>($"""
            SELECT TOP 1 f.Factura_id, ISNULL(NULLIF(f.ClaveUnica, 0), f.Factura_id) AS ClaveUnica, f.HabitacionReserva_id,
                   ISNULL(f.TipoHabitacion, h.HabitacionTipo) AS TipoHabitacion, RTRIM(f.CategoriaPrecio_id) AS CategoriaPrecio_id,
                   f.Transferida, r.FechaEntrada, r.FechaSalida
            FROM HotelFactura f {(tx is null ? "WITH (NOLOCK)" : "WITH (UPDLOCK)")}
            JOIN hotelHabitacion h ON h.Habitacion_id = f.Habitacion_id
            LEFT JOIN HotelHabitacionReserva r ON r.HabitacionReserva_id = f.HabitacionReserva_id
            WHERE f.Habitacion_id = @habitacion ORDER BY f.Factura_id DESC
            """, new { habitacion }, tx) ?? throw new ErrorCatalogo($"La habitacion {habitacion} no tiene cuenta abierta.");

    private static List<FilaLinea> Lineas(IDbConnection cn, IDbTransaction? tx, decimal? reserva) => reserva is null
        ? new List<FilaLinea>()
        : cn.Query<FilaLinea>("""
            SELECT Secuencia, Habitacion_id, RTRIM(CategoriaPrecio_id) AS CategoriaPrecio_id, Tarifa, Cantidad, NoHabitacion
            FROM HotelHabitacionReservada WHERE HabitacionReserva_id = @reserva ORDER BY Secuencia
            """, new { reserva }, tx).ToList();

    /// <summary>La linea de la que la carga de la noche toma la tarifa de esta cuenta (reserva + tipo + categoria).</summary>
    private static FilaLinea? SuLinea(List<FilaLinea> lineas, FilaCuenta c) =>
        lineas.FirstOrDefault(l => l.Habitacion_id == c.TipoHabitacion
                                   && string.Equals(l.CategoriaPrecio_id?.Trim(), c.CategoriaPrecio_id?.Trim(), StringComparison.OrdinalIgnoreCase));

    private static Dictionary<int, string> Tipos(IDbConnection cn, IDbTransaction? tx) =>
        cn.Query<(int, string)>("SELECT HabitacionTipo, RTRIM(Descripcion) FROM HotelHabitacionTipo", transaction: tx)
            .ToDictionary(t => t.Item1, t => t.Item2);

    /// <summary>Habitaciones sin cuenta abierta. Dentro de una transaccion, bloquea para que nadie las ocupe mientras tanto.</summary>
    private static List<LibreVista> Libres(IDbConnection cn, IDbTransaction? tx) =>
        cn.Query<(decimal Numero, int Tipo, string TipoNombre, bool? Disponible)>($"""
            SELECT h.Habitacion_id, h.HabitacionTipo, RTRIM(t.Descripcion), e.Disponible
            FROM hotelHabitacion h
            JOIN HotelHabitacionTipo t ON t.HabitacionTipo = h.HabitacionTipo
            JOIN HotelHabitacionEstado e ON e.HabitacionEstado_id = h.HabitacionEstado_id
            WHERE NOT EXISTS (SELECT 1 FROM HotelFactura f {(tx is null ? "WITH (NOLOCK)" : "WITH (UPDLOCK, HOLDLOCK)")}
                              WHERE f.Habitacion_id = h.Habitacion_id)
            ORDER BY h.Habitacion_id
            """, transaction: tx).Select(x => new LibreVista((int)x.Numero, x.Tipo, x.TipoNombre, x.Disponible == true)).ToList();

    public PreparacionCambio Preparar(int habitacion)
    {
        using var cn = _conexiones.Hotel();
        var c = Cuenta(cn, null, habitacion);
        if (c.Transferida == true) throw new ErrorCatalogo("La cuenta esta transferida a otra habitacion: no se mueve.");
        var linea = SuLinea(Lineas(cn, null, c.HabitacionReserva_id), c);
        var tipos = Tipos(cn, null);
        return new PreparacionCambio(habitacion, (long)c.Factura_id, (long?)c.HabitacionReserva_id, c.TipoHabitacion,
            tipos.TryGetValue(c.TipoHabitacion, out var n) ? n : $"tipo {c.TipoHabitacion}", c.CategoriaPrecio_id ?? "A",
            linea?.Tarifa ?? 0, linea is not null, Libres(cn, null));
    }

    /// <param name="categoria">Null = la de ahora.</param>
    /// <param name="tarifa">Sin impuestos. Null = la de ahora si no cambia el tipo ni la categoria; si cambia, la de la tabla.</param>
    public ResultadoCambio Cambiar(int desde, int hasta, string? categoria, decimal? tarifa, string usuario)
    {
        if (desde == hasta) throw new ErrorCatalogo("No puede cambiar a la misma habitacion.");
        using var cn = _conexiones.Hotel();
        cn.Open();
        using var tx = cn.BeginTransaction(IsolationLevel.Serializable);
        var c = Cuenta(cn, tx, desde);
        if (c.Transferida == true) throw new ErrorCatalogo("La cuenta esta transferida a otra habitacion: no se mueve.");
        var destino = Libres(cn, tx).FirstOrDefault(x => x.Numero == hasta)
                      ?? throw new ErrorCatalogo($"La habitacion {hasta} no esta libre (ocupada o no existe).");
        if (!destino.Disponible) throw new ErrorCatalogo($"La habitacion {hasta} esta bloqueada o no se usa.");

        var tipos = Tipos(cn, tx);
        string Nombre(int t) => tipos.TryGetValue(t, out var n) ? n : $"tipo {t}";
        var lineas = Lineas(cn, tx, c.HabitacionReserva_id);
        var linea = SuLinea(lineas, c);
        var avisos = new List<string>();
        var accion = AccionLinea.SoloNumero;
        var catActual = (c.CategoriaPrecio_id ?? "A").Trim().ToUpperInvariant();
        var cat = string.IsNullOrWhiteSpace(categoria) ? catActual : categoria.Trim().ToUpperInvariant();
        var tipo = destino.Tipo;

        if (linea is null)
        {
            // sin linea de reserva (walk-in o reserva rara): como Delphi, solo el numero; el tipo de la cuenta sigue igual
            tipo = c.TipoHabitacion;
            cat = catActual;
            if (destino.Tipo != c.TipoHabitacion)
                avisos.Add("La cuenta no tiene linea de reserva: solo cambia el numero, el precio sigue igual.");
        }
        else
        {
            if (cat != catActual && cn.ExecuteScalar<int>("SELECT COUNT(*) FROM HotelCategoriaPrecio WHERE UPPER(RTRIM(CategoriaPrecio_id)) = @cat",
                    new { cat = A(cat, 1) }, tx) == 0)
                throw new ErrorCatalogo($"No existe la categoria de precio {cat}.");
            var nueva = tarifa;
            if (nueva is null)
            {
                if (tipo == c.TipoHabitacion && cat == catActual) nueva = linea.Tarifa;
                else nueva = cn.ExecuteScalar<decimal?>("""
                        SELECT PrecioHabitacion FROM HotelHabitacionTipoPrecio
                        WHERE HabitacionTipo_id = @tipo AND UPPER(RTRIM(CategoriaPrecio_id)) = @cat
                        """, new { tipo, cat = A(cat, 1) }, tx)
                    ?? throw new ErrorCatalogo($"{Nombre(tipo)} no tiene precio en la categoria {cat}: pongalo en Precios o escriba la tarifa.");
            }
            if (nueva < 0 || nueva > 999_999_999m) throw new ErrorCatalogo("La tarifa no es valida.");
            var original = new LineaReservada(linea.Secuencia, linea.Habitacion_id, linea.CategoriaPrecio_id ?? "A", linea.Tarifa, linea.Cantidad);
            accion = ReglasCambioHabitacion.Accion(original, tipo, cat, nueva.Value);

            // como quedan las lineas de la reserva: sin dos tarifas para el mismo tipo y categoria
            var quedan = lineas.Where(l => l.Secuencia != linea.Secuencia || accion == AccionLinea.Separar)
                .Select(l => (l.Habitacion_id, l.CategoriaPrecio_id ?? "A", l.Tarifa)).ToList();
            if (accion != AccionLinea.SoloNumero) quedan.Add((tipo, cat, nueva.Value));
            if (ReglasCambioHabitacion.Conflicto(quedan, Nombre) is { } conflicto) throw new ErrorCatalogo(conflicto);

            var noches = c.FechaEntrada is { } e && c.FechaSalida is { } s ? ReglasReserva.Noches(e, s) : 0;
            var nombre = Nombre(tipo);
            var datos = new
            {
                reserva = c.HabitacionReserva_id, sec = linea.Secuencia, tipoViejo = linea.Habitacion_id, tipo,
                nombre = A(nombre.Length > 30 ? nombre[..30] : nombre, 30), cat = A(cat, 1), tarifa = nueva.Value, noches, desde, hasta,
            };
            switch (accion)
            {
                case AccionLinea.SoloNumero:
                    cn.Execute("""
                        UPDATE HotelHabitacionReservada SET NoHabitacion = @hasta
                        WHERE HabitacionReserva_id = @reserva AND Secuencia = @sec AND NoHabitacion = @desde
                        """, datos, tx);
                    break;
                case AccionLinea.Actualizar:
                    cn.Execute("""
                        UPDATE HotelHabitacionReservada
                        SET Habitacion_id = @tipo, Habitacion = @nombre, CategoriaPrecio_id = @cat, Tarifa = @tarifa,
                            Importe = ROUND(Cantidad * @tarifa * @noches, 2), NoHabitacion = @hasta
                        WHERE HabitacionReserva_id = @reserva AND Secuencia = @sec AND Habitacion_id = @tipoViejo
                        """, datos, tx);
                    break;
                case AccionLinea.Separar:
                    // la linea nueva copia personas y credito; la vieja queda con una habitacion menos
                    cn.Execute("""
                        INSERT INTO HotelHabitacionReservada (HabitacionReserva_id, Habitacion_id, Habitacion, Cantidad, Adultos, Ninos, Bebes,
                                                              Credito, Tarifa, Importe, CategoriaPrecio_id, NoHabitacion)
                        SELECT HabitacionReserva_id, @tipo, @nombre, 1, Adultos, Ninos, Bebes, Credito, @tarifa, ROUND(@tarifa * @noches, 2), @cat, @hasta
                        FROM HotelHabitacionReservada WHERE HabitacionReserva_id = @reserva AND Secuencia = @sec;
                        UPDATE HotelHabitacionReservada SET Cantidad = Cantidad - 1, Importe = ROUND((Cantidad - 1) * Tarifa * @noches, 2)
                        WHERE HabitacionReserva_id = @reserva AND Secuencia = @sec;
                        """, datos, tx);
                    // si la linea vieja apuntaba a esta habitacion, que apunte a otra de las que siguen en ella
                    cn.Execute("""
                        UPDATE d SET NoHabitacion = (SELECT TOP 1 f.Habitacion_id FROM HotelFactura f
                                                     WHERE f.HabitacionReserva_id = d.HabitacionReserva_id AND f.TipoHabitacion = d.Habitacion_id
                                                       AND RTRIM(f.CategoriaPrecio_id) = RTRIM(d.CategoriaPrecio_id) AND f.Habitacion_id <> @desde
                                                     ORDER BY f.Habitacion_id)
                        FROM HotelHabitacionReservada d
                        WHERE d.HabitacionReserva_id = @reserva AND d.Secuencia = @sec AND d.NoHabitacion = @desde
                        """, datos, tx);
                    break;
            }
            if (destino.Tipo != c.TipoHabitacion)
                avisos.Add(nueva.Value == linea.Tarifa && cat == catActual
                    ? $"La {hasta} es {Nombre(destino.Tipo)}: se le respeta el precio de antes ({nueva.Value:N2})."
                    : $"La {hasta} es {Nombre(destino.Tipo)}: desde la proxima noche se cobra {nueva.Value:N2} (antes {linea.Tarifa:N2}).");
            else if (nueva.Value != linea.Tarifa || cat != catActual)
                avisos.Add($"Desde la proxima noche se cobra {nueva.Value:N2} (antes {linea.Tarifa:N2}).");
        }

        // la cuenta, sus recibos y sus cambios de salida van a la habitacion nueva
        var p = new { factura = c.Factura_id, clave = c.ClaveUnica, desde, hasta, tipo, cat = A(cat, 1) };
        cn.Execute("""
            UPDATE HotelFactura SET Habitacion_id = @hasta, TipoHabitacion = @tipo, CategoriaPrecio_id = @cat WHERE Factura_id = @factura;
            UPDATE HotelReciboIngreso SET Habitacion = @hasta WHERE Factura_id = @clave AND Habitacion = @desde;
            UPDATE HotelCambioSalida SET Habitacion_id = @hasta WHERE Factura_id = @factura;
            """, p, tx);
        tx.Commit();
        return new ResultadoCambio(desde, hasta, (long)c.Factura_id, accion.ToString(), avisos);
    }
}
