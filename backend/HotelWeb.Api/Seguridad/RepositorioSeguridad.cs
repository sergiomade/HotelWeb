using System.Data;
using Dapper;
using HotelWeb.Api.Datos;
using HotelWeb.Core.Seguridad;
using Microsoft.Data.SqlClient;

namespace HotelWeb.Api.Seguridad;

/// <summary>Usuario del hotel (esta en algun grupo del hotel), con login y nombre ya desencriptados.</summary>
public sealed record UsuarioSeguridad(int Id, string Login, string Nombre, bool Activo, bool CancelaFactura, IReadOnlyList<int> Grupos);

/// <summary>Grupo del hotel: tiene la opcion 4000 del modulo 40 en Seg_Permiso_Grupo.</summary>
public sealed record GrupoSeguridad(int Id, string Descripcion, bool Activo, IReadOnlyList<int> Permisos, int Miembros);

/// <summary>
/// Usuarios, grupos y permisos de la web en las tablas del modulo de Seguridad de Delphi (base Seguridad): Seg_Usuario,
/// Seg_Grupo, Seg_Grupo_Usuario, Seg_Modulos, Seg_Opcion y Seg_Permiso_Grupo. La base la comparten otros programas:
/// la web solo AGREGA su modulo 40 y sus grupos, y solo ve y cambia a los usuarios que estan en grupos del hotel.
/// Nunca borra usuarios ni grupos (se desactivan) ni cambia Usuario_Id (los triggers de Delphi lo impiden).
/// </summary>
public sealed class RepositorioSeguridad
{
    private const int M = PermisosHotel.Modulo;
    private const int Marca = PermisosHotel.MarcaGrupo;
    /// <summary>Permiso_Codigo de Delphi: '6' = Navegar (la web solo mira que la fila exista y sea visible).</summary>
    private const string CodigoDelphi = "6";

    /// <summary>Columnas de Seg_Usuario que se llenan al crear un usuario, si la base las tiene (valores fijos).</summary>
    private static readonly (string Columna, string Valor)[] ExtrasUsuarioNuevo =
    {
        ("Id_Personal", "0"), ("confidencial", "'N'"), ("nomina", "'N'"), ("Sucursal_id", "1"),
        ("ModuloCheques", "'N'"), ("ModuloCaja", "'N'"), ("ModuloContabilidad", "'N'"), ("Supervisor", "0"),
        ("CancelaFactura", "@cancelaFactura"),
    };

    private readonly Conexiones _conexiones;

    public RepositorioSeguridad(Conexiones conexiones) => _conexiones = conexiones;

    /// <summary>True si ya existe el modulo 40 (se activo la seguridad de la web).</summary>
    public bool Configurada()
    {
        using var cn = _conexiones.Seguridad();
        return cn.ExecuteScalar<int>("SELECT COUNT(*) FROM Seg_Modulos WHERE Modulo_Codigo = @M", new { M }) > 0;
    }

    /// <summary>Los permisos (Opcion_Codigo) que dan al usuario sus grupos ACTIVOS del hotel.</summary>
    public HashSet<int> PermisosDe(int usuarioId)
    {
        using var cn = _conexiones.Seguridad();
        return cn.Query<int>(
            "SELECT DISTINCT CAST(p.Opcion_Codigo AS int) FROM Seg_Grupo_Usuario gu " +
            "JOIN Seg_Grupo g ON g.Grupo_Id = gu.Grupo_Id AND g.Estado = 1 " +
            "JOIN Seg_Permiso_Grupo p ON p.Grupo_Id = g.Grupo_Id AND p.Modulo_Codigo = @M AND p.visible = 1 " +
            "WHERE gu.Usuario_Id = @usuarioId AND p.Opcion_Codigo <> @Marca",
            new { M, Marca, usuarioId }).ToHashSet();
    }

    private const string FiltroGrupoHotel =
        "EXISTS (SELECT 1 FROM Seg_Permiso_Grupo pm WHERE pm.Grupo_Id = g.Grupo_Id AND pm.Modulo_Codigo = 40 AND pm.Opcion_Codigo = 4000)";

