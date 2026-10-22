using Dapper;
using HotelWeb.Api.Datos;
using HotelWeb.Core.Catalogos;

namespace HotelWeb.Api.Catalogos;

public sealed record Habitacion(int Numero, int Tipo, int Estado, string? Comentario, string? Extension,
    bool Television, bool AireAcondicionado, bool Cabana, int X, int Y, bool Ocupada, string? Usuario, DateTime? Fecha);
public sealed record EstadoHabitacion(int Id, string Descripcion, bool Disponible);
public sealed record CatalogoHabitaciones(IReadOnlyList<Habitacion> Habitaciones, IReadOnlyList<TipoHabitacion> Tipos,
    IReadOnlyList<EstadoHabitacion> Estados);

/// <summary>
/// Catalogo de habitaciones (hotelHabitacion). Se crean y se modifican; no se borran (HotelFactura y el historico
/// apuntan al numero): para sacar una de uso esta el estado "No usar".
/// </summary>
public sealed class RepositorioHabitaciones
{
    private readonly Conexiones _conexiones;
    private readonly RepositorioPrecios _precios;

    public RepositorioHabitaciones(Conexiones conexiones, RepositorioPrecios precios)
    {
        _conexiones = conexiones;
        _precios = precios;
    }

    private sealed class Fila
    {
        public decimal Habitacion_id { get; set; }
        public int HabitacionTipo { get; set; }
        public int HabitacionEstado_id { get; set; }
        public string? Comentario { get; set; }
        public string? Extension { get; set; }
        public bool Television { get; set; }
        public bool AireAcondicionado { get; set; }
        public bool? Cabana { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public bool Ocupada { get; set; }
        public string? Usuario { get; set; }
        public DateTime? Fecha { get; set; }
    }

    private const string Seleccion = """
        SELECT h.Habitacion_id, h.HabitacionTipo, h.HabitacionEstado_id,
               CAST(h.Comentario AS varchar(500)) AS Comentario, RTRIM(h.Extension) AS Extension,
               h.Television, h.AireAcondicionado, h.cabana AS Cabana, h.x AS X, h.y AS Y,
               CAST(CASE WHEN EXISTS (SELECT 1 FROM HotelFactura f WITH (NOLOCK) WHERE f.Habitacion_id = h.Habitacion_id)
                         THEN 1 ELSE 0 END AS bit) AS Ocupada,
               RTRIM(h.usuario) AS Usuario, h.fechaadicion AS Fecha
        FROM hotelHabitacion h
        """;

    public CatalogoHabitaciones Obtener()
    {
        using var cn = _conexiones.Hotel();
        var habitaciones = cn.Query<Fila>(Seleccion + " ORDER BY h.Habitacion_id").Select(De).ToList();
        var estados = cn.Query<(int Id, string? Descripcion, bool? Disponible)>(
                "SELECT HabitacionEstado_id, RTRIM(Descripcion), Disponible FROM HotelHabitacionEstado ORDER BY HabitacionEstado_id")
            .Select(e => new EstadoHabitacion(e.Id, e.Descripcion ?? e.Id.ToString(), e.Disponible == true)).ToList();
        return new CatalogoHabitaciones(habitaciones, _precios.Obtener().Tipos, estados);
    }

    public Habitacion Modificar(int numero, CambiosHabitacion c, string usuario)
    {
        if (ReglasHabitacion.Validar(c) is { } motivo) throw new ErrorCatalogo(motivo);
        using var cn = _conexiones.Hotel();
        ValidarTipoYEstado(cn, c);
        var n = cn.Execute("""
            UPDATE hotelHabitacion
               SET HabitacionTipo = @Tipo, HabitacionEstado_id = @Estado, Comentario = @Comentario, Extension = @Extension,
                   Television = @Television, AireAcondicionado = @AireAcondicionado, cabana = @Cabana,
                   usuario = @usuario, fechaadicion = GETDATE()
             WHERE Habitacion_id = @numero
            """, Parametros(numero, c, usuario));
        if (n == 0) throw new ErrorCatalogo($"No existe la habitacion {numero}.");
        return Leer(cn, numero);
    }

