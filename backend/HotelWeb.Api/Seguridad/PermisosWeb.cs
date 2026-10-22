namespace HotelWeb.Api.Seguridad;

/// <summary>
/// Quien puede hacer lo delicado en la web (appsettings, listas de Usuario_Id de Seg_Usuario).
/// En el hotel los permisos de Delphi no estan configurados (las pantallas piden una opcion que no existe y todos
/// quedan como supervisor), asi que la web lleva sus propias listas cortas, como hace Delphi con Sucursal (1 y 4).
/// No toca la base Seguridad, que comparten otros programas.
/// - Permisos:EditanCatalogos: precios, habitaciones, tipos, servicios y clientes.
/// - Permisos:AnulanAdelantos: anular un adelanto de reserva (si no esta, la misma de catalogos).
/// Reservas y adelantos los registra cualquier usuario, como en Delphi.
/// </summary>
public sealed class PermisosWeb
{
    private readonly HashSet<int> _editanCatalogos;
    private readonly HashSet<int> _anulanAdelantos;

    public PermisosWeb(IConfiguration config)
    {
        _editanCatalogos = Lista(config, "Permisos:EditanCatalogos");
        var anulan = config.GetSection("Permisos:AnulanAdelantos");
        _anulanAdelantos = anulan.Exists() ? Lista(config, "Permisos:AnulanAdelantos") : _editanCatalogos;
    }

    private static HashSet<int> Lista(IConfiguration config, string clave) =>
        (config.GetSection(clave).Get<int[]>() ?? Array.Empty<int>()).ToHashSet();

    public bool EditaCatalogos(int usuarioId) => _editanCatalogos.Contains(usuarioId);

    public bool AnulaAdelantos(int usuarioId) => _anulanAdelantos.Contains(usuarioId);
}
