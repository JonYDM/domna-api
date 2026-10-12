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
    public DbSet<ImagenProducto> Imagenes => Set<ImagenProducto>();
    public DbSet<Categoria> Categorias => Set<Categoria>();
    public DbSet<ColorCatalogo> ColoresCatalogo => Set<ColorCatalogo>();
    public DbSet<Apartado> Apartados => Set<Apartado>();
    public DbSet<LineaApartado> LineasApartado => Set<LineaApartado>();
    public DbSet<Abono> Abonos => Set<Abono>();
    public DbSet<ConfigBoutique> Config => Set<ConfigBoutique>();
    public DbSet<AvisoLeido> AvisosLeidos => Set<AvisoLeido>();
    public DbSet<MovimientoInventario> Movimientos => Set<MovimientoInventario>();

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
            e.HasMany(x => x.Imagenes).WithOne().HasForeignKey(i => i.ProductoId);
            e.Navigation(x => x.Colores).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.Navigation(x => x.Variantes).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.Navigation(x => x.Imagenes).UsePropertyAccessMode(PropertyAccessMode.Field);
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

        b.Entity<ImagenProducto>(e =>
        {
            e.HasKey(x => x.Id);
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

        b.Entity<Apartado>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Folio).IsUnique();
            e.Property(x => x.Subtotal).HasPrecision(12, 2);
            e.Property(x => x.Penalizacion).HasPrecision(12, 2);
            e.Property(x => x.Total).HasPrecision(12, 2);
            e.Property(x => x.Modalidad).HasConversion<string>();
            e.Property(x => x.Canal).HasConversion<string>();
            e.Property(x => x.Entrega).HasConversion<string>();
            e.Property(x => x.EstadoEntrega).HasConversion<string>();
            e.Property(x => x.Estado).HasConversion<string>();
            e.HasMany(x => x.Lineas).WithOne().HasForeignKey(l => l.ApartadoId);
            e.HasMany(x => x.Abonos).WithOne().HasForeignKey(a => a.ApartadoId);
            e.Navigation(x => x.Lineas).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.Navigation(x => x.Abonos).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        b.Entity<LineaApartado>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Silueta).HasConversion<string>();
            e.Property(x => x.SucursalOrigen).HasConversion<string>();
            e.Property(x => x.Precio).HasPrecision(12, 2);
            e.Property(x => x.Costo).HasPrecision(12, 2);
        });

        b.Entity<Abono>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Monto).HasPrecision(12, 2);
            e.Property(x => x.Metodo).HasConversion<string>();
            e.Property(x => x.Concepto).HasConversion<string>();
        });

        b.Entity<ConfigBoutique>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Penalizacion).HasPrecision(12, 2);
        });

        b.Entity<AvisoLeido>(e =>
        {
            e.HasKey(x => new { x.ClientaId, x.AvisoId });
        });

        b.Entity<MovimientoInventario>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Sucursal).HasConversion<string>();
            e.Property(x => x.Motivo).HasConversion<string>();
            e.HasIndex(x => new { x.ProductoId, x.Fecha });
        });
    }
}
