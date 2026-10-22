using HotelWeb.Core.Identificacion;

namespace HotelWeb.Core.Catalogos;

/// <summary>Lo que la web cambia de un cliente (CxcCliente). Las demas columnas (cuenta contable, vendedor...) no se tocan.</summary>
public sealed record DatosCliente(
    string Nombre, string? Rnc, string? Cedula, string? Pasaporte,
    string? Direccion, string? Sector, string? Ciudad, string? Telefono, string? Contacto, string? Correo,
    int TipoComprobante, bool Itbis, bool Propina, decimal LimiteCredito, string? Condiciones,
    bool Extranjero, bool Activo);

public sealed record ResultadoValidacion(string? Error, IReadOnlyList<string> Avisos);

/// <summary>
/// Reglas del catalogo de clientes. Como Delphi (UFrmMntClientesx): nombre en mayusculas, RNC vacio = 000000000,
/// RNC de 9 u 11 digitos. Ademas, con el digito verificador: obligatorio si el tipo de comprobante lo pide (E31, E44,
/// E45); en consumo (E32) solo se avisa, porque hay clientes viejos con el RNC mal escrito.
/// </summary>
public static class ReglasCliente
{
    public const string SinRnc = "000000000";
    public const string SinDireccion = "sin direccion";   // el valor por omision de la columna
    public const string CondicionesPorOmision = "30 Dias";

    /// <summary>ComprobanteFiscal.RecordKey_id que se le pueden poner a un cliente del hotel.</summary>
    public static readonly IReadOnlyDictionary<int, string> Comprobantes = new Dictionary<int, string>
    {
        [1] = "E31 Credito fiscal",
        [2] = "E32 Consumo",
        [13] = "E44 Regimenes especiales",
        [14] = "E45 Gubernamental",
    };

    /// <summary>Los que exigen RNC o cedula valida.</summary>
    public static bool PideRnc(int tipo) => tipo is 1 or 13 or 14;

    public static DatosCliente Normalizar(DatosCliente d)
    {
        static string? T(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        var rnc = IdentificacionRD.Limpiar(d.Rnc);
        return d with
        {
            Nombre = (d.Nombre ?? "").Trim().ToUpperInvariant(),
            Rnc = IdentificacionRD.SinIdentificacion(rnc) ? SinRnc : rnc,
            Cedula = T(d.Cedula), Pasaporte = T(d.Pasaporte)?.ToUpperInvariant(),
            Direccion = T(d.Direccion) ?? SinDireccion, Sector = T(d.Sector), Ciudad = T(d.Ciudad),
            Telefono = T(d.Telefono), Contacto = T(d.Contacto), Correo = T(d.Correo),
            Condiciones = T(d.Condiciones) ?? CondicionesPorOmision,
        };
    }

    /// <param name="tipoActual">El tipo que ya tenia el cliente: si no es de la lista (como el 16 del cliente 2),
    /// se puede guardar sin cambiarlo.</param>
    public static ResultadoValidacion Validar(DatosCliente d, int? tipoActual = null)
    {
        var avisos = new List<string>();
        ResultadoValidacion Mal(string e) => new(e, avisos);

        if (d.Nombre.Length == 0) return Mal("Escriba el nombre del cliente.");
        if (d.Nombre.Length > 60) return Mal("El nombre lleva a lo sumo 60 letras.");
        if (!Comprobantes.ContainsKey(d.TipoComprobante) && d.TipoComprobante != tipoActual)
            return Mal("Elija el tipo de comprobante.");

        var rnc = d.Rnc ?? SinRnc;
        var sinRnc = rnc == SinRnc;
        if (PideRnc(d.TipoComprobante))
        {
            if (!IdentificacionRD.EsRncOCedulaValido(rnc))
                return Mal($"{Nombre(d.TipoComprobante)} pide un RNC (9) o cedula (11) con su digito verificador correcto.");
        }
        else if (!sinRnc)
        {
            if (!IdentificacionRD.SoloDigitos(rnc) || rnc.Length is not (9 or 11))
                return Mal("El RNC lleva 9 digitos (o la cedula 11), como en Delphi.");
            if (!IdentificacionRD.EsRncOCedulaValido(rnc))
                avisos.Add("El digito verificador del RNC no cuadra: revise el numero.");
        }

        if ((d.Cedula?.Length ?? 0) > 13) return Mal("La cedula lleva a lo sumo 13 caracteres.");
        if ((d.Pasaporte?.Length ?? 0) > 13) return Mal("El pasaporte lleva a lo sumo 13 caracteres.");
        if ((d.Direccion?.Length ?? 0) > 50) return Mal("La direccion lleva a lo sumo 50 letras.");
        if ((d.Sector?.Length ?? 0) > 30) return Mal("El sector lleva a lo sumo 30 letras.");
        if ((d.Ciudad?.Length ?? 0) > 15) return Mal("La ciudad lleva a lo sumo 15 letras.");
        if ((d.Telefono?.Length ?? 0) > 15) return Mal("El telefono lleva a lo sumo 15 caracteres.");
        if ((d.Contacto?.Length ?? 0) > 30) return Mal("El contacto lleva a lo sumo 30 letras.");
        if ((d.Correo?.Length ?? 0) > 50) return Mal("El correo lleva a lo sumo 50 letras.");
        // aviso y no error: hay correos viejos como "0" o "N/A" (el e-CF los omite si no son validos)
        if (d.Correo is { Length: > 0 } c && !CorreoValido(c)) avisos.Add("El correo no tiene forma de correo (nombre@dominio.com): no saldra en el e-CF.");
        if ((d.Condiciones?.Length ?? 0) > 15) return Mal("Las condiciones llevan a lo sumo 15 letras.");
        if (d.LimiteCredito < 0) return Mal("El limite de credito no puede ser negativo.");
        if (d.LimiteCredito > 99_999_999m) return Mal("El limite de credito es demasiado grande.");

        if (d.TipoComprobante == 13 && d.Itbis)
            avisos.Add("Regimen especial (E44): al cobrar no se le pone ITBIS aunque este marcado.");
        return new ResultadoValidacion(null, avisos);
    }

    public static string Nombre(int tipo) => Comprobantes.TryGetValue(tipo, out var n) ? n : $"Tipo {tipo}";

    /// <summary>
    /// Estatus de CxcCliente. Como Delphi (UFrmMntClientesx): solo "Inactivo" es inactivo; "Activo", "A" y los "0" de
    /// clientes viejos cuentan como activos.
    /// </summary>
    public static bool EsActivo(string? estatus) =>
        !string.Equals(estatus?.Trim(), "Inactivo", StringComparison.OrdinalIgnoreCase);

    /// <summary>Forma de correo que acepta el XSD de la DGII (como CertificacionWeb).</summary>
    public static bool CorreoValido(string correo)
    {
        var n = correo.Length;
        if (n < 5 || n > 80) return false;
        static bool Letra(char ch) => char.IsAsciiLetterOrDigit(ch) || ch == '_';
        int arroba = 0, punto = 0;
        for (var i = 0; i < n; i++)
        {
            var ch = correo[i];
            if (Letra(ch)) continue;
            if (ch is not ('.' or '-' or '+' or '@')) return false;
            if (i == 0 || i == n - 1 || !Letra(correo[i - 1]) || !Letra(correo[i + 1])) return false;
            if (ch == '@')
            {
                if (arroba > 0) return false;
                arroba = i;
            }
            else if (ch == '.' && arroba > 0) punto = i;
        }
        return arroba > 0 && punto > arroba;
    }
}