    private sealed record FilaGrupo(int Grupo_Id, string Descripcion, bool Estado);
    private sealed record FilaPar(int A, int B);
    private sealed record FilaUsuario(int Usuario_Id, string? Cuenta_Login, string? Nombre, bool Estado, bool? CancelaFactura);

    public IReadOnlyList<GrupoSeguridad> Grupos()
    {
        using var cn = _conexiones.Seguridad();
        var grupos = cn.Query<FilaGrupo>(
            $"SELECT CAST(g.Grupo_Id AS int) Grupo_Id, g.Descripcion, g.Estado FROM Seg_Grupo g WHERE {FiltroGrupoHotel} ORDER BY g.Descripcion").ToList();
        var permisos = cn.Query<FilaPar>(
            "SELECT CAST(Grupo_Id AS int) A, CAST(Opcion_Codigo AS int) B FROM Seg_Permiso_Grupo " +
            "WHERE Modulo_Codigo = @M AND Opcion_Codigo <> @Marca AND visible = 1", new { M, Marca }).ToLookup(x => x.A, x => x.B);
        var miembros = cn.Query<FilaPar>(
            "SELECT CAST(Grupo_Id AS int) A, CAST(Usuario_Id AS int) B FROM Seg_Grupo_Usuario").ToLookup(x => x.A);
        return grupos.Select(g => new GrupoSeguridad(g.Grupo_Id, g.Descripcion.Trim(), g.Estado,
            permisos[g.Grupo_Id].OrderBy(x => x).ToList(), miembros[g.Grupo_Id].Count())).ToList();
    }

