namespace HotelWeb.Core.Seguridad;

/// <summary>
/// Area = una entrada del menu. Opcion = una pestana de un area (se ve si tiene el area Y la opcion). Accion = algo
/// delicado que la API revisa (crear clientes, anular recibos...).
/// </summary>
public enum TipoPermiso { Area, Opcion, Accion }

/// <summary>
/// Permiso de la web del hotel. Cada uno es una fila de Seg_Opcion del modulo 40 (HOTEL WEB) de la base Seguridad
/// de Delphi; el codigo es el Opcion_Codigo. Un grupo lo tiene si hay fila en Seg_Permiso_Grupo (visible = 1).
/// Area: la clave del area a la que pertenece (null en las areas y en las acciones generales).
/// Hereda: al aparecer un permiso nuevo en una base ya activada, lo reciben los grupos que tenian este codigo (asi
/// nadie pierde lo que veia: las pestanas nuevas, a quien tenia el area; las acciones de catalogos, a quien tenia
/// la vieja 4010 "Editar catalogos").
/// </summary>
public sealed record PermisoHotel(int Codigo, string Clave, string Nombre, TipoPermiso Tipo, string Ayuda,
    string? Area = null, int? Hereda = null);

/// <summary>La lista fija de permisos (no hay pantalla de modulos ni de opciones: los define el programa).</summary>
public static class PermisosHotel
{
    /// <summary>Seg_Modulos.Modulo_Codigo de la web del hotel.</summary>
    public const int Modulo = 40;
    public const string NombreModulo = "HOTEL WEB";

    /// <summary>
    /// Opcion 4000 "MENU PRINCIPAL" (como la opcion 0 de los modulos de Delphi): la tiene todo grupo del hotel. Asi se
    /// sabe que grupos son del hotel aunque no tengan otro permiso.
    /// </summary>
    public const int MarcaGrupo = 4000;

    /// <summary>La primera version tenia un solo permiso para todos los catalogos; ahora son cinco (4151-4155).</summary>
    public const int EditarCatalogosViejo = 4010;

    // ---- areas (el menu) ----
    public static readonly PermisoHotel Hospedaje = new(4001, "hospedaje", "Hospedaje", TipoPermiso.Area, "El rack de habitaciones y las cuentas");
    public static readonly PermisoHotel Reservas = new(4002, "reservas", "Reservas", TipoPermiso.Area, "Reservaciones y adelantos");
    public static readonly PermisoHotel Recepcion = new(4003, "recepcion", "Recepción", TipoPermiso.Area, "Check-in, cargar noche, facturas y caja del hotel");
    public static readonly PermisoHotel WalkIn = new(4004, "walkin", "Walk-in", TipoPermiso.Area, "Ventas de pasadía");
    public static readonly PermisoHotel Cafeteria = new(4005, "cafeteria", "Cafetería", TipoPermiso.Area, "Mesas, facturas y caja de la cafetería");
    public static readonly PermisoHotel Compras = new(4006, "compras", "Compras", TipoPermiso.Area, "Gastos menores y compras a informales");
    public static readonly PermisoHotel Reportes = new(4007, "reportes", "Reportes", TipoPermiso.Area, "Reportes del hotel");
    public static readonly PermisoHotel Catalogos = new(4008, "catalogos", "Catálogos", TipoPermiso.Area, "Habitaciones, tipos, precios, servicios, clientes y secuencias");

