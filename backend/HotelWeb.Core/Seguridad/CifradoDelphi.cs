namespace HotelWeb.Core.Seguridad;

/// <summary>
/// La funcion Encriptar del Delphi del hotel (Hotel\Source\UFrmPrincipal.pas), tal cual: Seg_Usuario guarda asi
/// Cuenta_Login, Clave_Acceso y Nombre. Es una sustitucion por posicion: el caracter i se busca en la tabla 1 y se
/// cambia por el de la misma posicion en la tabla 2, 3, 4 o 5 (segun i). Las tablas se copiaron del .pas
/// (Windows-1252) sin tocarlas: no corregir sus caracteres repetidos, cambiaria las claves guardadas.
/// </summary>
public static class CifradoDelphi
{
    private static readonly string[] Tablas =
    {
        "`1234567890-=\\][poiuytrewqasdfghjkl;'/.,mnb vcxz~!@#$%^&*()_+|}{POIUYTREWQASDFGHJKL:\"?><MNBVCXZ",
        "~!@#$%^&*()_+=-09876c54321`QWEzRTYUIOP{}|\\][poiuytrexwqASDFGvHJKL:\"';lkjhgfdsaZXCVBNM<>?/.,mnb",
        "\\][poiuytrewqZXCVBNM<>?=-0987654321`ASDFGHJKL:\"~!@#$%^&*()_+';lkjhgfdsa/.,mnbvcxzQWERTYUIOP{}|",
        "\u00B5\u00E9\u00E2\u00E4\u00E5\u00E7\u00EA\u00EB\u00E8\u00EF\u00EE\u00EC\u00C4\u00C5\u00C9\u00E6\u00C6\u00F4\u00F6\u00F2\u00FB\u00F9\u00FF\u00D6\u00DC\u00A2\u00A3\u00A5\u00E1\u00ED\u00F3\u00FA\u00F1\u00D1\u00AA\u00BA\u00BF\u00AC\u00BD\u00BC\u00A1\u00AB\u00BB\u00B6\u00DF\u00B1ertyuiopUYTREWQ`!@#+_)(*$%^&[]\\=-0987654321`/.,M",
        "\u00F1\u00D1\u00AA\u00BA\u00BF\u00AC\u00BD\u00BCtyuiopUY098765\u00A2\u00A3\u00A5\u00E1\u00ED\u00F3\u00FA\u00A1\u00AB\u00BB\u00B6\u00DF\u00B1er\u00E8\u00EF\u00EE\u00EC\u00C44321`/.,M\u00DC\u00C5\u00C9\u00E6\u00C6\u00F4\u00F6\u00F2\u00FB\u00F9\u00FF\u00D6#+_)(*$%^&[]\\=-TREWQ`!@\u00B5\u00E9\u00E2\u00E4\u00E5\u00E7\u00EA\u00EB",
    };

    /// <summary>Encriptar(Cadena, 0).</summary>
    public static string Encriptar(string cadena) => Convertir(cadena, encriptar: true);

    /// <summary>Encriptar(Cadena, 1).</summary>
    public static string Desencriptar(string cadena) => Convertir(cadena, encriptar: false);

    /// <summary>
    /// True si el texto solo tiene caracteres de la tabla 1 (letras sin acento, numeros, espacio y simbolos del
    /// teclado). Con otro caracter Delphi leia fuera de la tabla: esa clave no se puede reproducir.
    /// </summary>
    public static bool EsCifrable(string cadena) => cadena.All(c => Tablas[0].Contains(c));

    private static string Convertir(string cadena, bool encriptar)
    {
        var r = new char[cadena.Length];
        for (var i = 1; i <= cadena.Length; i++)
        {
            var x = i > 4 ? i % 4 + 2 : i + 1;   // tabla 2..5 (base 1, como en Delphi)
            var origen = encriptar ? Tablas[0] : Tablas[x - 1];
            var destino = encriptar ? Tablas[x - 1] : Tablas[0];
            var p = origen.IndexOf(cadena[i - 1]);
            // fuera de la tabla Delphi leia basura: se marca con '\0' (no coincide con nada guardado)
            r[i - 1] = p >= 0 && p < destino.Length ? destino[p] : '\0';
        }
        return new string(r);
    }
}
