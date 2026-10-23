using HotelWeb.Core.Comun;

namespace HotelWeb.Tests;

public class MontoEnLetrasTests
{
    [Theory]
    [InlineData(0, "Cero pesos con 00/100")]
    [InlineData(1, "Un peso con 00/100")]
    [InlineData(21, "Veintiún pesos con 00/100")]
    [InlineData(31, "Treinta y un pesos con 00/100")]
    [InlineData(100, "Cien pesos con 00/100")]
    [InlineData(101, "Ciento un pesos con 00/100")]
    [InlineData(199.99, "Ciento noventa y nueve pesos con 99/100")]
    [InlineData(1000, "Mil pesos con 00/100")]
    [InlineData(1200.50, "Mil doscientos pesos con 50/100")]
    [InlineData(3500, "Tres mil quinientos pesos con 00/100")]
    [InlineData(21000, "Veintiún mil pesos con 00/100")]
    [InlineData(25900, "Veinticinco mil novecientos pesos con 00/100")]
    [InlineData(37334, "Treinta y siete mil trescientos treinta y cuatro pesos con 00/100")]
    [InlineData(100000, "Cien mil pesos con 00/100")]
    [InlineData(1000000, "Un millón de pesos con 00/100")]
    [InlineData(2500000, "Dos millones quinientos mil pesos con 00/100")]
    [InlineData(3000000, "Tres millones de pesos con 00/100")]
    [InlineData(-3933.88, "Tres mil novecientos treinta y tres pesos con 88/100")]
    public void En_letras(decimal monto, string texto) => Assert.Equal(texto, MontoEnLetras.Pesos(monto));
}
