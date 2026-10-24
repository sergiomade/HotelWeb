using Dapper;
using HotelWeb.Api.Datos;
using HotelWeb.Core.Catalogos;

namespace HotelWeb.Api.Catalogos;

/// <summary>Que tipos tienen foto; Version cambia al cambiar la foto (para que el navegador no muestre la vieja).</summary>
public sealed record FotoTipo(int Tipo, string Descripcion, long Version);

public sealed record ArchivoFoto(byte[] Bytes, string TipoContenido, DateTime Fecha);

/// <summary>Fotos de los tipos de habitacion (HotelTipoFoto, MIG_27): una por tipo. Delphi no usa la tabla.</summary>
public sealed class RepositorioFotosTipo
{
    private readonly Conexiones _conexiones;

    public RepositorioFotosTipo(Conexiones conexiones) => _conexiones = conexiones;

    private sealed class FilaLista
    {
        public int HabitacionTipo { get; set; }
        public string? Descripcion { get; set; }
        public DateTime Fecha { get; set; }
    }

    private sealed class FilaFoto
    {
        public byte[] Foto { get; set; } = [];
        public string TipoContenido { get; set; } = "";
        public DateTime Fecha { get; set; }
    }

    public IReadOnlyList<FotoTipo> Lista()
    {
        using var cn = _conexiones.Hotel();
        return cn.Query<FilaLista>("""
            SELECT f.HabitacionTipo, RTRIM(t.Descripcion) AS Descripcion, f.Fecha
            FROM HotelTipoFoto f JOIN HotelHabitacionTipo t ON t.HabitacionTipo = f.HabitacionTipo
            ORDER BY f.HabitacionTipo
            """).Select(f => new FotoTipo(f.HabitacionTipo, f.Descripcion ?? "", f.Fecha.Ticks)).ToList();
    }

    public ArchivoFoto? Leer(int tipo)
    {
        using var cn = _conexiones.Hotel();
        var f = cn.QueryFirstOrDefault<FilaFoto>(
            "SELECT Foto, RTRIM(TipoContenido) AS TipoContenido, Fecha FROM HotelTipoFoto WHERE HabitacionTipo = @tipo", new { tipo });
        return f is null ? null : new ArchivoFoto(f.Foto, f.TipoContenido, f.Fecha);
    }

    public void Guardar(int tipo, byte[] foto, string usuario)
    {
        if (ReglasFoto.Validar(foto) is { } motivo) throw new ErrorCatalogo(motivo);
        var contenido = ReglasFoto.TipoDe(foto)!;
        using var cn = _conexiones.Hotel();
        cn.Open();
        using var tx = cn.BeginTransaction();
        if (cn.ExecuteScalar<int>("SELECT COUNT(*) FROM HotelHabitacionTipo WHERE HabitacionTipo = @tipo", new { tipo }, tx) == 0)
            throw new ErrorCatalogo($"No existe el tipo {tipo}.");
        var u = new DbString { Value = usuario.Length > 10 ? usuario[..10] : usuario, IsAnsi = true, Length = 10 };
        var c = new DbString { Value = contenido, IsAnsi = true, Length = 30 };
        var n = cn.Execute("""
            UPDATE HotelTipoFoto WITH (UPDLOCK, HOLDLOCK) SET Foto = @foto, TipoContenido = @c, Usuario = @u, Fecha = GETDATE()
            WHERE HabitacionTipo = @tipo
            """, new { tipo, foto, c, u }, tx);
        if (n == 0)
            cn.Execute("INSERT INTO HotelTipoFoto (HabitacionTipo, Foto, TipoContenido, Usuario) VALUES (@tipo, @foto, @c, @u)",
                new { tipo, foto, c, u }, tx);
        tx.Commit();
    }

    public bool Quitar(int tipo)
    {
        using var cn = _conexiones.Hotel();
        return cn.Execute("DELETE FROM HotelTipoFoto WHERE HabitacionTipo = @tipo", new { tipo }) > 0;
    }
}
