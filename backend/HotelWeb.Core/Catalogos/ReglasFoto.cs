namespace HotelWeb.Core.Catalogos;

/// <summary>
/// Las fotos de los tipos de habitacion (HotelTipoFoto). El navegador la achica antes de subirla; aqui se revisa que
/// sea de verdad una imagen (por sus primeros bytes, no por lo que diga el navegador) y que no pase del limite.
/// </summary>
public static class ReglasFoto
{
    /// <summary>2 MB: una foto achicada a 1280 px pesa unos 100-200 KB; esto es solo el tope.</summary>
    public const int MaximoBytes = 2 * 1024 * 1024;

    /// <summary>image/jpeg, image/png o image/webp segun la firma del archivo; null si no es ninguna.</summary>
    public static string? TipoDe(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "image/jpeg";
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47
            && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A) return "image/png";
        if (b.Length >= 12 && b[0] == (byte)'R' && b[1] == (byte)'I' && b[2] == (byte)'F' && b[3] == (byte)'F'
            && b[8] == (byte)'W' && b[9] == (byte)'E' && b[10] == (byte)'B' && b[11] == (byte)'P') return "image/webp";
        return null;
    }

    /// <summary>El motivo por el que no se acepta la foto, o null si sirve.</summary>
    public static string? Validar(ReadOnlySpan<byte> b)
    {
        if (b.Length == 0) return "Falta la foto.";
        if (b.Length > MaximoBytes) return "La foto pesa demasiado (hasta 2 MB).";
        return TipoDe(b) is null ? "El archivo no es una foto JPG, PNG o WEBP." : null;
    }
}