    // ---- acciones ----
    public static readonly PermisoHotel AnularCargos = new(4012, "anularCargos", "Anular cargos", TipoPermiso.Accion, "Anular un cargo de la cuenta de una habitación", "hospedaje");
    public static readonly PermisoHotel AnularRecibos = new(4013, "anularRecibos", "Anular recibos", TipoPermiso.Accion, "Anular un recibo de pago antes del cuadre", "hospedaje");
    public static readonly PermisoHotel AnularAdelantos = new(4011, "anularAdelantos", "Anular adelantos", TipoPermiso.Accion, "Anular un adelanto de reserva", "reservas");
    public static readonly PermisoHotel EditarHabitaciones = new(4151, "editarHabitaciones", "Crear y modificar habitaciones", TipoPermiso.Accion, "", "catalogos", EditarCatalogosViejo);
    public static readonly PermisoHotel EditarTipos = new(4152, "editarTipos", "Crear y modificar tipos y sus fotos", TipoPermiso.Accion, "", "catalogos", EditarCatalogosViejo);
    public static readonly PermisoHotel EditarPrecios = new(4153, "editarPrecios", "Cambiar precios", TipoPermiso.Accion, "", "catalogos", EditarCatalogosViejo);
    public static readonly PermisoHotel EditarServicios = new(4154, "editarServicios", "Crear y modificar servicios", TipoPermiso.Accion, "", "catalogos", EditarCatalogosViejo);
    public static readonly PermisoHotel EditarClientes = new(4155, "editarClientes", "Crear y modificar clientes", TipoPermiso.Accion, "", "catalogos", EditarCatalogosViejo);
    // el cierre de caja: ver el cuadre es la pestana Caja; cerrar el turno (Posteado = 1) es esta accion. Al aparecer
    // en una base ya activada la reciben los grupos que veian la pestana Caja (antes, ver la caja era poder cerrarla).
    public static readonly PermisoHotel CerrarCajaHotel = new(4161, "cerrarCajaHotel", "Cerrar el turno de caja", TipoPermiso.Accion, "Cierre de caja del hotel (recibos y adelantos)", "recepcion", 4104);
    public static readonly PermisoHotel CerrarCajaCafeteria = new(4162, "cerrarCajaCafeteria", "Cerrar el turno de caja", TipoPermiso.Accion, "Cierre de caja de la cafetería", "cafeteria", 4113);
    public static readonly PermisoHotel ComprobantesEspeciales = new(4014, "comprobantesEspeciales", "Emitir E41 y E43", TipoPermiso.Accion, "Emitir gastos menores (E43) y compras a informales (E41)", "compras");
    // antes iba dentro de la 4014; al aparecer la reciben los grupos que ya la tenian (decision del usuario 2026-10-25:
    // el equipo de contabilidad emite E41/E43 sin tocar las secuencias)
    public static readonly PermisoHotel CambiarSecuencias = new(4015, "cambiarSecuencias", "Cambiar las secuencias e-CF", TipoPermiso.Accion, "Las secuencias e-NCF autorizadas por la DGII", "catalogos", 4014);
    public static readonly PermisoHotel AdministrarSeguridad = new(4020, "seguridad", "Administrar seguridad", TipoPermiso.Accion, "Usuarios, grupos y permisos de la web (el menú Seguridad)");

    /// <summary>Las pestanas de las areas: clave = "area.pestana", la misma que usa la pagina.</summary>
    private static readonly PermisoHotel[] Opciones =
    {
        Opcion(4101, Recepcion, "checkin", "Check-in"),
        Opcion(4102, Recepcion, "noche", "Cargar noche"),
        Opcion(4103, Recepcion, "facturas", "Facturas"),
        Opcion(4104, Recepcion, "caja", "Caja del hotel"),
        Opcion(4111, Cafeteria, "mesas", "Mesas"),
        Opcion(4112, Cafeteria, "facturas", "Facturas"),
        Opcion(4113, Cafeteria, "caja", "Caja de la cafetería"),
        Opcion(4121, Compras, "gastos", "Gastos menores (E43)"),
        Opcion(4122, Compras, "informales", "Compras a informales (E41)"),
        Opcion(4131, Reportes, "hotel", "Ventas del hotel"),
        Opcion(4132, Reportes, "cafeteria", "Ventas de la cafetería"),
        Opcion(4133, Reportes, "comprobantes", "Comprobantes (NCF)"),
        Opcion(4134, Reportes, "canceladas", "Canceladas sin cobro"),
        Opcion(4135, Reportes, "ocupadas", "Habitaciones ocupadas"),
        Opcion(4136, Reportes, "proyeccion", "Proyección de reservas"),
        Opcion(4137, Reportes, "clientes", "Clientes"),
        Opcion(4141, Catalogos, "habitaciones", "Habitaciones"),
        Opcion(4142, Catalogos, "tipos", "Tipos"),
        Opcion(4143, Catalogos, "precios", "Precios"),
        Opcion(4144, Catalogos, "servicios", "Servicios"),
        Opcion(4145, Catalogos, "clientes", "Clientes"),
        Opcion(4146, Catalogos, "secuencias", "Secuencias e-CF"),
    };

    private static PermisoHotel Opcion(int codigo, PermisoHotel area, string pestana, string nombre) =>
        new(codigo, $"{area.Clave}.{pestana}", nombre, TipoPermiso.Opcion, $"Pestaña {nombre} de {area.Nombre}", area.Clave, area.Codigo);

    public static readonly IReadOnlyList<PermisoHotel> Areas = new[] { Hospedaje, Reservas, Recepcion, WalkIn, Cafeteria, Compras, Reportes, Catalogos };

    public static readonly IReadOnlyList<PermisoHotel> Todos = Areas
        .Concat(Opciones)
        .Concat(new[] { AnularCargos, AnularRecibos, AnularAdelantos, CerrarCajaHotel, CerrarCajaCafeteria, EditarHabitaciones, EditarTipos, EditarPrecios, EditarServicios, EditarClientes, ComprobantesEspeciales, CambiarSecuencias, AdministrarSeguridad })
        .ToList();

    /// <summary>Lo que todos podian hacer antes de la seguridad por grupos (sin activar, siguen pudiendo).</summary>
    public static readonly IReadOnlyList<PermisoHotel> DeTodosSinActivar = new[] { CerrarCajaHotel, CerrarCajaCafeteria };

