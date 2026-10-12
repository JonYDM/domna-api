namespace Domna.Application.Dtos;

/// <summary>Alta de una cajera desde el panel de la dueña: nombre, teléfono +52, PIN de 6 dígitos y sucursal.</summary>
public record CrearCajeraInput(string Nombre, string Telefono, string Pin, string Sucursal);

/// <summary>Edición de una cajera: nombre, teléfono y sucursal (sin tocar el PIN).</summary>
public record EditarCajeraInput(string Nombre, string Telefono, string Sucursal);

/// <summary>Restablece el PIN de una cuenta de staff (la cajera lo olvidó).</summary>
public record ResetPinInput(string Pin);