    /// <summary>Solo los usuarios que estan en algun grupo del hotel (la base la usan otros programas).</summary>
    public IReadOnlyList<UsuarioSeguridad> Usuarios()
    {
        using var cn = _conexiones.Seguridad();
        var grupos = cn.Query<FilaPar>(
            "SELECT CAST(gu.Usuario_Id AS int) A, CAST(gu.Grupo_Id AS int) B FROM Seg_Grupo_Usuario gu " +
            $"JOIN Seg_Grupo g ON g.Grupo_Id = gu.Grupo_Id WHERE {FiltroGrupoHotel}").ToLookup(x => x.A, x => x.B);
        var filas = cn.Query<FilaUsuario>(
            "SELECT CAST(u.Usuario_Id AS int) Usuario_Id, u.Cuenta_Login, u.Nombre, u.Estado, u.CancelaFactura FROM Seg_Usuario u " +
            "WHERE EXISTS (SELECT 1 FROM Seg_Grupo_Usuario gu JOIN Seg_Grupo g ON g.Grupo_Id = gu.Grupo_Id " +
            $"WHERE gu.Usuario_Id = u.Usuario_Id AND {FiltroGrupoHotel})");
        return filas.Select(f => new UsuarioSeguridad(f.Usuario_Id,
                CifradoDelphi.Desencriptar(f.Cuenta_Login ?? "").Trim(), CifradoDelphi.Desencriptar(f.Nombre ?? "").Trim(),
                f.Estado, f.CancelaFactura == true, grupos[f.Usuario_Id].OrderBy(x => x).ToList()))
            .OrderBy(u => u.Nombre, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Crea el modulo 40, sus opciones (los permisos de la lista) y dos grupos: ADMIN HOTEL (todos los permisos) con
    /// los usuarios dados, y RECEPCION (las areas de trabajo) con los activos del grupo HOTEL de Delphi. Una sola vez.
    /// </summary>
    public void Activar(IReadOnlyCollection<int> administradores)
    {
        Transaccion((cn, tx) =>
        {
            var modulo = cn.QueryFirstOrDefault<string>(
                "SELECT Descripcion FROM Seg_Modulos WITH (UPDLOCK, HOLDLOCK) WHERE Modulo_Codigo = @M", new { M }, tx);
            if (modulo is not null)
                throw new ErrorSeguridad(modulo.Trim() == PermisosHotel.NombreModulo
                    ? "La seguridad de la web ya está activada."
                    : $"El módulo {M} de la base Seguridad ya lo usa otro programa ({modulo.Trim()}). Avise a sistemas.");
            // triggers de Delphi con "raiserror @errno @errmsg": en un SQL Server nuevo no compilan y fallan siempre
            // (no se podria agregar a nadie a un grupo). Se corrigen con deploy/sql/seguridad-triggers-raiserror.sql
            var viejos = cn.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM sys.triggers t WHERE OBJECT_NAME(t.parent_id) LIKE 'Seg[_]%' " +
                "AND OBJECT_DEFINITION(t.object_id) LIKE '%raiserror @errno @errmsg%'", transaction: tx);
            if (viejos > 0)
                throw new ErrorSeguridad($"La base Seguridad tiene {viejos} triggers antiguos que este SQL Server no acepta. " +
                                         "Antes de activar, ejecute en ella deploy\\sql\\seguridad-triggers-raiserror.sql.");
            var admins = cn.Query<int>(
                "SELECT CAST(Usuario_Id AS int) FROM Seg_Usuario WHERE Estado = 1 AND Usuario_Id IN @ids",
                new { ids = administradores.ToArray() }, tx).ToList();
            if (admins.Count == 0) throw new ErrorSeguridad("No hay un usuario activo para el grupo de administradores.");

            cn.Execute("INSERT INTO Seg_Modulos (Modulo_Codigo, Descripcion) VALUES (@M, @d)",
                new { M, d = PermisosHotel.NombreModulo }, tx);
            AsegurarOpciones(cn, tx);

            var admin = NuevoGrupo(cn, tx, "ADMIN HOTEL", PermisosHotel.Todos.Select(p => p.Codigo).ToList());
            var recepcion = NuevoGrupo(cn, tx, "RECEPCION", PermisosHotel.DeRecepcion.Select(p => p.Codigo).ToList());
            cn.Execute("INSERT INTO Seg_Grupo_Usuario (Usuario_Id, Grupo_Id) VALUES (@u, @admin)",
                admins.Select(u => new { u, admin }), tx);
            // decision del usuario (2026-10-24): los usuarios activos del grupo HOTEL de Delphi entran a RECEPCION
            cn.Execute(
                "INSERT INTO Seg_Grupo_Usuario (Usuario_Id, Grupo_Id) SELECT DISTINCT gu.Usuario_Id, @recepcion " +
                "FROM Seg_Grupo_Usuario gu JOIN Seg_Grupo g ON g.Grupo_Id = gu.Grupo_Id " +
                "JOIN Seg_Usuario u ON u.Usuario_Id = gu.Usuario_Id AND u.Estado = 1 " +
                "WHERE UPPER(LTRIM(RTRIM(g.Descripcion))) = 'HOTEL'",
                new { recepcion }, tx);
        });
    }

    /// <summary>Usuario nuevo en Seg_Usuario (login, clave y nombre con el cifrado de Delphi), en grupos del hotel.</summary>
    public int CrearUsuario(string login, string nombre, string clave, bool cancelaFactura, IReadOnlyList<int> grupos)
    {
        login = ReglasSeguridad.Login(login);
        nombre = ReglasSeguridad.Nombre(nombre);
        clave = ReglasSeguridad.Clave(clave);
        var id = 0;
        Transaccion((cn, tx) =>
        {
            ValidarGrupos(cn, tx, grupos, alMenosUno: true);
            if (BuscarLogin(cn, tx, login) is not null)
                throw new ErrorSeguridad($"Ya existe el usuario {login}. Si es de otro programa, use \"Agregar usuario existente\".");
            id = cn.ExecuteScalar<int>(
                "SELECT CAST(ISNULL(MAX(Usuario_Id), 0) + 1 AS int) FROM Seg_Usuario WITH (UPDLOCK, HOLDLOCK)", transaction: tx);
            // valores de un usuario del hotel: sin supervisor (no entra al programa de Seguridad de Delphi) ni los
            // modulos de contabilidad, cheques o caja de los otros programas; el Delphi del hotel solo mira Estado.
            // Las columnas cambian entre bases (hseguridad no tiene Id_Personal): solo se llenan las que existen.
            var existen = cn.Query<string>(
                "SELECT name FROM sys.columns WHERE object_id = OBJECT_ID('Seg_Usuario')", transaction: tx)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var columnas = new List<string> { "Usuario_Id", "Nombre", "Estado", "Cuenta_Login", "Clave_Acceso" };
            var valores = new List<string> { "@id", "@nombre", "1", "@login", "@clave" };
            foreach (var (columna, valor) in ExtrasUsuarioNuevo)
            {
                if (!existen.Contains(columna)) continue;
                columnas.Add(columna);
                valores.Add(valor);
            }
            cn.Execute(
                $"INSERT INTO Seg_Usuario ({string.Join(", ", columnas)}) VALUES ({string.Join(", ", valores)})",
                new { id, nombre = Ansi(nombre, ReglasSeguridad.LargoNombre), login = Ansi(login, ReglasSeguridad.LargoLogin),
                      clave = Ansi(clave, ReglasSeguridad.LargoClave), cancelaFactura }, tx);
            PonerGrupos(cn, tx, id, grupos);
        });
        return id;
    }

    /// <summary>Un usuario que ya existe (de Delphi u otro programa) se agrega a grupos del hotel.</summary>
    public int AgregarUsuario(string login, IReadOnlyList<int> grupos)
    {
        login = (login ?? "").Trim();
        if (login.Length == 0 || !CifradoDelphi.EsCifrable(login)) throw new ErrorSeguridad("Escriba el usuario (login) exacto.");
        var id = 0;
        Transaccion((cn, tx) =>
        {
            ValidarGrupos(cn, tx, grupos, alMenosUno: true);
            id = BuscarLogin(cn, tx, login) ?? throw new ErrorSeguridad($"No existe el usuario {login}.");
            if (GruposHotelDe(cn, tx, id).Count > 0)
                throw new ErrorSeguridad($"El usuario {login} ya está en el hotel: búsquelo en la lista.");
            PonerGrupos(cn, tx, id, grupos);
        });
        return id;
    }

    /// <summary>
    /// Nombre, activo, si autoriza notas de credito (Seg_Usuario.CancelaFactura, la misma bandera de Delphi) y sus grupos
    /// del hotel (los grupos de otros programas no se tocan). Sin grupos del hotel deja de verse en la lista.
    /// </summary>
    public void ModificarUsuario(int id, string nombre, bool activo, bool cancelaFactura, IReadOnlyList<int> grupos)
    {
        nombre = ReglasSeguridad.Nombre(nombre);
        Transaccion((cn, tx) =>
        {
            DelHotel(cn, tx, id);
            ValidarGrupos(cn, tx, grupos, alMenosUno: false);
            cn.Execute("UPDATE Seg_Usuario SET Nombre = @nombre, Estado = @activo, CancelaFactura = @cancelaFactura WHERE Usuario_Id = @id",
                new { id, nombre = Ansi(nombre, ReglasSeguridad.LargoNombre), activo, cancelaFactura }, tx);
            var antes = GruposHotelDe(cn, tx, id);
            cn.Execute("DELETE FROM Seg_Grupo_Usuario WHERE Usuario_Id = @id AND Grupo_Id = @g",
                antes.Except(grupos).Select(g => new { id, g }), tx);
            cn.Execute("INSERT INTO Seg_Grupo_Usuario (Usuario_Id, Grupo_Id) VALUES (@id, @g)",
                grupos.Except(antes).Select(g => new { id, g }), tx);
            QuedaAdministrador(cn, tx);
        });
    }

    /// <summary>El administrador pone una clave nueva a un usuario del hotel.</summary>
    public void PonerClave(int id, string clave)
    {
        clave = ReglasSeguridad.Clave(clave);
        Transaccion((cn, tx) =>
        {
            DelHotel(cn, tx, id);
            GrabarClave(cn, tx, id, clave);
        });
    }

    /// <summary>Cada usuario cambia su propia clave (debe dar la actual). False si la actual no coincide.</summary>
    public bool CambiarMiClave(int id, string actual, string nueva)
    {
        nueva = ReglasSeguridad.Clave(nueva);
        actual ??= "";
        if (actual.Length == 0 || actual.Length > ReglasSeguridad.LargoClave || !CifradoDelphi.EsCifrable(actual)) return false;
        var ok = false;
        Transaccion((cn, tx) =>
        {
            ok = cn.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM Seg_Usuario WITH (UPDLOCK) WHERE Usuario_Id = @id AND Estado = 1 AND Clave_Acceso = @actual",
                new { id, actual = Ansi(actual, ReglasSeguridad.LargoClave) }, tx) == 1;
            if (ok) GrabarClave(cn, tx, id, nueva);
        });
        return ok;
    }