    /// <summary>Las de los catalogos (la vieja "Editar catalogos").</summary>
    public static readonly IReadOnlyList<PermisoHotel> EdicionCatalogos = new[] { EditarHabitaciones, EditarTipos, EditarPrecios, EditarServicios, EditarClientes };

    /// <summary>Las areas de trabajo del grupo RECEPCION que se crea al activar, con todas sus pestanas y el cierre de caja.</summary>
    public static readonly IReadOnlyList<PermisoHotel> DeRecepcion =
        ConSusOpciones(Hospedaje, Reservas, Recepcion, WalkIn, Cafeteria).Concat(new[] { CerrarCajaHotel, CerrarCajaCafeteria }).ToList();

    /// <summary>Las areas dadas y todas sus pestanas.</summary>
    public static IReadOnlyList<PermisoHotel> ConSusOpciones(params PermisoHotel[] areas) =>
        areas.Concat(Opciones.Where(o => areas.Any(a => a.Clave == o.Area))).ToList();

    public static PermisoHotel? PorCodigo(int codigo) => Todos.FirstOrDefault(p => p.Codigo == codigo);
}

public sealed class ErrorSeguridad(string mensaje) : Exception(mensaje);

/// <summary>Validaciones de usuarios y grupos, con los largos de las columnas de Seg_Usuario y Seg_Grupo.</summary>
public static class ReglasSeguridad
{
    public const int LargoLogin = 30;      // Seg_Usuario.Cuenta_Login varchar(30)
    public const int LargoNombre = 30;     // Seg_Usuario.Nombre varchar(30)
    public const int LargoClave = 10;      // Seg_Usuario.Clave_Acceso varchar(10)
    public const int LargoGrupo = 30;      // Seg_Grupo.Descripcion varchar(30)

    /// <summary>El login, sin espacios a los lados. Debe volver igual al desencriptarlo (lo lee Delphi).</summary>
    public static string Login(string? login)
    {
        var l = (login ?? "").Trim();
        if (l.Length == 0) throw new ErrorSeguridad("Escriba el usuario (login).");
        if (l.Length > LargoLogin) throw new ErrorSeguridad($"El usuario no puede pasar de {LargoLogin} caracteres.");
        if (l.Contains(' ')) throw new ErrorSeguridad("El usuario no puede llevar espacios.");
        if (!Reversible(l)) throw new ErrorSeguridad("El usuario solo puede llevar letras sin acento, números y símbolos del teclado.");
        return l;
    }

    public static string Nombre(string? nombre)
    {
        var n = (nombre ?? "").Trim();
        if (n.Length == 0) throw new ErrorSeguridad("Escriba el nombre.");
        if (n.Length > LargoNombre) throw new ErrorSeguridad($"El nombre no puede pasar de {LargoNombre} caracteres.");
        if (!Reversible(n)) throw new ErrorSeguridad("El nombre solo puede llevar letras sin acento ni ñ, números y espacios (Delphi lo guarda cifrado).");
        return n;
    }

    /// <summary>La clave tal cual (los espacios cuentan). De 4 a 10 caracteres, que Delphi pueda cifrar.</summary>
    public static string Clave(string? clave)
    {
        var c = clave ?? "";
        if (c.Trim().Length == 0) throw new ErrorSeguridad("Escriba la clave.");
        if (c.Length < 4) throw new ErrorSeguridad("La clave debe tener 4 caracteres o más.");
        if (c.Length > LargoClave) throw new ErrorSeguridad($"La clave no puede pasar de {LargoClave} caracteres (así la guarda Delphi).");
        if (!CifradoDelphi.EsCifrable(c)) throw new ErrorSeguridad("La clave solo puede llevar letras sin acento ni ñ, números y símbolos del teclado.");
        return c;
    }

    public static string Grupo(string? descripcion)
    {
        var d = (descripcion ?? "").Trim().ToUpperInvariant();
        if (d.Length == 0) throw new ErrorSeguridad("Escriba el nombre del grupo.");
        if (d.Length > LargoGrupo) throw new ErrorSeguridad($"El nombre del grupo no puede pasar de {LargoGrupo} caracteres.");
        return d;
    }

    /// <summary>Solo codigos de la lista; sin repetir.</summary>
    public static IReadOnlyList<int> Permisos(IEnumerable<int>? codigos)
    {
        var l = (codigos ?? Array.Empty<int>()).Distinct().ToList();
        if (l.Any(c => PermisosHotel.PorCodigo(c) is null)) throw new ErrorSeguridad("Hay un permiso que no existe.");
        return l;
    }

    /// <summary>
    /// Las tablas del cifrado de Delphi repiten caracteres: algunos textos no vuelven igual al desencriptarlos. El login
    /// y el nombre se leen desencriptados, asi que deben volver iguales.
    /// </summary>
    public static bool Reversible(string texto) =>
        CifradoDelphi.EsCifrable(texto) && CifradoDelphi.Desencriptar(CifradoDelphi.Encriptar(texto)) == texto;
}
