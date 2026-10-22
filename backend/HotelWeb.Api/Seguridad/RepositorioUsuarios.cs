using System.Data;
using Dapper;
using HotelWeb.Api.Datos;
using HotelWeb.Core.Seguridad;

namespace HotelWeb.Api.Seguridad;

/// <summary>Usuario de Delphi (Seg_Usuario), con el nombre ya desencriptado.</summary>
public sealed record Usuario(int Id, string Login, string Nombre, bool Supervisor, bool CancelaFactura);

/// <summary>
/// Ingreso con los mismos usuarios y claves de Delphi (UFrmLogin): se busca en Seg_Usuario el login y la clave
/// ENCRIPTADOS, igual que hace Delphi. La web no guarda ni cambia claves (eso sigue en Delphi por ahora).
/// </summary>
public sealed class RepositorioUsuarios
{
    private readonly Conexiones _conexiones;

    public RepositorioUsuarios(Conexiones conexiones) => _conexiones = conexiones;

    private sealed record Fila(decimal Usuario_Id, string? Cuenta_Login, string? Nombre, bool? Estado,
        bool? Supervisor, bool? CancelaFactura);

    public Usuario? Validar(string login, string clave, out string motivo)
    {
        motivo = "Usuario o clave de acceso invalida.";
        login ??= "";
        clave ??= "";
        if (login.Trim().Length == 0 || clave.Trim().Length == 0) return null;
        // Clave_Acceso es varchar(10) y Cuenta_Login varchar(30) en Seg_Usuario
        if (clave.Length > 10 || login.Length > 30) return null;
        if (!CifradoDelphi.EsCifrable(login) || !CifradoDelphi.EsCifrable(clave)) return null;

        using var cn = _conexiones.Seguridad();
        var f = cn.QueryFirstOrDefault<Fila>(
            "SELECT Usuario_Id, Cuenta_Login, Nombre, Estado, Supervisor, CancelaFactura " +
            "FROM Seg_Usuario WHERE Cuenta_Login = @login AND Clave_Acceso = @clave",
            new
            {
                // varchar, como las columnas: los caracteres del cifrado son de Windows-1252
                login = new DbString { Value = CifradoDelphi.Encriptar(login), IsAnsi = true, Length = 30 },
                clave = new DbString { Value = CifradoDelphi.Encriptar(clave), IsAnsi = true, Length = 10 },
            });
        if (f is null) return null;
        if (f.Estado != true)
        {
            motivo = "El usuario esta inactivo.";
            return null;
        }
        return De(f);
    }

    public Usuario? Obtener(int id)
    {
        using var cn = _conexiones.Seguridad();
        var f = cn.QueryFirstOrDefault<Fila>(
            "SELECT Usuario_Id, Cuenta_Login, Nombre, Estado, Supervisor, CancelaFactura FROM Seg_Usuario WHERE Usuario_Id = @id",
            new { id });
        return f is { Estado: true } ? De(f) : null;
    }

    private static Usuario De(Fila f) => new(
        (int)f.Usuario_Id,
        CifradoDelphi.Desencriptar(f.Cuenta_Login ?? "").Trim(),
        CifradoDelphi.Desencriptar(f.Nombre ?? "").Trim(),
        f.Supervisor == true,
        f.CancelaFactura == true);
}