    public int CrearGrupo(string descripcion, IReadOnlyList<int> permisos)
    {
        descripcion = ReglasSeguridad.Grupo(descripcion);
        permisos = ReglasSeguridad.Permisos(permisos);
        var id = 0;
        Transaccion((cn, tx) =>
        {
            AsegurarOpciones(cn, tx);
            NombreLibre(cn, tx, descripcion, 0);
            id = NuevoGrupo(cn, tx, descripcion, permisos);
        });
        return id;
    }

    public void ModificarGrupo(int id, string descripcion, bool activo, IReadOnlyList<int> permisos)
    {
        descripcion = ReglasSeguridad.Grupo(descripcion);
        permisos = ReglasSeguridad.Permisos(permisos);
        Transaccion((cn, tx) =>
        {
            if (!GruposHotel(cn, tx).Contains(id)) throw new ErrorSeguridad("Ese grupo no es del hotel.");
            AsegurarOpciones(cn, tx);
            NombreLibre(cn, tx, descripcion, id);
            cn.Execute("UPDATE Seg_Grupo SET Descripcion = @descripcion, Estado = @activo WHERE Grupo_Id = @id",
                new { id, descripcion, activo }, tx);
            cn.Execute("DELETE FROM Seg_Permiso_Grupo WHERE Grupo_Id = @id AND Modulo_Codigo = @M AND Opcion_Codigo <> @Marca",
                new { id, M, Marca }, tx);
            InsertarPermisos(cn, tx, id, permisos);
            QuedaAdministrador(cn, tx);
        });
    }