    public Habitacion Crear(int numero, int? x, int? y, CambiosHabitacion c, string usuario)
    {
        using var cn = _conexiones.Hotel();
        if (cn.ExecuteScalar<int>("SELECT COUNT(*) FROM hotelHabitacion WHERE Habitacion_id = @numero", new { numero }) > 0)
            throw new ErrorCatalogo($"Ya existe la habitacion {numero}.");
        var (sx, sy) = Sugerir(cn, numero);
        var px = x ?? sx;
        var py = y ?? sy;
        if (ReglasHabitacion.ValidarNueva(numero, px, py, c) is { } motivo) throw new ErrorCatalogo(motivo);
        ValidarTipoYEstado(cn, c);
        var p = new DynamicParameters(Parametros(numero, c, usuario));
        p.Add("x", px);
        p.Add("y", py);
        cn.Execute("""
            INSERT INTO hotelHabitacion (Habitacion_id, HabitacionTipo, HabitacionEstado_id, Comentario, Extension,
                                         Television, cabana, AireAcondicionado, x, y, usuario, fechaadicion)
            VALUES (@numero, @Tipo, @Estado, @Comentario, @Extension, @Television, @Cabana, @AireAcondicionado,
                    @x, @y, @usuario, GETDATE())
            """, p);
        return Leer(cn, numero);
    }

    /// <summary>Posicion que se le daria a una habitacion nueva en el plano.</summary>
    public (int X, int Y) PosicionSugerida(int numero)
    {
        using var cn = _conexiones.Hotel();
        return Sugerir(cn, numero);
    }

    private static (int X, int Y) Sugerir(System.Data.IDbConnection cn, int numero)
    {
        var hay = cn.Query<(decimal Numero, int X, int Y)>("SELECT Habitacion_id, x, y FROM hotelHabitacion")
            .Select(h => ((int)h.Numero, h.X, h.Y)).ToList();
        return ReglasHabitacion.PosicionSugerida(numero, hay);
    }

    private static void ValidarTipoYEstado(System.Data.IDbConnection cn, CambiosHabitacion c)
    {
        if (cn.ExecuteScalar<int>("SELECT COUNT(*) FROM HotelHabitacionTipo WHERE HabitacionTipo = @Tipo", c) == 0)
            throw new ErrorCatalogo($"No existe el tipo de habitacion {c.Tipo}.");
        if (cn.ExecuteScalar<int>("SELECT COUNT(*) FROM HotelHabitacionEstado WHERE HabitacionEstado_id = @Estado", c) == 0)
            throw new ErrorCatalogo($"No existe el estado {c.Estado}.");
    }

    private static object Parametros(int numero, CambiosHabitacion c, string usuario) => new
    {
        numero,
        c.Tipo,
        c.Estado,
        Comentario = new DbString { Value = ReglasHabitacion.Limpio(c.Comentario), IsAnsi = true, Length = -1 },
        Extension = new DbString { Value = ReglasHabitacion.Limpio(c.Extension), IsAnsi = true, Length = 4 },
        c.Television,
        c.AireAcondicionado,
        c.Cabana,
        // usuario char(10): el login cortado, como Delphi
        usuario = new DbString { Value = usuario.Length > 10 ? usuario[..10] : usuario, IsAnsi = true, Length = 10 },
    };

    private static Habitacion Leer(System.Data.IDbConnection cn, int numero) =>
        De(cn.QuerySingle<Fila>(Seleccion + " WHERE h.Habitacion_id = @numero", new { numero }));

    private static Habitacion De(Fila f) => new((int)f.Habitacion_id, f.HabitacionTipo, f.HabitacionEstado_id,
        string.IsNullOrWhiteSpace(f.Comentario) ? null : f.Comentario.Trim(), ReglasHabitacion.Limpio(f.Extension),
        f.Television, f.AireAcondicionado, f.Cabana == true, f.X, f.Y, f.Ocupada, f.Usuario, f.Fecha);
}
