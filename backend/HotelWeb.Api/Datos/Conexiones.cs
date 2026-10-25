using Microsoft.Data.SqlClient;

namespace HotelWeb.Api.Datos;

/// <summary>
/// Las dos bases de Delphi: la del hotel (ConnectionStrings:Hotel, la misma que DBFinanzas de conexiones.ini) y la
/// de usuarios (ConnectionStrings:Seguridad, DBSeguridad). La web no crea tablas propias: trabaja sobre las mismas.
/// </summary>
public sealed class Conexiones
{
    private readonly string _hotel;
    private readonly string _seguridad;

    public Conexiones(IConfiguration config)
    {
        _hotel = config.GetConnectionString("Hotel")
                 ?? throw new InvalidOperationException("Falta ConnectionStrings:Hotel. En desarrollo: dotnet user-secrets set \"ConnectionStrings:Hotel\" \"...\" (ver README.md); en IIS: appsettings.json del sitio.");
        _seguridad = config.GetConnectionString("Seguridad")
                     ?? throw new InvalidOperationException("Falta ConnectionStrings:Seguridad en appsettings.json.");
    }

    public SqlConnection Hotel() => new(_hotel);

    public SqlConnection Seguridad() => new(_seguridad);

    /// <summary>Servidor y base de una cadena, sin la clave (para el diagnostico).</summary>
    public static (string Servidor, string Base) Describir(SqlConnection c)
    {
        var b = new SqlConnectionStringBuilder(c.ConnectionString);
        return (b.DataSource, b.InitialCatalog);
    }
}