    /// <summary>
    /// Pone al dia una base ya activada cuando el programa trae permisos nuevos (ver AsegurarOpciones). La llama
    /// PermisosWeb una vez al arrancar, antes de leer los permisos de nadie.
    /// </summary>
    public void Sincronizar() => Transaccion(AsegurarOpciones);

    // ---- ayudas (todas dentro de la transaccion) ----

    /// <summary>
    /// Crea en Seg_Opcion las opciones del modulo 40 que falten (permisos nuevos del programa) y se las da a los grupos
    /// que tenian su permiso "Hereda": las pestanas nuevas a quien ya veia el area, y las cinco acciones de catalogos a
    /// quien tenia la vieja 4010 (que despues se quita). Asi nadie pierde ni gana nada al actualizar la web.
    /// </summary>
    private static void AsegurarOpciones(SqlConnection cn, IDbTransaction tx)
    {
        var existen = cn.Query<int>(
            "SELECT CAST(Opcion_Codigo AS int) FROM Seg_Opcion WITH (UPDLOCK, HOLDLOCK) WHERE Modulo_Codigo = @M",
            new { M }, tx).ToHashSet();
        var faltan = new[] { (Codigo: Marca, Nombre: "MENU PRINCIPAL", Hereda: (int?)null) }
            .Concat(PermisosHotel.Todos.Select(p => (p.Codigo, Nombre: p.Nombre.ToUpperInvariant(), p.Hereda)))
            .Where(p => !existen.Contains(p.Codigo)).ToList();
        if (faltan.Count == 0) return;

        cn.Execute("INSERT INTO Seg_Opcion (Opcion_Codigo, Modulo_Codigo, Descripcion, Acciones_Posibles, Imagen, Orden) " +
                   "VALUES (@Codigo, @M, @Nombre, @CodigoDelphi, NULL, @Orden)",
            faltan.Select(p => new
            {
                p.Codigo, M, CodigoDelphi, Orden = p.Codigo.ToString(),
                Nombre = p.Nombre.Length > 50 ? p.Nombre[..50] : p.Nombre,   // Seg_Opcion.Descripcion varchar(50)
            }), tx);
        cn.Execute("INSERT INTO Seg_Permiso_Grupo (Opcion_Codigo, Grupo_Id, Modulo_Codigo, Permiso_Codigo, visible) " +
                   "SELECT @Codigo, p.Grupo_Id, @M, @CodigoDelphi, 1 FROM Seg_Permiso_Grupo p " +
                   "WHERE p.Modulo_Codigo = @M AND p.Opcion_Codigo = @Hereda AND p.visible = 1 " +
                   "AND NOT EXISTS (SELECT 1 FROM Seg_Permiso_Grupo x WHERE x.Grupo_Id = p.Grupo_Id AND x.Modulo_Codigo = @M AND x.Opcion_Codigo = @Codigo)",
            faltan.Where(p => p.Hereda is not null).Select(p => new { p.Codigo, M, CodigoDelphi, p.Hereda }), tx);
        if (faltan.Any(p => p.Hereda == PermisosHotel.EditarCatalogosViejo))
            cn.Execute("DELETE FROM Seg_Permiso_Grupo WHERE Modulo_Codigo = @M AND Opcion_Codigo = @viejo",
                new { M, viejo = PermisosHotel.EditarCatalogosViejo }, tx);
    }

