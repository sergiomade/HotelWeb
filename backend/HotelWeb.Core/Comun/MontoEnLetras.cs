namespace HotelWeb.Core.Comun;

/// <summary>
/// Un monto en letras, para los recibos ("La suma de"), como MonToLit de Delphi (Libgen.pas) pero sin sus tropiezos
/// ("Un Millones", el "Mil" de mas): "Mil doscientos pesos con 50/100".
/// </summary>
public static class MontoEnLetras
{
    private static readonly string[] Unidades =
    [
        "", "uno", "dos", "tres", "cuatro", "cinco", "seis", "siete", "ocho", "nueve", "diez", "once", "doce", "trece", "catorce",
        "quince", "dieciséis", "diecisiete", "dieciocho", "diecinueve", "veinte", "veintiuno", "veintidós", "veintitrés",
        "veinticuatro", "veinticinco", "veintiséis", "veintisiete", "veintiocho", "veintinueve",
    ];

    private static readonly string[] Decenas = ["", "", "", "treinta", "cuarenta", "cincuenta", "sesenta", "setenta", "ochenta", "noventa"];

    private static readonly string[] Centenas =
        ["", "ciento", "doscientos", "trescientos", "cuatrocientos", "quinientos", "seiscientos", "setecientos", "ochocientos", "novecientos"];

    /// <summary>"Mil doscientos pesos con 50/100". Negativos: el valor absoluto (un recibo de devolucion dice la suma).</summary>
    public static string Pesos(decimal monto)
    {
        monto = Math.Abs(Math.Round(monto, 2, MidpointRounding.AwayFromZero));
        var enteros = (long)Math.Truncate(monto);
        var centavos = (int)Math.Round((monto - enteros) * 100);
        var texto = enteros == 0 ? "cero" : Numero(enteros, apocopado: true);
        var moneda = enteros == 1 ? "peso" : (enteros > 0 && enteros % 1_000_000 == 0 ? "de pesos" : "pesos");
        var r = $"{texto} {moneda} con {centavos:00}/100";
        return char.ToUpperInvariant(r[0]) + r[1..];
    }

    /// <summary>El numero en letras. Apocopado: "un" en vez de "uno" al final (antes de "pesos").</summary>
    public static string Numero(long n, bool apocopado = false)
    {
        if (n < 0) throw new ArgumentOutOfRangeException(nameof(n));
        if (n == 0) return "cero";
        var partes = new List<string>();
        var millones = n / 1_000_000;
        var miles = n / 1_000 % 1_000;
        var resto = n % 1_000;
        if (millones > 0)
            partes.Add(millones == 1 ? "un millón" : $"{Numero(millones, apocopado: true)} millones");
        if (miles > 0)
            partes.Add(miles == 1 ? "mil" : $"{HastaMil((int)miles, apocopado: true)} mil");
        if (resto > 0)
            partes.Add(HastaMil((int)resto, apocopado));
        return string.Join(" ", partes);
    }

    private static string HastaMil(int n, bool apocopado)
    {
        if (n == 100) return "cien";
        var c = n / 100;
        var d = n % 100;
        var texto = d switch
        {
            0 => "",
            1 when apocopado => "un",
            21 when apocopado => "veintiún",
            < 30 => Unidades[d],
            _ => d % 10 == 0 ? Decenas[d / 10] : $"{Decenas[d / 10]} y {(d % 10 == 1 && apocopado ? "un" : Unidades[d % 10])}",
        };
        return c == 0 ? texto : texto == "" ? Centenas[c] : $"{Centenas[c]} {texto}";
    }
}
