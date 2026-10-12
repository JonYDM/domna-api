namespace Domna.Domain;

/// <summary>
/// Renglón del historial de inventario: cuánto cambió el stock físico de una variante en una
/// sucursal y por qué. Es un registro de auditoría: nunca se edita ni se borra.
/// </summary>
public class MovimientoInventario
{
    public string Id { get; private set; } = default!;
    public DateTime Fecha { get; private set; }
    public string ProductoId { get; private set; } = default!;
    public string VarianteId { get; private set; } = default!;
    public SucursalId Sucursal { get; private set; }

    /// <summary>Positivo = entra stock, negativo = sale.</summary>
    public int Delta { get; private set; }
    public MotivoAjuste Motivo { get; private set; }

    private MovimientoInventario() { }

    public MovimientoInventario(
        string id, DateTime fecha, string productoId, string varianteId,
        SucursalId sucursal, int delta, MotivoAjuste motivo)
    {
        Id = id;
        Fecha = fecha;
        ProductoId = productoId;
        VarianteId = varianteId;
        Sucursal = sucursal;
        Delta = delta;
        Motivo = motivo;
    }
}
