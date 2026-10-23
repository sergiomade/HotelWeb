namespace HotelWeb.Core.Cafeteria;

/// <summary>Una linea de la cuenta abierta de una mesa (FaFacturaDetalle).</summary>
/// <param name="Tragos">1 = por unidad (botella, plato), 2 = por fraccion (trago), como Delphi.</param>
/// <param name="Impreso">Ya salio en una pre-cuenta: no cambia de cantidad ni de precio.</param>
public sealed record LineaMesa(int Secuencia, string Referencia, string Descripcion, decimal Cantidad, decimal Precio, decimal Importe,
    int Tragos, bool Impreso);

public sealed record TotalesMesa(decimal SubTotal, decimal Itbis, decimal Propina, decimal Total);

/// <summary>El precio de un articulo para un cliente (Stp_FacturarUnArticulo): por unidad y por fraccion.</summary>
/// <param name="Fraccion">Cuantas fracciones (tragos) tiene la unidad; 1 = no se vende por fraccion.</param>
public sealed record PrecioArticulo(string Referencia, string Descripcion, decimal Precio, decimal Precio2, decimal Costo, int Fraccion);

/// <summary>Como queda una linea nueva o cambiada: precio, tragos y costo.</summary>
public sealed record PrecioLinea(decimal Precio, int Tragos, decimal Costo);

/// <summary>
/// Reglas de la cuenta abierta de la cafeteria (etapa 8a), como Ufrmfactura de Delphi (RestaurantHotel):
/// importe = cantidad x precio; ITBIS y propina sobre el subtotal segun el cliente (CxcCliente.Itbis / Propina),
/// TRUNCADOS a 2 decimales como Delphi (2236 de 2236 facturas de 2026); sin descuento (decision 2026-10-23).
/// </summary>
public static class ReglasCafeteria
{
    /// <summary>Articulo especial de Delphi (SBtnNuevoClick): referencia y articulo segun sea comida o bebida.</summary>
    public const string ReferenciaAlimento = "00";
    public const string ReferenciaBebida = "**";
    public const int ArticuloAlimento = 8;
    public const int ArticuloBebida = 7;

    /// <summary>FaFacturaDetalle.Descripcion es varchar(25): Delphi copia los primeros 25.</summary>
    public const int LargoDescripcion = 25;

    public const int CantidadMaxima = 999;

    public static decimal Importe(decimal cantidad, decimal precio) => Math.Round(cantidad * precio, 2, MidpointRounding.AwayFromZero);

    public static decimal Truncar(decimal v) => Math.Truncate(v * 100m) / 100m;

    /// <summary>
    /// Como CalcularTotales de Delphi: ITBIS y propina se graban truncados, pero el total sale de los montos SIN truncar
    /// (Currency de 4 decimales) y se trunca al final. Asi 609.38 da 109.68 + 60.93 y total 780.00, no 779.99: los
    /// precios del menu estan puestos para que el total sea redondo (factura 12790, E310000000233 aceptada).
    /// </summary>
    public static TotalesMesa Totales(IEnumerable<LineaMesa> lineas, bool conItbis, bool conPropina, decimal porcItbis, decimal porcPropina)
    {
        var sub = lineas.Sum(l => l.Importe);
        var itbis = conItbis ? sub * porcItbis : 0m;
        var propina = conPropina ? sub * porcPropina : 0m;
        return new TotalesMesa(sub, Truncar(itbis), Truncar(propina), Truncar(sub + itbis + propina));
    }

    /// <summary>
    /// El precio con que entra el articulo, como DbeReferenciaExit: si la unidad tiene fracciones (Fraccion > 1) entra
    /// por fraccion (Precio2, tragos 2, costo / fraccion); si no, por unidad. <paramref name="porFraccion"/> lo cambia
    /// (los "Cambiar a fraccion / unidad" de Delphi). Null si ese precio no existe para el cliente.
    /// </summary>
    public static PrecioLinea? Precio(PrecioArticulo a, bool? porFraccion = null)
    {
        var fraccion = porFraccion ?? a.Fraccion > 1;
        if (fraccion)
        {
            if (a.Fraccion <= 1 || a.Precio2 <= 0) return null;
            return new PrecioLinea(a.Precio2, 2, Math.Round(a.Costo / a.Fraccion, 2, MidpointRounding.AwayFromZero));
        }
        return a.Precio <= 0 ? null : new PrecioLinea(a.Precio, 1, a.Costo);
    }

    public static string? ValidarCantidad(decimal cantidad) =>
        cantidad < 1 ? "La cantidad debe ser 1 o más."
        : cantidad > CantidadMaxima ? $"La cantidad no puede pasar de {CantidadMaxima}."
        : cantidad != Math.Truncate(cantidad) ? "La cantidad va en unidades enteras."
        : null;

    /// <summary>El e-CF no acepta precio unitario menor de 1.00 (lo revisa el armador de Delphi al cobrar).</summary>
    public static string? ValidarPrecio(decimal precio) =>
        precio < 1 ? "El precio debe ser de 1.00 o más." : precio > 999_999 ? "El precio es demasiado alto." : null;

    public static string? ValidarEspecial(string? descripcion, decimal precio) =>
        string.IsNullOrWhiteSpace(descripcion) ? "Escriba la descripción del artículo."
        : descripcion.Trim().Length < 3 ? "La descripción es muy corta."
        : ValidarPrecio(precio);

    public static string Descripcion(string d)
    {
        var t = d.Trim();
        return t.Length > LargoDescripcion ? t[..LargoDescripcion] : t;
    }

    /// <summary>Lo impreso en la pre-cuenta queda fijo (como Delphi: ValidarImpreso).</summary>
    public static string? PuedeCambiar(LineaMesa l) =>
        l.Impreso ? $"\"{l.Descripcion}\" ya salió en la pre-cuenta: no se cambia." : null;

    /// <summary>
    /// Borrar una linea. Lo impreso no se borra (decision 2026-10-23: lo impreso queda fijo; Delphi lo dejaba borrar).
    /// La ultima linea si se borra cuando nada se ha impreso: la mesa queda libre.
    /// </summary>
    public static string? PuedeBorrar(LineaMesa l) =>
        l.Impreso ? $"\"{l.Descripcion}\" ya salió en la pre-cuenta: no se borra." : null;

    /// <summary>Para cobrar hace falta la pre-cuenta de TODO lo que hay (Delphi: "FACTURA NO ESTA IMPRESA").</summary>
    public static bool PreCuentaAlDia(IReadOnlyCollection<LineaMesa> lineas) => lineas.Count > 0 && lineas.All(l => l.Impreso);

    /// <summary>Unir dos cuentas (mover a una mesa ocupada): las lineas de la que se mueve van al final de la otra.</summary>
    public static IReadOnlyList<LineaMesa> Unir(IReadOnlyList<LineaMesa> destino, IReadOnlyList<LineaMesa> origen)
    {
        var ultima = destino.Count == 0 ? 0 : destino.Max(l => l.Secuencia);
        return destino.Concat(origen.OrderBy(l => l.Secuencia).Select((l, i) => l with { Secuencia = ultima + i + 1 })).ToList();
    }
}
