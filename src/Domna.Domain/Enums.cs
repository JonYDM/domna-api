namespace Domna.Domain;

/// <summary>Sucursales de la boutique. Coincide con SucursalId del front ("temixco" | "azteca").</summary>
public enum SucursalId
{
    Temixco,
    Azteca
}

/// <summary>Rol del sistema (clienta | duena | cajera).</summary>
public enum Rol
{
    Clienta,
    Duena,
    Cajera
}

/// <summary>Cómo se registró la clienta (registro propio o alta en mostrador).</summary>
public enum OrigenClienta
{
    Registro,
    Mostrador
}

/// <summary>Silueta de la prenda (para dibujar la imagen y proponer tallas).</summary>
public enum Silueta
{
    Vestido,
    Blusa,
    Pantalon,
    Falda,
    Chamarra,
    Bolsa,
    Conjunto
}

/// <summary>Familia de color para el filtro del catálogo.</summary>
public enum FamiliaColor
{
    Negro,
    Blanco,
    Beige,
    Cafe,
    Gris,
    Rojo,
    Rosa,
    Azul,
    Verde
}