    private void Transaccion(Action<SqlConnection, IDbTransaction> trabajo)
    {
        using var cn = _conexiones.Seguridad();
        cn.Open();
        using var tx = cn.BeginTransaction(IsolationLevel.ReadCommitted);
        trabajo(cn, tx);
        tx.Commit();
    }

    /// <summary>varchar como las columnas: los caracteres del cifrado son de Windows-1252.</summary>
    private static DbString Ansi(string texto, int largo) =>
        new() { Value = CifradoDelphi.Encriptar(texto), IsAnsi = true, Length = largo };

    private static int? BuscarLogin(SqlConnection cn, IDbTransaction tx, string login) =>
        cn.QueryFirstOrDefault<int?>(
            "SELECT CAST(Usuario_Id AS int) FROM Seg_Usuario WITH (UPDLOCK, HOLDLOCK) WHERE Cuenta_Login = @login",
            new { login = Ansi(login, ReglasSeguridad.LargoLogin) }, tx);

    private static void GrabarClave(SqlConnection cn, IDbTransaction tx, int id, string clave) =>
        cn.Execute("UPDATE Seg_Usuario SET Clave_Acceso = @clave WHERE Usuario_Id = @id",
            new { id, clave = Ansi(clave, ReglasSeguridad.LargoClave) }, tx);

    private static HashSet<int> GruposHotel(SqlConnection cn, IDbTransaction tx) =>
        cn.Query<int>($"SELECT CAST(g.Grupo_Id AS int) FROM Seg_Grupo g WHERE {FiltroGrupoHotel}", transaction: tx).ToHashSet();

    private static HashSet<int> GruposHotelDe(SqlConnection cn, IDbTransaction tx, int usuarioId) =>
        cn.Query<int>("SELECT CAST(gu.Grupo_Id AS int) FROM Seg_Grupo_Usuario gu JOIN Seg_Grupo g ON g.Grupo_Id = gu.Grupo_Id " +
                      $"WHERE gu.Usuario_Id = @usuarioId AND {FiltroGrupoHotel}", new { usuarioId }, tx).ToHashSet();

