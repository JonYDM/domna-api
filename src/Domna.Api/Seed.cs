using Domna.Application.Abstractions;
using Domna.Domain;
using Domna.Infrastructure.Persistence;

namespace Domna.Api;

/// <summary>
/// Datos de ejemplo para la demo: la clienta María (777 123 4567 / PIN 123456) y una prenda con
/// variantes y stock por sucursal. Solo corre si la base está vacía.
/// </summary>
public static class Seed
{
    public static void Sembrar(DomnaDbContext db, IPasswordHasher hasher)
    {
        if (db.Clientas.Any() || db.Productos.Any() || db.Categorias.Any())
            return;

        var ahora = DateTime.UtcNow;

        // Catálogo de categorías (con sus tallas) y colores base.
        db.Categorias.AddRange(
            Categoria.Crear("vestidos", "Vestidos", new[] { "CH", "M", "G", "XG" }),
            Categoria.Crear("blusas", "Blusas", new[] { "CH", "M", "G" }),
            Categoria.Crear("pantalones", "Pantalones", new[] { "3", "5", "7", "9", "11" }),
            Categoria.Crear("bolsas", "Bolsas", new[] { "Única" }));

        db.ColoresCatalogo.AddRange(
            ColorCatalogo.Crear("rosa", "Rosa", "#F2A7B5"),
            ColorCatalogo.Crear("azul", "Azul marino", "#4A6A92"),
            ColorCatalogo.Crear("negro", "Negro", "#1F1B24"),
            ColorCatalogo.Crear("beige", "Beige", "#D9C4A5"));

        // Staff: dueña Jessica y una cajera de ejemplo (login real con teléfono + PIN).
        db.Clientas.Add(Clienta.CrearStaff(
            "u-jessica", "Jessica", "Jesly", "7774602303", "jessica@jeslyboutique.mx",
            Rol.Duena, hasher.Hash("010101"), ahora));
        db.Clientas.Add(Clienta.CrearStaff(
            "u-caja-temixco", "Caja Temixco", "", "7771004050", "caja.temixco@jeslyboutique.mx",
            Rol.Cajera, hasher.Hash("135790"), ahora, SucursalId.Temixco));

        var maria = Clienta.Crear(
            "c-maria",
            "María",
            "González",
            "777 123 4567",
            "maria@example.com",
            OrigenClienta.Registro,
            ahora,
            hasher.Hash("123456"));
        db.Clientas.Add(maria);

        var vestido = Producto.Crear(
            "p-vestido-demo",
            "Vestido Primavera",
            "Vestido ligero de temporada.",
            "vestidos",
            Silueta.Vestido,
            precio: 599m,
            costo: 280m,
            permiteApartado: true,
            colores: new[]
            {
                new ColorDraft("Rosa", "#F2A7B5", FamiliaColor.Rosa),
                new ColorDraft("Azul", "#4A6A92", FamiliaColor.Azul)
            },
            tallas: new[] { "CH", "M", "G" },
            stock: new Dictionary<SucursalId, IReadOnlyDictionary<string, int[]>>
            {
                [SucursalId.Temixco] = new Dictionary<string, int[]>
                {
                    ["CH"] = new[] { 3, 2 },
                    ["M"] = new[] { 5, 4 },
                    ["G"] = new[] { 2, 1 }
                },
                [SucursalId.Azteca] = new Dictionary<string, int[]>
                {
                    ["CH"] = new[] { 1, 0 },
                    ["M"] = new[] { 2, 3 },
                    ["G"] = new[] { 0, 1 }
                }
            },
            ahora: ahora,
            codigoBarras: new Dictionary<string, string?[]>
            {
                ["CH"] = new string?[] { "7501000000011", "7501000000028" },
                ["M"] = new string?[] { "7501000000035", "7501000000042" },
                ["G"] = new string?[] { "7501000000059", "7501000000066" }
            });
        db.Productos.Add(vestido);

        db.SaveChanges();
    }
}
