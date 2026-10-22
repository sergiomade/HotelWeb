using Dapper;
using HotelWeb.Api.Datos;
using HotelWeb.Core.Catalogos;
using HotelWeb.Core.Identificacion;

namespace HotelWeb.Api.Catalogos;

public sealed record ClienteResumen(long Numero, string Nombre, string? Rnc, string? Telefono, int TipoComprobante,
    string TipoNombre, bool Activo);

public sealed record Cliente(long Numero, string Nombre, string? Rnc, string? Cedula, string? Pasaporte,
    string? Direccion, string? Sector, string? Ciudad, string? Telefono, string? Contacto, string? Correo,
    int TipoComprobante, bool Itbis, bool Propina, decimal LimiteCredito, string? Condiciones, bool Extranjero,
    bool Activo, string? Estatus);

public sealed record PaginaClientes(IReadOnlyList<ClienteResumen> Clientes, int Total, int Mostrados);

public sealed record ClienteGuardado(Cliente Cliente, IReadOnlyList<string> Avisos);

/// <summary>
/// Clientes (CxcCliente, la misma tabla de cuentas por cobrar). Se crean (numero = mayor + 1 y los valores por
/// omision de Delphi) y se modifican solo las columnas de DatosCliente; no se borran (reservas, cargos y pagos
/// apuntan al cliente): se ponen inactivos.
/// </summary>
public sealed class RepositorioClientes
{
    public const int PorPagina = 50;
    private readonly Conexiones _conexiones;

    public RepositorioClientes(Conexiones conexiones) => _conexiones = conexiones;

    private sealed class Fila
    {
        public decimal NumeroCliente { get; set; }
        public string? Nombre { get; set; }
        public string? RNC { get; set; }
        public string? Cedula { get; set; }
        public string? Passaporte { get; set; }
        public string? Direccion { get; set; }
        public string? Sector { get; set; }
        public string? Ciudad { get; set; }
        public string? Telefono { get; set; }
        public string? Contacto { get; set; }
        public string? E_mail { get; set; }
        public int? CodigoCompFiscal { get; set; }
        public bool? Itbis { get; set; }
        public bool? Propina { get; set; }
        public decimal LimiteCredito { get; set; }
        public string? Condiciones { get; set; }
        public decimal NumeroTipo { get; set; }
        public string? Estatus { get; set; }
    }

    private const string Columnas = """
        NumeroCliente, RTRIM(Nombre) AS Nombre, RTRIM(RNC) AS RNC, RTRIM(Cedula) AS Cedula, RTRIM(Passaporte) AS Passaporte,
        RTRIM(Direccion) AS Direccion, RTRIM(Sector) AS Sector, RTRIM(Ciudad) AS Ciudad, RTRIM(Telefono) AS Telefono,
        RTRIM(Contacto) AS Contacto, RTRIM(E_mail) AS E_mail, CodigoCompFiscal, Itbis, Propina, LimiteCredito,
        RTRIM(Condiciones) AS Condiciones, numerotipo AS NumeroTipo, RTRIM(Estatus) AS Estatus
        """;

