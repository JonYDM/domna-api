using System.Linq.Expressions;

namespace Domna.Domain;

/// <summary>
/// Qué pedidos incluye cada filtro de la lista de la dueña y la cajera. Son expresiones (no métodos) para que
/// la base de datos los resuelva con WHERE en lugar de traer todos los pedidos y filtrarlos en memoria. El
/// repositorio real y el de pruebas usan exactamente las mismas reglas.
/// </summary>
public static class FiltroPedidos
{
    /// <summary>Un apartado activo está "por vencer" cuando le quedan 3 días o menos (o ya pasó la fecha).</summary>
    public const int DiasPorVencer = 3;

    public const int TextoMaximo = 80;

    /// <summary>"compras" = ventas directas; "apartados" = todo lo demás; cualquier otro valor = todos.</summary>
    public static Expression<Func<Apartado, bool>> PorTipo(string? tipo) => tipo switch
    {
        "compras" => a => a.Modalidad == ModalidadApartado.Compra,
        "apartados" => a => a.Modalidad != ModalidadApartado.Compra,
        _ => a => true,
    };

    /// <summary>
    /// Estados simples (activo, liquidado, vencido, cancelado) y los derivados: por_vencer, por_entregar
    /// (pagado y sin entregar) y entregado. Un estado que no existe no devuelve nada. Sin estado, todos.
    /// </summary>
    public static Expression<Func<Apartado, bool>> PorEstado(string? estado, DateTime ahora)
    {
        // Se compara contra una fecha límite ya calculada: VenceEl.Date - hoy.Date <= 3 días equivale a
        // VenceEl < (hoy + 4 días) a medianoche, y así se puede traducir a SQL.
        var limite = ahora.Date.AddDays(DiasPorVencer + 1);
        return (estado ?? "").Trim().ToLowerInvariant() switch
        {
            "" => a => true,
            "activo" => a => a.Estado == EstadoApartado.Activo,
            "liquidado" => a => a.Estado == EstadoApartado.Liquidado,
            "vencido" => a => a.Estado == EstadoApartado.Vencido,
            "cancelado" => a => a.Estado == EstadoApartado.Cancelado,
            "por_vencer" => a => a.Estado == EstadoApartado.Activo && a.VenceEl < limite,
            "por_entregar" => a => a.Estado == EstadoApartado.Liquidado && a.EstadoEntrega != EstadoEntrega.Entregado,
            "entregado" => a => a.Estado == EstadoApartado.Liquidado && a.EstadoEntrega == EstadoEntrega.Entregado,
            _ => a => false,
        };
    }

    /// <summary>Busca en folio, nombre y teléfono de la clienta y nombre de las prendas, sin distinguir mayúsculas.</summary>
    public static Expression<Func<Apartado, bool>> PorTexto(string? texto)
    {
        var t = (texto ?? "").Trim().ToLowerInvariant();
        if (t.Length == 0) return a => true;
        if (t.Length > TextoMaximo) t = t[..TextoMaximo];

        // Los teléfonos se guardan como "777 123 4567". Si lo escrito parece un teléfono (solo dígitos,
        // espacios, + o -), se compara sin espacios para que "7771234567" también lo encuentre.
        var digitos = new string(t.Where(char.IsDigit).ToArray());
        var pareceTelefono = digitos.Length >= 3 && t.All(c => char.IsDigit(c) || c is ' ' or '+' or '-');
        if (pareceTelefono)
            return a => a.ClientaTelefono.Replace(" ", "").Contains(digitos)
                || a.Folio.ToLower().Contains(t)
                || a.Lineas.Any(l => l.Nombre.ToLower().Contains(t));

        return a => a.Folio.ToLower().Contains(t)
            || a.ClientaNombre.ToLower().Contains(t)
            || a.Lineas.Any(l => l.Nombre.ToLower().Contains(t));
    }

    /// <summary>Las tres condiciones juntas (Y).</summary>
    public static Expression<Func<Apartado, bool>> Combinar(string? tipo, string? estado, string? texto, DateTime ahora)
    {
        var condiciones = new[] { PorTipo(tipo), PorEstado(estado, ahora), PorTexto(texto) };
        var parametro = Expression.Parameter(typeof(Apartado), "a");
        Expression cuerpo = Expression.Constant(true);
        foreach (var c in condiciones)
        {
            var interno = new CambiaParametro(c.Parameters[0], parametro).Visit(c.Body)!;
            cuerpo = Expression.AndAlso(cuerpo, interno);
        }
        return Expression.Lambda<Func<Apartado, bool>>(cuerpo, parametro);
    }

    private sealed class CambiaParametro : ExpressionVisitor
    {
        private readonly ParameterExpression _viejo;
        private readonly ParameterExpression _nuevo;

        public CambiaParametro(ParameterExpression viejo, ParameterExpression nuevo)
        {
            _viejo = viejo;
            _nuevo = nuevo;
        }

        protected override Expression VisitParameter(ParameterExpression nodo) => nodo == _viejo ? _nuevo : base.VisitParameter(nodo);
    }
}