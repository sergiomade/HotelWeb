using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace HotelWeb.Api.Seguridad;

/// <summary>Token JWT de la sesion (12 horas: un turno de recepcion). Lleva el id y el nombre del usuario.</summary>
public sealed class Tokens
{
    public const string Emisor = "HotelWeb";
    public static readonly TimeSpan Duracion = TimeSpan.FromHours(12);

    public Tokens(string clave) => Clave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(clave));

    public SymmetricSecurityKey Clave { get; }

    public string Crear(Usuario u)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, u.Id.ToString()),
            new("name", u.Nombre),
            new("login", u.Login),
        };
        var token = new JwtSecurityToken(Emisor, Emisor, claims, DateTime.UtcNow, DateTime.UtcNow + Duracion,
            new SigningCredentials(Clave, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// La clave de firma (Jwt:Key). Fuera de desarrollo es obligatoria, de 32 caracteres o mas y distinta de la
    /// plantilla: si no, la API no arranca. En desarrollo, sin clave, se inventa una (las sesiones se pierden al reiniciar).
    /// </summary>
    public static string ClaveConfigurada(IConfiguration config, bool desarrollo)
    {
        var clave = config["Jwt:Key"];
        if (!string.IsNullOrWhiteSpace(clave) && clave.Length >= 32 && !clave.Contains("<CAMBIAR")) return clave;
        if (desarrollo) return Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        throw new InvalidOperationException(
            "Falta Jwt:Key en appsettings.json: ponga una clave aleatoria de 32 caracteres o mas. " +
            "La API no arranca con la clave de la plantilla.");
    }

    public static int IdDe(ClaimsPrincipal u) =>
        int.TryParse(u.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id) ? id : 0;
}
