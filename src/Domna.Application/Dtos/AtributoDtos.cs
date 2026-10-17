namespace Domna.Application.Dtos;

/// <summary>Categoría con su juego de tallas y cuántas prendas la usan (para saber si se puede borrar).</summary>
public record CategoriaDto(string Id, string Nombre, List<string> Tallas, int Productos);

/// <summary>Alta/edición de categoría.</summary>
public record CategoriaInput(string Nombre, List<string> Tallas);

/// <summary>Color del catálogo de la boutique (familia calculada del hex).</summary>
public record ColorCatalogoDto(string Id, string Nombre, string Hex, string Familia);

/// <summary>Alta/edición de color (la familia la calcula el servidor).</summary>
public record ColorInput(string Nombre, string Hex);

/// <summary>Resultado de editar un color: lo mismo que <see cref="ColorCatalogoDto"/> más cuántas prendas se actualizaron.</summary>
public record ColorEditadoDto(string Id, string Nombre, string Hex, string Familia, int PrendasActualizadas);

/// <summary>En cuántas prendas se usa un color del catálogo (se reconoce por nombre).</summary>
public record UsoColorDto(int Prendas);
