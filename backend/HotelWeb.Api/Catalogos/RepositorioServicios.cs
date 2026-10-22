using Dapper;
using HotelWeb.Api.Datos;
using HotelWeb.Core.Catalogos;

namespace HotelWeb.Api.Catalogos;

public sealed record Servicio(int Id, string Descripcion, decimal Precio, decimal ConItbis, decimal ConItbisYPropina,
    bool Impuesto, long Cuenta, string? DelSistema, int Usos, string? Usuario, DateTime? Fecha);

/// <summary>
/// Servicios que se cargan a las cuentas (HotelServicioAdicional). Se crean (numero = mayor + 1) y se modifican; no se
/// borran: HotelFacturaDetalle apunta al servicio. Catalogo_id (cuenta contable) se respeta tal cual; uno nuevo lleva 0.
/// </summary>
public sealed class RepositorioServicios
{
    private readonly Conexiones _conexiones;

    public RepositorioServicios(Conexiones conexiones) => _conexiones = conexiones;

    private sealed class Fila
    {
        public string Servicio_id { get; set; } = "";
        public string? Descripcion { get; set; }
        public decimal Precio { get; set; }
        public bool Impuesto { get; set; }
        public decimal Catalogo_id { get; set; }
        public int Usos { get; set; }
        public string? Usuario { get; set; }
        public DateTime? FechaAdicion { get; set; }
    }

    private const string Seleccion = """
        SELECT s.Servicio_id, RTRIM(s.Descripcion) AS Descripcion, s.Precio, s.Impuesto, s.Catalogo_id,
               (SELECT COUNT(*) FROM HotelFacturaDetalle d WITH (NOLOCK) WHERE d.Servicio_id = s.Servicio_id) AS Usos,
               RTRIM(s.Usuario) AS Usuario, s.FechaAdicion
        FROM HotelServicioAdicional s
        """;

    public IReadOnlyList<Servicio> Obtener()
    {
        using var cn = _conexiones.Hotel();
        return cn.Query<Fila>(Seleccion).Select(De).OrderBy(s => s.Id).ToList();
    }

    public Servicio Modificar(int id, DatosServicio datos, string usuario)
    {
        var d = Validado(datos);
        using var cn = _conexiones.Hotel();
        var n = cn.Execute("""
            UPDATE HotelServicioAdicional
               SET Descripcion = @Descripcion, Precio = @Precio, Impuesto = @Impuesto, Usuario = @usuario, FechaAdicion = GETDATE()
             WHERE LTRIM(RTRIM(Servicio_id)) = @clave
            """, Parametros(id.ToString(), d, usuario));
        if (n == 0) throw new ErrorCatalogo($"No existe el servicio {id}.");
        return Leer(cn, id.ToString());
    }

    public Servicio Crear(DatosServicio datos, string usuario)
    {
        var d = Validado(datos);
        using var cn = _conexiones.Hotel();
        cn.Open();
        using var tx = cn.BeginTransaction();
        var existentes = cn.Query<string>("SELECT Servicio_id FROM HotelServicioAdicional WITH (UPDLOCK, HOLDLOCK)", transaction: tx);
        string clave;
        try { clave = ReglasServicio.Siguiente(existentes); }
        catch (InvalidOperationException e) { throw new ErrorCatalogo(e.Message); }
        cn.Execute("""
            INSERT INTO HotelServicioAdicional (Servicio_id, Descripcion, Precio, Impuesto, Catalogo_id, Usuario, FechaAdicion)
            VALUES (@clave, @Descripcion, @Precio, @Impuesto, 0, @usuario, GETDATE())
            """, Parametros(clave, d, usuario), tx);
        tx.Commit();
        return Leer(cn, clave);
    }

    private static DatosServicio Validado(DatosServicio datos)
    {
        var d = ReglasServicio.Normalizar(datos);
        if (ReglasServicio.Validar(d) is { } motivo) throw new ErrorCatalogo(motivo);
        return d;
    }

    private static object Parametros(string clave, DatosServicio d, string usuario) => new
    {
        // Servicio_id es char(4): se compara sin espacios y se guarda como lo deja SQL (relleno a la derecha)
        clave = new DbString { Value = clave, IsAnsi = true, Length = 4 },
        Descripcion = new DbString { Value = d.Descripcion, IsAnsi = true, Length = 50 },
        d.Precio,
        d.Impuesto,
        usuario = new DbString { Value = usuario.Length > 10 ? usuario[..10] : usuario, IsAnsi = true, Length = 10 },
    };

    private static Servicio Leer(System.Data.IDbConnection cn, string clave) =>
        De(cn.QuerySingle<Fila>(Seleccion + " WHERE LTRIM(RTRIM(s.Servicio_id)) = @clave", new { clave }));

    private static Servicio De(Fila f)
    {
        var id = int.TryParse(f.Servicio_id.Trim(), out var n) ? n : 0;
        return new Servicio(id, f.Descripcion ?? "", f.Precio, ReglasServicio.ConItbis(f.Precio),
            ReglasServicio.ConItbisYPropina(f.Precio), f.Impuesto, (long)f.Catalogo_id,
            ReglasServicio.DelSistema.TryGetValue(id, out var uso) ? uso : null, f.Usos, f.Usuario, f.FechaAdicion);
    }
}
