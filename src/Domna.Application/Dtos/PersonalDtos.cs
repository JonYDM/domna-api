namespace Domna.Application.Dtos;

/// <summary>Alta de una cajera desde el panel de la dueña: nombre, teléfono +52, PIN de 6 dígitos y sucursal.</summary>
public record CrearCajeraInput(string Nombre, string Telefono, string Pin, string Sucursal);
