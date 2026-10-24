using System.Collections.Concurrent;
using HotelWeb.Core.Seguridad;

namespace HotelWeb.Api.Seguridad;

/// <summary>
/// Que puede hacer cada usuario en la web.
/// - Con la seguridad ACTIVADA (existe el modulo 40 en la base Seguridad, etapa 10): lo dicen sus grupos del hotel
///   (Seg_Grupo_Usuario + Seg_Permiso_Grupo). Sin grupos del hotel no ve ninguna area (solo el tablero).
/// - Sin activar: como antes, todas las areas para todos y las acciones delicadas por las listas de appsettings
///   (Usuario_Id de Seg_Usuario):
///   - Permisos:EditanCatalogos: precios, habitaciones, tipos, servicios y clientes; tambien activan la seguridad.
///   - Permisos:AnulanAdelantos, AnulanCargos, AnulanRecibos, ComprobantesEspeciales (si no estan, la de catalogos).
/// Los permisos de la base se guardan 30 segundos; un cambio desde la web los olvida enseguida.
/// </summary>
public sealed class PermisosWeb
{
    private static readonly TimeSpan Vida = TimeSpan.FromSeconds(30);

    private readonly RepositorioSeguridad _repo;
    private readonly ILogger<PermisosWeb> _log;
    private readonly HashSet<int> _editanCatalogos;
    private readonly HashSet<int> _anulanAdelantos;
    private readonly HashSet<int> _anulanCargos;
    private readonly HashSet<int> _anulanRecibos;
    private readonly HashSet<int> _comprobantesEspeciales;
    private readonly ConcurrentDictionary<int, (HashSet<int> Permisos, DateTime Hora)> _porUsuario = new();
    private (bool Valor, DateTime Hora)? _configurada;
    private volatile bool _sincronizada;

    public PermisosWeb(IConfiguration config, RepositorioSeguridad repo, ILogger<PermisosWeb> log)
    {
        _repo = repo;
        _log = log;
        _editanCatalogos = Lista(config, "Permisos:EditanCatalogos");
        _anulanAdelantos = ListaO(config, "Permisos:AnulanAdelantos", _editanCatalogos);
        _anulanCargos = ListaO(config, "Permisos:AnulanCargos", _editanCatalogos);
        _anulanRecibos = ListaO(config, "Permisos:AnulanRecibos", _editanCatalogos);
        _comprobantesEspeciales = ListaO(config, "Permisos:ComprobantesEspeciales", _editanCatalogos);
    }

    /// <summary>True si la seguridad de la web ya esta activada en la base (modulo 40).</summary>
    public bool Activada()
    {
        var c = _configurada;
        if (c is { } v && DateTime.UtcNow - v.Hora < Vida) return v.Valor;
        try
        {
            var valor = _repo.Configurada();
            if (valor && !_sincronizada)
            {
                // permisos nuevos de esta version: se crean y heredan antes de leer los de nadie
                _repo.Sincronizar();
                _sincronizada = true;
                _porUsuario.Clear();
            }
            _configurada = (valor, DateTime.UtcNow);
            return valor;
        }
        catch (Exception e)
        {
            // sin base: lo ultimo que se supo; si nunca se supo, como antes de activar
            _log.LogWarning(e, "No se pudo saber si la seguridad de la web esta activada");
            return c?.Valor ?? false;
        }
    }

    /// <summary>Los codigos de permiso (PermisosHotel) del usuario.</summary>
    public IReadOnlySet<int> De(int usuarioId)
    {
        if (!Activada()) return SinActivar(usuarioId);
        if (_porUsuario.TryGetValue(usuarioId, out var c) && DateTime.UtcNow - c.Hora < Vida) return c.Permisos;
        try
        {
            var p = _repo.PermisosDe(usuarioId);
            _porUsuario[usuarioId] = (p, DateTime.UtcNow);
            return p;
        }
        catch (Exception e)
        {
            _log.LogWarning(e, "No se pudieron leer los permisos del usuario {Usuario}", usuarioId);
            return new HashSet<int>();
        }
    }

    public bool Tiene(int usuarioId, PermisoHotel p) => De(usuarioId).Contains(p.Codigo);

    /// <summary>Despues de cambiar usuarios, grupos o permisos.</summary>
    public void Olvidar()
    {
        _porUsuario.Clear();
        _configurada = null;
    }