    /// <summary>Solo se cambian usuarios que esten en grupos del hotel.</summary>
    private static void DelHotel(SqlConnection cn, IDbTransaction tx, int id)
    {
        if (GruposHotelDe(cn, tx, id).Count == 0) throw new ErrorSeguridad("Ese usuario no es del hotel.");
    }

    private static void ValidarGrupos(SqlConnection cn, IDbTransaction tx, IReadOnlyList<int> grupos, bool alMenosUno)
    {
        if (alMenosUno && grupos.Count == 0) throw new ErrorSeguridad("Elija al menos un grupo.");
        var delHotel = GruposHotel(cn, tx);
        if (grupos.Any(g => !delHotel.Contains(g))) throw new ErrorSeguridad("Hay un grupo que no es del hotel.");
    }

    private static void PonerGrupos(SqlConnection cn, IDbTransaction tx, int id, IReadOnlyList<int> grupos) =>
        cn.Execute("INSERT INTO Seg_Grupo_Usuario (Usuario_Id, Grupo_Id) VALUES (@id, @g)",
            grupos.Distinct().Select(g => new { id, g }), tx);

    private static void NombreLibre(SqlConnection cn, IDbTransaction tx, string descripcion, int id)
    {
        // entre los grupos del hotel (los de otros programas pueden repetir nombres, como COBROS)
        var otro = cn.ExecuteScalar<int>(
            $"SELECT COUNT(*) FROM Seg_Grupo g WHERE {FiltroGrupoHotel} AND g.Descripcion = @descripcion AND g.Grupo_Id <> @id",
            new { descripcion, id }, tx);
        if (otro > 0) throw new ErrorSeguridad($"Ya hay un grupo {descripcion}.");
    }

    private static int NuevoGrupo(SqlConnection cn, IDbTransaction tx, string descripcion, IReadOnlyList<int> permisos)
    {
        var id = cn.ExecuteScalar<int>(
            "SELECT CAST(ISNULL(MAX(Grupo_Id), 0) + 1 AS int) FROM Seg_Grupo WITH (UPDLOCK, HOLDLOCK)", transaction: tx);
        cn.Execute("INSERT INTO Seg_Grupo (Grupo_Id, Descripcion, Estado) VALUES (@id, @descripcion, 1)", new { id, descripcion }, tx);
        InsertarPermisos(cn, tx, id, new[] { Marca }.Concat(permisos).ToList());
        return id;
    }

    private static void InsertarPermisos(SqlConnection cn, IDbTransaction tx, int grupo, IReadOnlyList<int> permisos) =>
        cn.Execute("INSERT INTO Seg_Permiso_Grupo (Opcion_Codigo, Grupo_Id, Modulo_Codigo, Permiso_Codigo, visible) " +
                   "VALUES (@p, @grupo, @M, @CodigoDelphi, 1)",
            permisos.Distinct().Select(p => new { p, grupo, M, CodigoDelphi }), tx);

    /// <summary>Despues de un cambio debe quedar al menos un usuario activo, en un grupo activo, que administre.</summary>
    private static void QuedaAdministrador(SqlConnection cn, IDbTransaction tx)
    {
        var n = cn.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM Seg_Usuario u WHERE u.Estado = 1 AND EXISTS (SELECT 1 FROM Seg_Grupo_Usuario gu " +
            "JOIN Seg_Grupo g ON g.Grupo_Id = gu.Grupo_Id AND g.Estado = 1 " +
            "JOIN Seg_Permiso_Grupo p ON p.Grupo_Id = g.Grupo_Id AND p.Modulo_Codigo = @M AND p.Opcion_Codigo = @admin AND p.visible = 1 " +
            "WHERE gu.Usuario_Id = u.Usuario_Id)",
            new { M, admin = PermisosHotel.AdministrarSeguridad.Codigo }, tx);
        if (n == 0) throw new ErrorSeguridad("Debe quedar al menos un usuario activo que pueda administrar la seguridad.");
    }
}
