using HotelWeb.Api.Reportes;

namespace HotelWeb.Tests;

public class ReportesTests
{
    [Theory]
    [InlineData("E310000000239", "E31")]
    [InlineData("E340000000005", "E34")]
    [InlineData("B0200016642", "B02")]
    [InlineData("00000000B0400000101", "B04")]   // NCF viejo de la cafeteria, relleno con ceros
    public void El_tipo_del_comprobante(string ncf, string tipo) => Assert.Equal(tipo, RepositorioReportes.Tipo(ncf));
}
