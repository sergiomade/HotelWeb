using HotelWeb.Core.Seguridad;

namespace HotelWeb.Tests;

public class ReglasSeguridadTests
{
    [Fact]
    public void Los_codigos_de_permiso_no_se_repiten_y_son_del_modulo_40()
    {
        Assert.Equal(PermisosHotel.Todos.Count, PermisosHotel.Todos.Select(p => p.Codigo).Distinct().Count());
        Assert.Equal(PermisosHotel.Todos.Count, PermisosHotel.Todos.Select(p => p.Clave).Distinct().Count());
        Assert.All(PermisosHotel.Todos, p => Assert.InRange(p.Codigo, 4001, 4999));
        Assert.DoesNotContain(PermisosHotel.Todos, p => p.Codigo == PermisosHotel.MarcaGrupo);
        // Seg_Opcion.Descripcion es varchar(50)
        Assert.All(PermisosHotel.Todos, p => Assert.True(p.Nombre.Length <= 50));
    }

    [Fact]
    public void Cambiar_secuencias_se_separo_de_los_comprobantes_especiales_y_lo_hereda()
    {
        Assert.Equal(4015, PermisosHotel.CambiarSecuencias.Codigo);
        Assert.Equal(PermisosHotel.ComprobantesEspeciales.Codigo, PermisosHotel.CambiarSecuencias.Hereda);
        Assert.Contains(PermisosHotel.CambiarSecuencias, PermisosHotel.Todos);
        Assert.Equal(PermisosHotel.CambiarSecuencias, PermisosHotel.PorCodigo(4015));
    }

    [Fact]
    public void Cada_pestana_es_de_un_area_y_la_hereda()
    {
        var areas = PermisosHotel.Areas.ToDictionary(a => a.Clave);
        foreach (var o in PermisosHotel.Todos.Where(p => p.Tipo == TipoPermiso.Opcion))
        {
            Assert.True(areas.ContainsKey(o.Area!), o.Clave);
            Assert.StartsWith(o.Area + ".", o.Clave);
            Assert.Equal(areas[o.Area!].Codigo, o.Hereda);
        }
        Assert.All(PermisosHotel.Areas, a => Assert.Null(a.Hereda));
    }

    [Fact]
    public void Las_cinco_de_catalogos_heredan_de_la_vieja_editar_catalogos()
    {
        Assert.Equal(5, PermisosHotel.EdicionCatalogos.Count);
        Assert.All(PermisosHotel.EdicionCatalogos, p => Assert.Equal(PermisosHotel.EditarCatalogosViejo, p.Hereda));
        Assert.Null(PermisosHotel.PorCodigo(PermisosHotel.EditarCatalogosViejo));
    }

    [Fact]
    public void Cerrar_la_caja_lo_hereda_quien_veia_la_pestana_caja()
    {
        Assert.Equal(PermisosHotel.PorCodigo(PermisosHotel.CerrarCajaHotel.Hereda!.Value)!.Clave, "recepcion.caja");
        Assert.Equal(PermisosHotel.PorCodigo(PermisosHotel.CerrarCajaCafeteria.Hereda!.Value)!.Clave, "cafeteria.caja");
        Assert.Contains(PermisosHotel.CerrarCajaHotel, PermisosHotel.DeRecepcion);
        Assert.Contains(PermisosHotel.CerrarCajaCafeteria, PermisosHotel.DeRecepcion);
        // la pestana va antes que la accion: al sincronizar, la pestana se hereda primero
        var orden = PermisosHotel.Todos.ToList();
        Assert.True(orden.FindIndex(p => p.Clave == "recepcion.caja") < orden.IndexOf(PermisosHotel.CerrarCajaHotel));
    }

    [Fact]
    public void Recepcion_trae_sus_pestanas_pero_no_las_de_catalogos()
    {
        var claves = PermisosHotel.DeRecepcion.Select(p => p.Clave).ToList();
        Assert.Contains("recepcion.checkin", claves);
        Assert.Contains("cafeteria.mesas", claves);
        Assert.DoesNotContain("catalogos", claves);
        Assert.DoesNotContain(claves, c => c.StartsWith("catalogos."));
    }

    [Theory]
    [InlineData("  maria  ", "maria")]
    [InlineData("JPerez2", "JPerez2")]
    public void El_login_se_recorta(string entrada, string esperado) => Assert.Equal(esperado, ReglasSeguridad.Login(entrada));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("con espacio")]
    [InlineData("muñoz")]
    [InlineData("1234567890123456789012345678901")]
    public void Logins_invalidos(string login) => Assert.Throws<ErrorSeguridad>(() => ReglasSeguridad.Login(login));

    [Theory]
    [InlineData("abc")]
    [InlineData("12345678901")]
    [InlineData("niño1")]
    [InlineData("    ")]
    public void Claves_invalidas(string clave) => Assert.Throws<ErrorSeguridad>(() => ReglasSeguridad.Clave(clave));

    [Fact]
    public void Una_clave_valida_se_guarda_tal_cual() => Assert.Equal("Hotel#26", ReglasSeguridad.Clave("Hotel#26"));

    [Fact]
    public void El_nombre_no_lleva_acentos()
    {
        Assert.Equal("Maria Perez", ReglasSeguridad.Nombre(" Maria Perez "));
        Assert.Throws<ErrorSeguridad>(() => ReglasSeguridad.Nombre("María Pérez"));
    }

    [Fact]
    public void El_grupo_va_en_mayusculas() => Assert.Equal("CAJEROS", ReglasSeguridad.Grupo(" cajeros "));

    [Fact]
    public void Un_permiso_que_no_existe_no_pasa()
    {
        Assert.Throws<ErrorSeguridad>(() => ReglasSeguridad.Permisos(new[] { 4001, 9999 }));
        Assert.Equal(new[] { 4001, 4155 }, ReglasSeguridad.Permisos(new[] { 4001, 4155, 4001 }));
        Assert.Throws<ErrorSeguridad>(() => ReglasSeguridad.Permisos(new[] { PermisosHotel.EditarCatalogosViejo }));
    }

    [Theory]
    [InlineData("sergio")]
    [InlineData("Yudelka Brens")]
    [InlineData("RECEPCION 2")]
    public void Logins_y_nombres_comunes_vuelven_iguales_al_desencriptar(string texto) =>
        Assert.True(ReglasSeguridad.Reversible(texto));
}
