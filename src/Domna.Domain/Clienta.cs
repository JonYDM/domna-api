namespace Domna.Domain;

/// <summary>
/// Clienta de la boutique. El teléfono (normalizado) es la llave de acceso y es único por boutique;
/// el correo es obligatorio (canal de recuperación del PIN). El hash del PIN vive aquí, nunca el PIN
/// en claro. Alineada con la interfaz Clienta de src/types/api.ts.
/// </summary>
public class Clienta
{
    public string Id { get; private set; } = default!;
    public string Nombre { get; private set; } = default!;
    public string Apellidos { get; private set; } = default!;
    public string Telefono { get; private set; } = default!;
    public string Email { get; private set; } = default!;
    public OrigenClienta Origen { get; private set; }
    public DateTime Creada { get; private set; }
    public DateTime? UltimoAcceso { get; private set; }
    public decimal PenalizacionPendiente { get; private set; }

    /// <summary>Rol de la cuenta: clienta (default), dueña o cajera.</summary>
    public Rol Rol { get; private set; } = Rol.Clienta;

    /// <summary>Sucursal donde opera la cajera (su corte del día). Null para clienta/dueña.</summary>
    public SucursalId? SucursalCajera { get; private set; }

    /// <summary>Hash BCrypt del PIN. Null cuando la clienta fue dada de alta en mostrador sin acceso.</summary>
    public string? PinHash { get; private set; }

    /// <summary>
    /// ¿La cuenta puede iniciar sesión? Para staff: la dueña desactiva el acceso de una cajera sin
    /// borrar su historial de ventas. Las clientas siempre nacen activas.
    /// </summary>
    public bool Activo { get; private set; } = true;

    private Clienta() { }

    /// <summary>
    /// Crea una clienta aplicando las reglas de dominio (normaliza y valida nombre, teléfono y correo).
    /// El PIN se recibe ya hasheado (el hashing es infraestructura).
    /// </summary>
    public static Clienta Crear(
        string id,
        string nombre,
        string? apellidos,
        string telefono,
        string email,
        OrigenClienta origen,
        DateTime ahora,
        string? pinHash = null)
    {
        var nombreNorm = Normalizacion.NormalizarNombre(nombre);
        if (nombreNorm.Length < 2)
            throw DomnaException.BadRequest("Escribe el nombre.");

        return new Clienta
        {
            Id = id,
            Nombre = nombreNorm,
            Apellidos = Normalizacion.NormalizarNombre(apellidos),
            Telefono = Normalizacion.NormalizarTelefono(telefono),
            Email = Normalizacion.NormalizarEmail(email),
            Origen = origen,
            Creada = ahora,
            UltimoAcceso = origen == OrigenClienta.Registro ? ahora : null,
            PenalizacionPendiente = 0m,
            PinHash = pinHash,
            Rol = Rol.Clienta,
        };
    }

    /// <summary>
    /// Crea una cuenta de staff (dueña o cajera). Lo usa el seed y, a futuro, el panel SuperAdmin
    /// (dueña) y la dueña (cajera). Siempre nace con acceso (PIN) y rol de staff.
    /// </summary>
    public static Clienta CrearStaff(
        string id,
        string nombre,
        string? apellidos,
        string telefono,
        string email,
        Rol rol,
        string pinHash,
        DateTime ahora,
        SucursalId? sucursalCajera = null)
    {
        if (rol == Rol.Clienta)
            throw DomnaException.BadRequest("CrearStaff es solo para dueña o cajera.");
        var cuenta = Crear(id, nombre, apellidos, telefono, email, OrigenClienta.Registro, ahora, pinHash);
        cuenta.Rol = rol;
        cuenta.SucursalCajera = rol == Rol.Cajera ? sucursalCajera : null;
        return cuenta;
    }

    /// <summary>¿Tiene acceso (PIN establecido)?</summary>
    public bool TieneAcceso => !string.IsNullOrEmpty(PinHash);

    public void FijarPinHash(string pinHash) => PinHash = pinHash;

    public void RegistrarAcceso(DateTime ahora) => UltimoAcceso = ahora;

    /// <summary>
    /// Edita los datos de una cuenta de staff (nombre, teléfono y, si es cajera, su sucursal).
    /// Lo usa la dueña desde el panel de Personal.
    /// </summary>
    public void EditarStaff(string nombre, string telefono, SucursalId? sucursalCajera)
    {
        if (Rol == Rol.Clienta)
            throw DomnaException.BadRequest("EditarStaff es solo para dueña o cajera.");
        var nombreNorm = Normalizacion.NormalizarNombre(nombre);
        if (nombreNorm.Length < 2)
            throw DomnaException.BadRequest("Escribe el nombre.");
        Nombre = nombreNorm;
        Telefono = Normalizacion.NormalizarTelefono(telefono);
        if (Rol == Rol.Cajera)
            SucursalCajera = sucursalCajera;
    }

    /// <summary>Bloquea el acceso de una cuenta de staff (no puede iniciar sesión) sin borrar su historial.</summary>
    public void Desactivar()
    {
        if (Rol == Rol.Clienta)
            throw DomnaException.BadRequest("Desactivar es solo para dueña o cajera.");
        Activo = false;
    }

    /// <summary>Restaura el acceso de una cuenta de staff previamente desactivada.</summary>
    public void Activar()
    {
        if (Rol == Rol.Clienta)
            throw DomnaException.BadRequest("Activar es solo para dueña o cajera.");
        Activo = true;
    }

    /// <summary>
    /// Reclama una ficha de mostrador sin acceso: conserva el id/historial y fija nombre,
    /// apellidos, correo, PIN y la marca como registro propio.
    /// </summary>
    public void ReclamarComoRegistro(string nombre, string? apellidos, string email, string pinHash, DateTime ahora)
    {
        var nombreNorm = Normalizacion.NormalizarNombre(nombre);
        if (nombreNorm.Length < 2)
            throw DomnaException.BadRequest("Escribe tu nombre.");
        Nombre = nombreNorm;
        Apellidos = Normalizacion.NormalizarNombre(apellidos);
        Email = Normalizacion.NormalizarEmail(email);
        Origen = OrigenClienta.Registro;
        PinHash = pinHash;
        UltimoAcceso = ahora;
    }
}
