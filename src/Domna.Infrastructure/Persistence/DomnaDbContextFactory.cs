using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Domna.Infrastructure.Persistence;

/// <summary>
/// Permite a las herramientas de EF (`dotnet ef migrations`) crear el DbContext en tiempo de diseño
/// apuntando a PostgreSQL, sin arrancar la aplicación. La cadena real no importa aquí: las migraciones
/// se generan del modelo, no de una conexión viva.
/// </summary>
public class DomnaDbContextFactory : IDesignTimeDbContextFactory<DomnaDbContext>
{
    public DomnaDbContext CreateDbContext(string[] args)
    {
        var opciones = new DbContextOptionsBuilder<DomnaDbContext>()
            .UseNpgsql("Host=localhost;Database=domna;Username=postgres;Password=postgres")
            .Options;
        return new DomnaDbContext(opciones);
    }
}
