using HotelWeb.Core.Catalogos;

namespace HotelWeb.Tests;

public class ReglasFotoTests
{
    [Fact]
    public void Reconoce_jpeg_png_y_webp_por_sus_bytes()
    {
        Assert.Equal("image/jpeg", ReglasFoto.TipoDe([0xFF, 0xD8, 0xFF, 0xE0, 0, 0]));
        Assert.Equal("image/png", ReglasFoto.TipoDe([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0]));
        Assert.Equal("image/webp", ReglasFoto.TipoDe("RIFF\0\0\0\0WEBPVP8 "u8));
        Assert.Null(ReglasFoto.TipoDe("<svg xmlns="u8));
        Assert.Null(ReglasFoto.TipoDe("%PDF-1.4"u8));
    }

    [Fact]
    public void Rechaza_vacia_grande_y_lo_que_no_es_foto()
    {
        Assert.NotNull(ReglasFoto.Validar([]));
        Assert.NotNull(ReglasFoto.Validar("hola"u8));
        var grande = new byte[ReglasFoto.MaximoBytes + 1];
        grande[0] = 0xFF; grande[1] = 0xD8; grande[2] = 0xFF;
        Assert.Contains("2 MB", ReglasFoto.Validar(grande));
        Assert.Null(ReglasFoto.Validar([0xFF, 0xD8, 0xFF, 0xDB]));
    }
}
