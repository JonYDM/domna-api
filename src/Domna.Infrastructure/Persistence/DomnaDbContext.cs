using Domna.Domain;
using Microsoft.EntityFrameworkCore;

namespace Domna.Infrastructure.Persistence;

/// <summary>
/// Contexto EF Core. Hoy corre InMemory (demo); al pasar a PostgreSQL solo cambia el provider en
/// la DI, el modelo se conserva. Las entidades exponen setters privados, así que se configura el
/// acceso por campo para que EF pueda materializarlas.
/// </summary>
public class DomnaDbContext : DbContext
{
    public DomnaDbContext(DbContextOptions<DomnaDbContext> options) : base(options) { }

    public DbSet<Clienta> Clientas => Set<Clienta>();
    public DbSet<RecuperacionPin> Recuperaciones => Set<RecuperacionPin>();
    public DbSet<Producto> Productos => Set<Producto>();
    public DbSet<ColorProducto> Colores => Set<ColorProducto>();
    public DbSet<Variante> Variantes => Set<Variante>();
    public DbSet<Categoria> Categorias => Set<Categoria>();
    public DbSet<ColorCatalogo> ColoresCatalogo => Set<ColorCatalogo>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Clienta>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Telefono).IsUnique();
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Origen).HasConversion<string>();
            e.Property(x => x.Rol).HasConversion<string>();
            e.Property(x => x.SucursalCajera).HasConversion<string>();
            e.Property(x => x.PenalizacionPendiente).HasPrecision(12, 2);
        });

        b.Entity<RecuperacionPin>(e =>
        {
            e.HasKey(x => x.Token);
        });

        b.Entity<Producto>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Silueta).HasConversion<string>();
            e.Property(x => x.Precio).HasPrecision(12, 2);
            e.Property(x => x.Costo).HasPrecision(12, 2);
            e.Property(x => x.PrecioAntes).HasPrecision(12, 2);
            // Colecciones del agregado: EF usa el campo de respaldo (lista privada).
            e.HasMany(x => x.Colores).WithOne().HasForeignKey(c => c.ProductoId);
            e.HasMany(x => x.Variantes).WithOne().HasForeignKey(v => v.ProductoId);
            e.Navigation(x => x.Colores).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.Navigation(x => x.Variantes).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        b.Entity<ColorProducto>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Familia).HasConversion<string>();
        });

        b.Entity<Variante>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.CodigoBarras);
        });

        b.Entity<Categoria>(e =>
        {
            e.HasKey(x => x.Id);
            // Lista de tallas: colección primitiva mapeada al campo de respaldo mutable (_tallas),
            // porque la propiedad pública Tallas es IReadOnlyList y EF no la admite como primitiva.
            e.PrimitiveCollection<List<string>>("_tallas")
                .UsePropertyAccessMode(PropertyAccessMode.Field);
            e.Ignore(x => x.Tallas);
        });

        b.Entity<ColorCatalogo>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Familia).HasConversion<string>();
        });
    }
}