    /// <summary>
    /// Busca por numero (exacto), RNC o cedula (empieza por los digitos) o nombre (contiene). Sin texto, los ultimos
    /// creados. Devuelve hasta 50 y el total que coincide.
    /// </summary>
    public PaginaClientes Buscar(string? texto)
    {
        texto = (texto ?? "").Trim();
        var digitos = new string(texto.Where(char.IsAsciiDigit).ToArray());
        var soloNumero = texto.Length > 0 && texto.All(char.IsAsciiDigit);
        var filtro = texto.Length == 0
            ? "1 = 1"
            : "(Nombre LIKE @nombre" +
              (digitos.Length >= 3 ? " OR RNC LIKE @rnc OR Cedula LIKE @rnc" : "") +
              (soloNumero && long.TryParse(texto, out _) ? " OR NumeroCliente = @numero" : "") + ")";
        var p = new
        {
            nombre = new DbString { Value = "%" + texto.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%", IsAnsi = true, Length = 70 },
            rnc = new DbString { Value = digitos + "%", IsAnsi = true, Length = 20 },
            numero = soloNumero && long.TryParse(texto, out var n) ? n : 0,
            cuantos = PorPagina,
        };

        using var cn = _conexiones.Hotel();
        var total = cn.ExecuteScalar<int>($"SELECT COUNT(*) FROM CxcCliente WITH (NOLOCK) WHERE {filtro}", p);
        var orden = texto.Length == 0 ? "NumeroCliente DESC" : "CASE WHEN NumeroCliente = @numero THEN 0 ELSE 1 END, Nombre";
        var filas = cn.Query<Fila>($"SELECT TOP (@cuantos) {Columnas} FROM CxcCliente WITH (NOLOCK) WHERE {filtro} ORDER BY {orden}", p);
        var lista = filas.Select(f => new ClienteResumen((long)f.NumeroCliente, f.Nombre ?? "", Rnc(f.RNC), f.Telefono,
            f.CodigoCompFiscal ?? 0, ReglasCliente.Nombre(f.CodigoCompFiscal ?? 0), ReglasCliente.EsActivo(f.Estatus))).ToList();
        return new PaginaClientes(lista, total, lista.Count);
    }

    public Cliente? Obtener(long numero)
    {
        using var cn = _conexiones.Hotel();
        var f = cn.QueryFirstOrDefault<Fila>($"SELECT {Columnas} FROM CxcCliente WHERE NumeroCliente = @numero", new { numero });
        return f is null ? null : De(f);
    }

    public ClienteGuardado Modificar(long numero, DatosCliente datos, string usuario)
    {
        var actual = Obtener(numero) ?? throw new ErrorCatalogo($"No existe el cliente {numero}.");
        var d = ReglasCliente.Normalizar(datos);
        var v = ReglasCliente.Validar(d, actual.TipoComprobante);
        if (v.Error is { } e) throw new ErrorCatalogo(e);

        using var cn = _conexiones.Hotel();
        // el estatus solo se reescribe si cambia (hay clientes viejos con "A" o "0")
        var estatus = d.Activo == actual.Activo ? null : d.Activo ? "Activo" : "Inactivo";
        var p = new DynamicParameters(Parametros(d, usuario));
        p.Add("numero", numero);
        p.Add("estatus", estatus);
        cn.Execute("""
            UPDATE CxcCliente
               SET Nombre = @Nombre, RNC = @Rnc, Cedula = @Cedula, Passaporte = @Pasaporte, Direccion = @Direccion,
                   Sector = @Sector, Ciudad = @Ciudad, Telefono = ISNULL(@Telefono, '809'), Contacto = @Contacto, E_mail = @Correo,
                   CodigoCompFiscal = @TipoComprobante, Itbis = @Itbis, Propina = @Propina, LimiteCredito = @LimiteCredito,
                   Condiciones = @Condiciones, numerotipo = @numerotipo, Estatus = ISNULL(@estatus, Estatus),
                   Usuario = @usuario
             WHERE NumeroCliente = @numero
            """, p);
        return new ClienteGuardado(Obtener(numero)!, v.Avisos.Concat(Duplicados(cn, d.Rnc, numero)).ToList());
    }

    public ClienteGuardado Crear(DatosCliente datos, string usuario)
    {
        var d = ReglasCliente.Normalizar(datos);
        var v = ReglasCliente.Validar(d);
        if (v.Error is { } e) throw new ErrorCatalogo(e);

        using var cn = _conexiones.Hotel();
        cn.Open();
        using var tx = cn.BeginTransaction();
        // mayor + 1, como Delphi (AdoFindMax); UPDLOCK para que dos a la vez no tomen el mismo numero
        var numero = cn.ExecuteScalar<long>(
            "SELECT ISNULL(MAX(NumeroCliente), 0) + 1 FROM CxcCliente WITH (UPDLOCK, HOLDLOCK)", transaction: tx);
        // cuenta contable de clientes: la de FaParametro, como Delphi (ClienteNewRecord)
        var catalogo = cn.ExecuteScalar<decimal?>("SELECT TOP 1 CuentaCatalogo_idCliente FROM FaParametro", transaction: tx) ?? 0;
        var p = new DynamicParameters(Parametros(d, usuario));
        p.Add("numero", numero);
        p.Add("catalogo", catalogo);
        cn.Execute("""
            INSERT INTO CxcCliente (NumeroCliente, Catalogo_id, NumeroLibro, Nombre, RNC, Cedula, Passaporte, Direccion, Sector,
                                    Ciudad, Telefono, Contacto, E_mail, CodigoCompFiscal, Itbis, Propina, LimiteCredito,
                                    Condiciones, numerotipo, Estatus, Usuario, FechaAdicion)
            VALUES (@numero, @catalogo, 1, @Nombre, @Rnc, @Cedula, @Pasaporte, @Direccion, @Sector,
                    @Ciudad, ISNULL(@Telefono, '809'), @Contacto, @Correo, @TipoComprobante, @Itbis, @Propina, @LimiteCredito,
                    @Condiciones, @numerotipo, 'Activo', @usuario, GETDATE())
            """, p, tx);
        tx.Commit();
        return new ClienteGuardado(Obtener(numero)!, v.Avisos.Concat(Duplicados(cn, d.Rnc, numero)).ToList());
    }

    private static IEnumerable<string> Duplicados(System.Data.IDbConnection cn, string? rnc, long numero)
    {
        if (rnc is null or ReglasCliente.SinRnc) return Array.Empty<string>();
        var otros = cn.Query<(decimal Numero, string Nombre)>(
            "SELECT TOP 3 NumeroCliente, RTRIM(Nombre) FROM CxcCliente WITH (NOLOCK) WHERE RTRIM(RNC) = @rnc AND NumeroCliente <> @numero",
            new { rnc = new DbString { Value = rnc, IsAnsi = true, Length = 13 }, numero }).ToList();
        return otros.Count == 0
            ? Array.Empty<string>()
            : new[] { "Otro cliente tiene el mismo RNC: " + string.Join(", ", otros.Select(o => $"{o.Numero} {o.Nombre}")) };
    }

    private static object Parametros(DatosCliente d, string usuario)
    {
        static DbString A(string? v, int largo) => new() { Value = v, IsAnsi = true, Length = largo };
        return new
        {
            Nombre = A(d.Nombre, 60),
            Rnc = A(d.Rnc, 13),
            Cedula = A(d.Cedula, 13),
            Pasaporte = A(d.Pasaporte, 13),
            Direccion = A(d.Direccion, 50),
            Sector = A(d.Sector, 30),
            Ciudad = A(d.Ciudad, 15),
            Telefono = A(d.Telefono, 15),
            // Contacto y E_mail son NOT NULL sin valor por omision: vacio, como deja Delphi
            Contacto = A(d.Contacto ?? "", 30),
            Correo = A(d.Correo ?? "", 50),
            d.TipoComprobante,
            d.Itbis,
            d.Propina,
            d.LimiteCredito,
            Condiciones = A(d.Condiciones, 15),
            numerotipo = d.Extranjero ? 2 : 1,
            usuario = A(usuario.Length > 10 ? usuario[..10] : usuario, 10),
        };
    }

    private static string? Rnc(string? rnc) => IdentificacionRD.SinIdentificacion(rnc) ? null : rnc?.Trim();

    private static Cliente De(Fila f) => new((long)f.NumeroCliente, f.Nombre ?? "", Rnc(f.RNC), f.Cedula, f.Passaporte,
        f.Direccion == ReglasCliente.SinDireccion ? null : f.Direccion, f.Sector, f.Ciudad,
        f.Telefono is "809" ? null : f.Telefono, f.Contacto, f.E_mail, f.CodigoCompFiscal ?? 0, f.Itbis == true,
        f.Propina == true, f.LimiteCredito, f.Condiciones, f.NumeroTipo == 2, ReglasCliente.EsActivo(f.Estatus), f.Estatus);
}
