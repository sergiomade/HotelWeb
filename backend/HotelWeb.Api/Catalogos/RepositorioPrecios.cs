using Dapper;
using HotelWeb.Api.Datos;
using HotelWeb.Core.Catalogos;

namespace HotelWeb.Api.Catalogos;

public sealed record TipoHabitacion(int Id, string Descripcion, string? Siglas, int Minimo, int Maximo, int Habitaciones);
public sealed record CategoriaPrecio(string Id, string Descripcion);
public sealed record Precio(int Tipo, string Categoria, decimal SinImpuestos, decimal ConImpuestos, decimal PersonaAdicional,
    string? Usuario, DateTime? Fecha);
public sealed record TablaPrecios(IReadOnlyList<TipoHabitacion> Tipos, IReadOnlyList<CategoriaPrecio> Categorias,
    IReadOnlyList<Precio> Precios);

public sealed class ErrorCatalogo(string mensaje) : Exception(mensaje);

/// <summary>
/// Precios por tipo de habitacion y categoria (HotelHabitacionTipoPrecio, clave tipo + categoria). Delphi los lee
/// al hacer el check-in (UFrmMntCheckIn): un cambio vale para las entradas nuevas, no para las estadias en curso.
/// </summary>
public sealed class RepositorioPrecios
{
    private readonly Conexiones _conexiones;

    public RepositorioPrecios(Conexiones conexiones) => _conexiones = conexiones;

    private sealed class FilaTipo
    {
        public int HabitacionTipo { get; set; }
        public string? Descripcion { get; set; }
        public string? Siglas { get; set; }
        public int? Minimo { get; set; }
        public int? Maximo { get; set; }
        public int Habitaciones { get; set; }
    }

    private sealed class FilaPrecio
    {
        public int HabitacionTipo_id { get; set; }
        public string CategoriaPrecio_id { get; set; } = "";
        public decimal PrecioHabitacion { get; set; }
        public decimal PrecioPersonaAdicional { get; set; }
        public string? Usuario { get; set; }
        public DateTime? FechaAdicion { get; set; }
    }

    public TablaPrecios Obtener()
    {
        using var cn = _conexiones.Hotel();
        var tipos = cn.Query<FilaTipo>("""
            SELECT t.HabitacionTipo, RTRIM(t.Descripcion) AS Descripcion, RTRIM(t.Siglas) AS Siglas, t.Minimo, t.Maximo,
                   (SELECT COUNT(*) FROM hotelHabitacion h WHERE h.HabitacionTipo = t.HabitacionTipo) AS Habitaciones
            FROM HotelHabitacionTipo t ORDER BY t.HabitacionTipo
            """).Select(t => new TipoHabitacion(t.HabitacionTipo, t.Descripcion ?? "", t.Siglas, t.Minimo ?? 0,
                t.Maximo ?? 0, t.Habitaciones)).ToList();

        var categorias = cn.Query<(string Id, string? Descripcion)>(
                "SELECT RTRIM(CategoriaPrecio_id), RTRIM(Descripcion) FROM HotelCategoriaPrecio ORDER BY CategoriaPrecio_id")
            .Select(c => new CategoriaPrecio(c.Id, c.Descripcion ?? c.Id)).ToList();

        var precios = cn.Query<FilaPrecio>("""
            SELECT HabitacionTipo_id, RTRIM(CategoriaPrecio_id) AS CategoriaPrecio_id, PrecioHabitacion,
                   PrecioPersonaAdicional, RTRIM(usuario) AS Usuario, fechaadicion AS FechaAdicion
            FROM HotelHabitacionTipoPrecio
            """).Select(De).ToList();

        return new TablaPrecios(tipos, categorias, precios);
    }

    /// <summary>Guarda el precio de un tipo y categoria (lo crea si no existia). Devuelve como quedo.</summary>
    public Precio Guardar(int tipo, string categoria, decimal sinImpuestos, decimal personaAdicional, string usuario)
    {
        categoria = (categoria ?? "").Trim().ToUpperInvariant();
        if (PrecioHabitacion.Validar(sinImpuestos, personaAdicional) is { } motivo) throw new ErrorCatalogo(motivo);

        using var cn = _conexiones.Hotel();
        if (cn.ExecuteScalar<int>("SELECT COUNT(*) FROM HotelHabitacionTipo WHERE HabitacionTipo = @tipo", new { tipo }) == 0)
            throw new ErrorCatalogo($"No existe el tipo de habitacion {tipo}.");
        if (cn.ExecuteScalar<int>("SELECT COUNT(*) FROM HotelCategoriaPrecio WHERE CategoriaPrecio_id = @categoria",
                new { categoria }) == 0)
            throw new ErrorCatalogo($"No existe la categoria de precio {categoria}.");

        // usuario char(10): Delphi guarda el login cortado (sergio made -> "sergio mad")
        var p = new
        {
            tipo, categoria, sinImpuestos, personaAdicional,
            usuario = new DbString { Value = usuario.Length > 10 ? usuario[..10] : usuario, IsAnsi = true, Length = 10 },
        };
        cn.Open();
        using var tx = cn.BeginTransaction();
        var n = cn.Execute("""
            UPDATE HotelHabitacionTipoPrecio
               SET PrecioHabitacion = @sinImpuestos, PrecioPersonaAdicional = @personaAdicional,
                   usuario = @usuario, fechaadicion = GETDATE()
             WHERE HabitacionTipo_id = @tipo AND CategoriaPrecio_id = @categoria
            """, p, tx);
        if (n == 0)
            cn.Execute("""
                INSERT INTO HotelHabitacionTipoPrecio
                       (HabitacionTipo_id, CategoriaPrecio_id, PrecioHabitacion, PrecioPersonaAdicional, Impuestos, usuario, fechaadicion)
                VALUES (@tipo, @categoria, @sinImpuestos, @personaAdicional, 0, @usuario, GETDATE())
                """, p, tx);
        var fila = cn.QuerySingle<FilaPrecio>("""
            SELECT HabitacionTipo_id, RTRIM(CategoriaPrecio_id) AS CategoriaPrecio_id, PrecioHabitacion,
                   PrecioPersonaAdicional, RTRIM(usuario) AS Usuario, fechaadicion AS FechaAdicion
            FROM HotelHabitacionTipoPrecio WHERE HabitacionTipo_id = @tipo AND CategoriaPrecio_id = @categoria
            """, p, tx);
        tx.Commit();
        return De(fila);
    }

    private static Precio De(FilaPrecio f) => new(f.HabitacionTipo_id, f.CategoriaPrecio_id, f.PrecioHabitacion,
        PrecioHabitacion.ConImpuestos(f.PrecioHabitacion), f.PrecioPersonaAdicional, f.Usuario, f.FechaAdicion);
}
