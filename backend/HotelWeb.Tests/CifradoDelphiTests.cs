using HotelWeb.Core.Seguridad;

namespace HotelWeb.Tests;

/// <summary>
/// La funcion Encriptar de Delphi. Los valores esperados se calcularon con una version aparte (PowerShell) que
/// desencripta bien los 13 usuarios de Seg_Usuario de la base Seguridad (2026-10-22).
/// </summary>
public class CifradoDelphiTests
{
    [Theory]
    [InlineData("abc123", "`J\u00B1\u00D1[\u00E4")]
    [InlineData("Hotel Las Caobas", "XB\u00F9\u00A31\u00B6Q`8\u00B6\u00EA`B\u00BB\u00EDQ")]
    [InlineData("sergio made", "Q=\u00FF\u00ABN\u00F43|9\u00E1\u00A3")]
    [InlineData("`~!@#$%^&*()", "~!yM$o\u00C9q*T\u00F6F")]
    public void Encripta_igual_que_Delphi(string texto, string esperado)
    {
        Assert.Equal(esperado, CifradoDelphi.Encriptar(texto));
        Assert.Equal(texto, CifradoDelphi.Desencriptar(esperado));
    }

    [Fact]
    public void La_Z_mayuscula_cae_fuera_de_las_tablas_como_en_Delphi()
    {
        // 'Z' es el caracter 95 de la tabla 1 y las otras tienen 94: Delphi lee el #0 del final de la cadena
        Assert.Equal("\0\0", CifradoDelphi.Encriptar("ZZ"));
    }

    [Theory]
    [InlineData("clave123", true)]
    [InlineData("con espacio", true)]
    [InlineData("ñandu", false)]
    [InlineData("acción", false)]
    public void Solo_se_cifran_los_caracteres_de_la_tabla(string texto, bool esperado) =>
        Assert.Equal(esperado, CifradoDelphi.EsCifrable(texto));

    [Theory]
    [InlineData("abcdefghijklmnopqrstuvwxyz")]
    [InlineData("0123456789 0123456789")]
    [InlineData("Yudelka Brens")]
    public void Ida_y_vuelta_con_letras_y_numeros(string texto) =>
        Assert.Equal(texto, CifradoDelphi.Desencriptar(CifradoDelphi.Encriptar(texto)));

    // Las tablas 2-5 de Delphi repiten algunos caracteres, asi que unos pocos simbolos (':' y 'N'/'Z' en ciertas
    // posiciones) no se desencriptan de vuelta. Al ingreso no le afecta: Delphi y la web solo encriptan y comparan.
}