    /// <summary>Antes de activar: todas las areas y pestanas; las acciones, por las listas de appsettings.</summary>
    private HashSet<int> SinActivar(int usuarioId)
    {
        var p = PermisosHotel.Todos.Where(x => x.Tipo != TipoPermiso.Accion).Select(x => x.Codigo).ToHashSet();
        p.UnionWith(PermisosHotel.DeTodosSinActivar.Select(x => x.Codigo));
        if (_editanCatalogos.Contains(usuarioId))
        {
            p.UnionWith(PermisosHotel.EdicionCatalogos.Select(x => x.Codigo));
            p.Add(PermisosHotel.AdministrarSeguridad.Codigo);
        }
        if (_anulanAdelantos.Contains(usuarioId)) p.Add(PermisosHotel.AnularAdelantos.Codigo);
        if (_anulanCargos.Contains(usuarioId)) p.Add(PermisosHotel.AnularCargos.Codigo);
        if (_anulanRecibos.Contains(usuarioId)) p.Add(PermisosHotel.AnularRecibos.Codigo);
        if (_comprobantesEspeciales.Contains(usuarioId)) p.Add(PermisosHotel.ComprobantesEspeciales.Codigo);
        return p;
    }

    /// <summary>Los que entran al grupo ADMIN HOTEL al activar: la lista de catalogos (hoy 1 y 4).</summary>
    public IReadOnlyCollection<int> AdministradoresIniciales => _editanCatalogos;

    /// <summary>
    /// Gastos menores (E43), compras a informales (E41) y las secuencias e-CF autorizadas. Delphi pedia usuario y clave
    /// otra vez; decision del usuario (2026-10-23): solo los que tienen el permiso.
    /// </summary>
    public bool ComprobantesEspeciales(int usuarioId) => Tiene(usuarioId, PermisosHotel.ComprobantesEspeciales);

    /// <summary>Anular un recibo de pago antes del cuadre.</summary>
    public bool AnulaRecibos(int usuarioId) => Tiene(usuarioId, PermisosHotel.AnularRecibos);

    /// <summary>Alguno de los cinco permisos de catalogos (habitaciones, tipos, precios, servicios, clientes).</summary>
    public bool EditaCatalogos(int usuarioId)
    {
        var p = De(usuarioId);
        return PermisosHotel.EdicionCatalogos.Any(x => p.Contains(x.Codigo));
    }

    public bool EditaHabitaciones(int usuarioId) => Tiene(usuarioId, PermisosHotel.EditarHabitaciones);
    public bool EditaTipos(int usuarioId) => Tiene(usuarioId, PermisosHotel.EditarTipos);
    public bool EditaPrecios(int usuarioId) => Tiene(usuarioId, PermisosHotel.EditarPrecios);
    public bool EditaServicios(int usuarioId) => Tiene(usuarioId, PermisosHotel.EditarServicios);
    public bool EditaClientes(int usuarioId) => Tiene(usuarioId, PermisosHotel.EditarClientes);

    public bool AnulaAdelantos(int usuarioId) => Tiene(usuarioId, PermisosHotel.AnularAdelantos);

    /// <summary>Anular un cargo de la cuenta de una habitacion.</summary>
    public bool AnulaCargos(int usuarioId) => Tiene(usuarioId, PermisosHotel.AnularCargos);

    /// <summary>Cerrar el turno de caja del hotel o de la cafeteria (Posteado = 1).</summary>
    public bool CierraCaja(int usuarioId, bool hotel) =>
        Tiene(usuarioId, hotel ? PermisosHotel.CerrarCajaHotel : PermisosHotel.CerrarCajaCafeteria);

    public bool AdministraSeguridad(int usuarioId) => Tiene(usuarioId, PermisosHotel.AdministrarSeguridad);

    /// <summary>La lista de esa clave, o la de catalogos si no esta en appsettings.</summary>
    private static HashSet<int> ListaO(IConfiguration config, string clave, HashSet<int> otra) =>
        config.GetSection(clave).Exists() ? Lista(config, clave) : otra;

    private static HashSet<int> Lista(IConfiguration config, string clave) =>
        (config.GetSection(clave).Get<int[]>() ?? Array.Empty<int>()).ToHashSet();
}
