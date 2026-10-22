namespace HotelWeb.Api.Seguridad;

/// <summary>
/// Quien puede modificar en la web (appsettings: Permisos:EditanCatalogos, lista de Usuario_Id de Seg_Usuario).
/// En el hotel los permisos de Delphi no estan configurados (las pantallas piden una opcion que no existe y todos
/// quedan como supervisor), asi que la web lleva su propia lista corta, como hace Delphi con Sucursal (usuarios 1 y 4).
/// No toca la base Seguridad, que comparten otros programas.
/// </summary>
public sealed class PermisosWeb
{
    private readonly HashSet<int> _editanCatalogos;

    public PermisosWeb(IConfiguration config) =>
        _editanCatalogos = (config.GetSection("Permisos:EditanCatalogos").Get<int[]>() ?? Array.Empty<int>()).ToHashSet();

    public bool EditaCatalogos(int usuarioId) => _editanCatalogos.Contains(usuarioId);
}
