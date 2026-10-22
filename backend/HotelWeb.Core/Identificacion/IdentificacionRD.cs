namespace HotelWeb.Core.Identificacion;

public enum TipoIdentificacion { Invalido, Cedula, Rnc, Pasaporte }

/// <summary>
/// RNC, cedula y pasaporte, igual que Comunes\uIdentificacionRD.pas de Delphi (lo que ya usan el hotel y la
/// cafeteria antes de dar un E31): cedula de 11 digitos con Luhn, RNC de 9 con modulo 11, y pasaporte de 5 a 20
/// caracteres alfanumericos que no sea cedula ni RNC.
/// </summary>
public static class IdentificacionRD
{
    /// <summary>Sin guiones, espacios, puntos ni barras, en mayusculas.</summary>
    public static string Limpiar(string? valor) =>
        new((valor ?? "").Where(c => c is not ('-' or ' ' or '.' or '/')).Select(char.ToUpperInvariant).ToArray());

    public static bool SoloDigitos(string s) => s.Length > 0 && s.All(char.IsAsciiDigit);

    public static bool ValidarCedula(string cedula)
    {
        if (cedula.Length != 11 || !SoloDigitos(cedula)) return false;
        var suma = 0;
        for (var i = 1; i <= 10; i++)
        {
            var p = (cedula[i - 1] - '0') * (i % 2 == 1 ? 1 : 2);
            suma += p > 9 ? p - 9 : p;
        }
        return (10 - suma % 10) % 10 == cedula[10] - '0';
    }

    public static bool ValidarRnc(string rnc)
    {
        int[] pesos = { 7, 9, 8, 6, 5, 4, 3, 2 };
        if (rnc.Length != 9 || !SoloDigitos(rnc)) return false;
        var suma = 0;
        for (var i = 0; i < 8; i++) suma += (rnc[i] - '0') * pesos[i];
        var resto = suma % 11;
        var dv = resto switch { 0 => 2, 1 => 1, _ => 11 - resto };
        return dv == rnc[8] - '0';
    }

    public static TipoIdentificacion Determinar(string? valor, out string limpio)
    {
        limpio = Limpiar(valor?.Trim());
        if (limpio.Length == 0) return TipoIdentificacion.Invalido;
        if (SoloDigitos(limpio))
        {
            return limpio.Length switch
            {
                11 => ValidarCedula(limpio) ? TipoIdentificacion.Cedula : TipoIdentificacion.Invalido,
                9 => ValidarRnc(limpio) ? TipoIdentificacion.Rnc : TipoIdentificacion.Invalido,
                // numerico sin largo de cedula ni RNC: posible identificacion extranjera
                >= 5 and <= 20 => TipoIdentificacion.Pasaporte,
                _ => TipoIdentificacion.Invalido,
            };
        }
        return limpio.All(c => char.IsAsciiDigit(c) || c is >= 'A' and <= 'Z') && limpio.Length is >= 5 and <= 20
            ? TipoIdentificacion.Pasaporte
            : TipoIdentificacion.Invalido;
    }

    /// <summary>Vacio, '0' o solo ceros = sin identificacion (once ceros pasan el digito de cedula).</summary>
    public static bool SinIdentificacion(string? valor) => Limpiar(valor?.Trim()).All(c => c == '0');

    /// <summary>RNC (9) o cedula (11) con su digito verificador: lo que exige el E31.</summary>
    public static bool EsRncOCedulaValido(string? valor) =>
        !SinIdentificacion(valor) && Determinar(valor, out _) is TipoIdentificacion.Rnc or TipoIdentificacion.Cedula;
}
