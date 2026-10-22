using Dapper;
using HotelWeb.Api.Datos;
using HotelWeb.Core.Catalogos;

namespace HotelWeb.Api.Catalogos;

/// <summary>
/// Tipos de habitacion (HotelHabitacionTipo). Se crean (numero = el mayor + 1, como Delphi) y se modifican; no se
/// borran: las habitaciones y los precios apuntan al tipo.
/// </summary>
public sealed class RepositorioTipos
{
    private readonly Conexiones _conexiones;
    private readonly RepositorioPrecios _precios;

    public RepositorioTipos(Conexiones conexiones, RepositorioPrecios precios)
    {
        _conexiones = conexiones;
        _precios = precios;
    }

    public IReadOnlyList<TipoHabitacion> Obtener() => _precios.Obtener().Tipos;

    public TipoHabitacion Modificar(int id, DatosTipo datos, string usuario)
    {
        var d = ReglasTipo.Normalizar(datos);
        using var cn = _conexiones.Hotel();
        cn.Open();
        using var tx = cn.BeginTransaction();
        if (ReglasTipo.Validar(d, Nombres(cn, tx), id) is { } motivo) throw new ErrorCatalogo(motivo);
        var n = cn.Execute("""
            UPDATE HotelHabitacionTipo
               SET Descripcion = @Descripcion, Siglas = @Siglas, Minimo = @Minimo, Maximo = @Maximo,
                   Usuario = @usuario, fechaadicion = GETDATE()
             WHERE HabitacionTipo = @id
            """, Parametros(id, d, usuario), tx);
        if (n == 0) throw new ErrorCatalogo($"No existe el tipo {id}.");
        tx.Commit();
        return Leer(id);
    }

    public TipoHabitacion Crear(DatosTipo datos, string usuario)
    {
        var d = ReglasTipo.Normalizar(datos);
        using var cn = _conexiones.Hotel();
        cn.Open();
        using var tx = cn.BeginTransaction();
        if (ReglasTipo.Validar(d, Nombres(cn, tx), null) is { } motivo) throw new ErrorCatalogo(motivo);
        // el mayor + 1, como Delphi; UPDLOCK para que dos a la vez no tomen el mismo numero
        var id = cn.ExecuteScalar<int>(
            "SELECT ISNULL(MAX(HabitacionTipo), 0) + 1 FROM HotelHabitacionTipo WITH (UPDLOCK, HOLDLOCK)", transaction: tx);
        cn.Execute("""
            INSERT INTO HotelHabitacionTipo (HabitacionTipo, Descripcion, Minimo, Maximo, Siglas, Usuario, fechaadicion)
            VALUES (@id, @Descripcion, @Minimo, @Maximo, @Siglas, @usuario, GETDATE())
            """, Parametros(id, d, usuario), tx);
        tx.Commit();
        return Leer(id);
    }

    private static IEnumerable<(int, string)> Nombres(System.Data.IDbConnection cn, System.Data.IDbTransaction tx) =>
        cn.Query<(int, string)>("SELECT HabitacionTipo, RTRIM(Descripcion) FROM HotelHabitacionTipo", transaction: tx).ToList();

    private static object Parametros(int id, DatosTipo d, string usuario) => new
    {
        id,
        Descripcion = new DbString { Value = d.Descripcion, IsAnsi = true, Length = 50 },
        Siglas = new DbString { Value = d.Siglas ?? "", IsAnsi = true, IsFixedLength = true, Length = 3 },
        d.Minimo,
        d.Maximo,
        usuario = new DbString { Value = usuario.Length > 10 ? usuario[..10] : usuario, IsAnsi = true, Length = 10 },
    };

    private TipoHabitacion Leer(int id) =>
        Obtener().FirstOrDefault(t => t.Id == id) ?? throw new ErrorCatalogo($"No existe el tipo {id}.");
}
