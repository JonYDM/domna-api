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

/// <summary>Estado del apartado/venta.</summary>
public enum EstadoApartado
{
    Activo,
    Liquidado,
    Vencido,
    Cancelado
}

/// <summary>"Compra" = pago completo al momento (venta directa); las otras dos son apartados.</summary>
public enum ModalidadApartado
{
    SinAnticipo,
    Anticipo,
    Compra
}

/// <summary>Estado de la entrega física de la prenda.</summary>
public enum EstadoEntrega
{
    EnOrigen,
    EnTraslado,
    Listo,
    Entregado
}

/// <summary>Forma de pago de un abono.</summary>
public enum MetodoPago
{
    Efectivo,
    Transferencia,
    Tarjeta
}

/// <summary>Canal de la venta/apartado: online (clienta) o físico (POS en mostrador).</summary>
public enum Canal
{
    Online,
    Fisico
}

/// <summary>Qué representa un abono dentro del historial del apartado.</summary>
public enum ConceptoAbono
{
    Anticipo,
    Abono,
    Liquidacion,
    Compra
}


/// <summary>Por qué cambió el stock físico (queda en el historial de movimientos).</summary>
public enum MotivoAjuste
{
    Entrada,
    Conteo,
    Merma,
    Devolucion,
    Traspaso
}
